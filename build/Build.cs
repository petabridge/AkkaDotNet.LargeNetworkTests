using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.DocFX.DocFXTasks;
using System.Text.Json;
using System.IO;
using static Nuke.Common.ChangeLog.ChangelogTasks;
using Nuke.Common.ChangeLog;
using System.Collections.Generic;
using Nuke.Common.Tools.DocFX;
using Nuke.Common.Tools.Docker;
using static Nuke.Common.Tools.Git.GitTasks;
using Nuke.Common.Tools.SignClient;
using Octokit;
using Nuke.Common.Utilities;

[ShutdownDotNetAfterServerBuild]
partial class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Install);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = Configuration.Release;
    
    public const string DefaultNuGet = "https://api.nuget.org/v3/index.json";

    //usage:
    //.\build.cmd createnuget --NugetPrerelease {suffix}
    [Parameter] string NugetPrerelease;

    [GitRepository] readonly GitRepository GitRepository;

    [Parameter] string NugetPublishUrl = DefaultNuGet;
    [Parameter][Secret] string NugetKey;

    [Parameter] string SymbolsPublishUrl;

    [Parameter][Secret] string GitHubToken;
    GitHubClient GitHubClient;

    [Parameter] string DockerRegistryUrl;

    [Parameter] int Port = 8090;

    [Parameter][Secret] string DockerUsername;
    [Parameter][Secret] string DockerPassword;
    
    [Parameter][Secret] string CustomNuGetSource;

    // Directories
    AbsolutePath ToolsDir => RootDirectory / "tools";
    AbsolutePath Output => RootDirectory / "bin";
    AbsolutePath OutputNuget => Output / "nuget";
    AbsolutePath OutputTests => RootDirectory / "TestResults";
    AbsolutePath OutputPerfTests => RootDirectory / "PerfResults";
    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath DocSiteDirectory => RootDirectory / "docs" / "_site";
    public string ChangelogFile => RootDirectory / "RELEASE_NOTES.md";
    public AbsolutePath DocFxDir => RootDirectory / "docs";
    public AbsolutePath DocFxDirJson => DocFxDir / "docfx.json";

    readonly Solution Solution = ProjectModelTasks.ParseSolution(RootDirectory.GlobFiles("*.sln").FirstOrDefault());

    static readonly JsonElement? _githubContext = string.IsNullOrWhiteSpace(EnvironmentInfo.GetVariable<string>("GITHUB_CONTEXT")) ?
        null
        : JsonSerializer.Deserialize<JsonElement>(EnvironmentInfo.GetVariable<string>("GITHUB_CONTEXT"));

    static readonly int BuildNumber = _githubContext.HasValue ? int.Parse(_githubContext.Value.GetProperty("run_number").GetString()) : 0;

    static readonly string PreReleaseVersionSuffix = "beta" + (BuildNumber > 0 ? BuildNumber : DateTime.UtcNow.Ticks.ToString());
    public ChangeLog Changelog => ReadChangelog(ChangelogFile);

    public ReleaseNotes ReleaseNotes => Changelog.ReleaseNotes.OrderByDescending(s => s.Version).FirstOrDefault() ?? throw new ArgumentException("Bad Changelog File. Version Should Exist");

    private string VersionFromReleaseNotes => ReleaseNotes.Version.IsPrerelease ? ReleaseNotes.Version.OriginalVersion : "";
    private string VersionSuffix => NugetPrerelease == "dev" ? PreReleaseVersionSuffix : NugetPrerelease == "" ? VersionFromReleaseNotes : NugetPrerelease;
    public string ReleaseVersion => ReleaseNotes.Version?.ToString() ?? throw new ArgumentException("Bad Changelog File. Define at least one version");

    Target Clean => _ => _
        .Description("Cleans all the output directories")
        .Before(Restore)
        .Executes(() =>
        {
            RootDirectory
            .GlobDirectories("src/**/bin", "src/**/obj", Output, OutputTests, OutputPerfTests, OutputNuget, DocSiteDirectory)
            .ForEach(DeleteDirectory);
            EnsureCleanDirectory(Output);
        });

    Target Restore => _ => _
        .Description("Restores all nuget packages")
        .DependsOn(Clean)
        .Executes(() =>
        {
            var sources = new List<string>(){DefaultNuGet };
            if(CustomNuGetSource != null)
                sources.Add(CustomNuGetSource);
            
            DotNetRestore(s => s
                .DisableProcessLogOutput()
                .DisableProcessLogInvocation()
                .SetProjectFile(Solution));
        });
    Target CreateNuget => _ => _
      .Description("Creates nuget packages")
      .DependsOn(RunTests)
      .Executes(() =>
      {
          var version = ReleaseNotes.Version.ToString();
          var releaseNotes = GetNuGetReleaseNotes(ChangelogFile, GitRepository);

          var projects = SourceDirectory.GlobFiles("**/*.csproj")
          .Except(SourceDirectory.GlobFiles("**/*Tests.csproj", "**/*Tests*.csproj"));
          foreach (var project in projects)
          {
              DotNetPack(s => s
                  .SetProject(project)
                  .SetConfiguration(Configuration)
                  .EnableNoBuild()
                  .SetIncludeSymbols(true)
                  .EnableNoRestore()
                  .SetAssemblyVersion(version)
                  .SetFileVersion(version)
                  .SetVersionPrefix(version)
                  .SetVersionSuffix(VersionSuffix)
                  .SetPackageReleaseNotes(releaseNotes)
                  .SetOutputDirectory(OutputNuget));
          }
      });
    Target DockerLogin => _ => _
        .Description("Docker login command")
        .Before(PushImage)
        .Requires(() => !DockerRegistryUrl.IsNullOrEmpty())
        .Requires(() => !DockerPassword.IsNullOrEmpty())
        .Requires(() => !DockerUsername.IsNullOrEmpty())
        .Executes(() =>
        {
            var settings = new DockerLoginSettings()
                .SetServer(DockerRegistryUrl)
                .SetUsername(DockerUsername)
                .SetPassword(DockerPassword);
            DockerTasks.DockerLogin(settings);
        });
    Target BuildDockerImages => _ => _
        .Description("Build docker image")
        .DependsOn(PublishCode)
        .Executes(() =>
        {
            var version = ReleaseNotes.Version;
            var tagVersion = $"{version.Version.Major}.{version.Version.Minor}.{version.Version.Build}";
            var dockfiles = GetDockerProjects();
            foreach (var dockfile in dockfiles)
            {
                var image = $"{Directory.GetParent(dockfile).Name}".ToLower();
                var tags = new List<string>
                {
                    $"{image}:latest",
                    $"{image}:{tagVersion}"
                };
                if (!string.IsNullOrWhiteSpace(DockerRegistryUrl))
                {
                    tags.Add($"{DockerRegistryUrl}/{image}:latest");
                    tags.Add($"{DockerRegistryUrl}/{image}:{tagVersion}");
                }
                var settings = new DockerBuildSettings()
                 .SetFile(dockfile)
                 .SetPath(Directory.GetParent(dockfile).FullName)
                 .SetTag(tags.ToArray());
                DockerTasks.DockerBuild(settings);
            }
        });
    Target PushImage => _ => _
        .Description("Push image to docker registry")
        //.DependsOn(DockerLogin)
        .Executes(() =>
        {
            var version = ReleaseNotes.Version;
            var tagVersion = $"{version.Version.Major}.{version.Version.Minor}.{version.Version.Build}";
            var dockfiles = GetDockerProjects();
            foreach (var dockfile in dockfiles)
            {
                var image = $"{Directory.GetParent(dockfile).Name}".ToLower();
                var settings = new DockerImagePushSettings()
                    .SetName($"{DockerRegistryUrl}/{image}:{tagVersion}");
                DockerTasks.DockerImagePush(settings);
                
                var latestSettings = new DockerImagePushSettings()
                    .SetName($"{DockerRegistryUrl}/{image}:latest");
                DockerTasks.DockerImagePush(latestSettings);
            }
        });

    public Target Docker => _ => _
    .DependsOn(BuildDockerImages);
    public Target PublishDockerImages => _ => _
    .DependsOn(Docker, PushImage);


    Target PublishNuget => _ => _
    .Description("Publishes .nuget packages to Nuget")
    .After(CreateNuget)
    .OnlyWhenDynamic(() => !NugetPublishUrl.IsNullOrEmpty())
    .OnlyWhenDynamic(() => !NugetKey.IsNullOrEmpty())
    .Triggers(GitHubRelease)
    .Executes(() =>
    {
        var packages = OutputNuget.GlobFiles("*.nupkg", "*.symbols.nupkg").NotNull();
        var shouldPublishSymbolsPackages = !string.IsNullOrWhiteSpace(SymbolsPublishUrl);
        if (!string.IsNullOrWhiteSpace(NugetPublishUrl))
        {
            foreach (var package in packages)
            {
                if (shouldPublishSymbolsPackages)
                {
                    DotNetNuGetPush(s => s
                     .SetTimeout(TimeSpan.FromMinutes(10).Minutes)
                     .SetTargetPath(package)
                     .SetSource(NugetPublishUrl)
                     .SetSymbolSource(SymbolsPublishUrl)
                     .SetApiKey(NugetKey));
                }
                else
                {
                    DotNetNuGetPush(s => s
                      .SetTimeout(TimeSpan.FromMinutes(10).Minutes)
                      .SetTargetPath(package)
                      .SetSource(NugetPublishUrl)
                      .SetApiKey(NugetKey)
                  );
                }
            }
        }
    });

    Target AuthenticatedGitHubClient => _ => _
        .Unlisted()
        .OnlyWhenDynamic(() => !string.IsNullOrWhiteSpace(GitHubToken))
        .Executes(() =>
        {
            GitHubClient = new GitHubClient(new ProductHeaderValue("nuke-build"))
            {
                Credentials = new Credentials(GitHubToken, AuthenticationType.Bearer)
            };
        });
    Target GitHubRelease => _ => _
        .Unlisted()
        .Description("Creates a GitHub release (or amends existing) and uploads the artifact")
        .OnlyWhenDynamic(() => !string.IsNullOrWhiteSpace(GitHubToken))
        .DependsOn(AuthenticatedGitHubClient)
        .Executes(async () =>
        {
            var version = ReleaseNotes.Version.ToString();
            var releaseNotes = GetNuGetReleaseNotes(ChangelogFile);
            Release release;
            var releaseName = $"{version}";
            if (!VersionSuffix.IsNullOrWhiteSpace())
                releaseName = $"{version}-{VersionSuffix}";
            var identifier = GitRepository.Identifier.Split("/");
            var (gitHubOwner, repoName) = (identifier[0], identifier[1]);
            try
            {
                release = await GitHubClient.Repository.Release.Get(gitHubOwner, repoName, releaseName);
            }
            catch (NotFoundException)
            {
                var newRelease = new NewRelease(releaseName)
                {
                    Body = releaseNotes,
                    Name = releaseName,
                    Draft = false,
                    Prerelease = GitRepository.IsOnReleaseBranch()
                };
                release = await GitHubClient.Repository.Release.Create(gitHubOwner, repoName, newRelease);
            }

            foreach (var existingAsset in release.Assets)
            {
                await GitHubClient.Repository.Release.DeleteAsset(gitHubOwner, repoName, existingAsset.Id);
            }

            Information($"GitHub Release {releaseName}");
            var packages = OutputNuget.GlobFiles("*.nupkg", "*.symbols.nupkg").NotNull();
            foreach (var artifact in packages)
            {
                var releaseAssetUpload = new ReleaseAssetUpload(artifact.Name, "application/zip", File.OpenRead(artifact), null);
                var releaseAsset = await GitHubClient.Repository.Release.UploadAsset(release, releaseAssetUpload);
                Information($"  {releaseAsset.BrowserDownloadUrl}");
            }
        });
    Target RunTests => _ => _
        .Description("Runs all the unit tests")
        .DependsOn(Compile)
        .Executes(() =>
        {
            var projects = Solution.GetAllProjects("*.Tests");
            foreach (var project in projects)
            {
                var frameworks = project.GetTargetFrameworks();
                if(frameworks is null)
                    continue;
                Information($"Running tests from {project}");
                foreach (var fw in frameworks)
                {
                    Information($"Running for {project} ({fw}) ...");
                    DotNetTest(c => c
                           .SetProjectFile(project)
                           .SetConfiguration(Configuration.ToString())
                           .SetFramework(fw)
                           .SetResultsDirectory(OutputTests)
                           .SetProcessWorkingDirectory(Directory.GetParent(project).FullName)
                           .SetLoggers("trx")
                           .SetVerbosity(verbosity: DotNetVerbosity.Normal)
                           .EnableNoBuild());
                }
            }
        });
    Target Nuget => _ => _
        .DependsOn(CreateNuget, PublishNuget);
    private AbsolutePath[] GetDockerProjects()
    {
        return SourceDirectory.GlobFiles("**/Dockerfile")// folders with Dockerfiles in it
            .ToArray();
    }
    Target PublishCode => _ => _
        .Unlisted()
        .Description("Publish project as release")
        .DependsOn(RunTests)
        .Executes(() =>
        {
            var dockfiles = GetDockerProjects();
            foreach (var dockfile in dockfiles)
            {
                Information(dockfile.Parent.ToString());
                var project = dockfile.Parent.GlobFiles("*.csproj").First();
                DotNetPublish(s => s
                .SetProject(project)
                .SetConfiguration(Configuration.Release));
            }
        });
    Target All => _ => _
     .Description("Executes NBench, Tests and Nuget targets/commands")
     .DependsOn(BuildRelease, RunTests, NBench, Nuget);

    Target NBench => _ => _
    .Description("Runs all BenchMarkDotNet tests")
    .DependsOn(Compile)
    .Executes(() =>
    {
        RootDirectory
            .GlobFiles("src/**/*.Tests.Performance.csproj")
            .ForEach(path =>
            {
                DotNetRun(s => s
                .SetApplicationArguments($"--no-build -c release --concurrent true --trace true --output {OutputPerfTests} --diagnostic")
                .SetProcessLogOutput(true)
                .SetProcessWorkingDirectory(Directory.GetParent(path).FullName)
                .SetProcessExecutionTimeout((int)TimeSpan.FromMinutes(30).TotalMilliseconds)
                );
            });
    });
    //--------------------------------------------------------------------------------
    // Documentation 
    //--------------------------------------------------------------------------------
    Target DocsInit => _ => _
        .Unlisted()
        .DependsOn(Compile)
        .Executes(() =>
        {
            DocFXInit(s => s.SetOutputFolder(DocFxDir).SetQuiet(true));
        });
    Target DocsMetadata => _ => _
        .Unlisted()
        .Description("Create DocFx metadata")
        .DependsOn(Compile)
        .Executes(() =>
        {
            DocFXMetadata(s => s
            .SetProjects(DocFxDirJson)
            .SetLogLevel(DocFXLogLevel.Verbose));
        });

    Target DocFx => _ => _
        .Description("Builds Documentation")
        .DependsOn(DocsMetadata)
        .Executes(() =>
        {
            DocFXBuild(s => s
            .SetConfigFile(DocFxDirJson)
            .SetLogLevel(DocFXLogLevel.Verbose));
        });

    Target ServeDocs => _ => _
        .Description("Build and preview documentation")
        .DependsOn(DocFx)
        .Executes(() => DocFXServe(s => s.SetFolder(DocFxDir).SetPort(Port)));

    Target Compile => _ => _
        .Description("Builds all the projects in the solution")
        .DependsOn(AssemblyInfo, Restore)
        .Executes(() =>
        {
            var version = ReleaseNotes.Version.ToString();
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });


    Target BuildRelease => _ => _
    .DependsOn(Compile);

    Target AssemblyInfo => _ => _
        .After(Restore)
        .Executes(() =>
        {
            XmlTasks.XmlPoke(SourceDirectory / "Directory.Build.props", "//Project/PropertyGroup/PackageReleaseNotes", GetNuGetReleaseNotes(ChangelogFile));
            XmlTasks.XmlPoke(SourceDirectory / "Directory.Build.props", "//Project/PropertyGroup/VersionPrefix", ReleaseVersion);

        });
    Target Install => _ => _
        .Description("Install `Nuke.GlobalTool` and SignClient")
        .Executes(() =>
        {
            DotNet($"tool install Nuke.GlobalTool --global");
        });

    static void Information(string info)
    {
        Serilog.Log.Information(info);
    }
}
