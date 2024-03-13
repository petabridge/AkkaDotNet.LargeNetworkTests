using System.CommandLine;
using System.Diagnostics;

namespace BareMetal.Local;

public static class Program
{
    private const int StartPort = 9221;
    private static readonly List<Process> RunningProcesses = new ();
    private static string _workingDirectory;

    static Program()
    {
        var currentProcess = Process.GetCurrentProcess();
        var processPath = currentProcess.MainModule?.FileName ?? Environment.CurrentDirectory;
        _workingDirectory = Path.GetDirectoryName(processPath) ?? Environment.CurrentDirectory;
    }
    
    public static async Task<int> Main(string[] args)
    {
        var backendOption = new Option<int>(
            aliases: new[] { "--backend", "-b" }, 
            description:"Number of backend roles to start.", getDefaultValue: () => 3);

        var rootCommand = new RootCommand("Run AkkaDotNet.LargeNetworkTests on bare metal")
        {
            backendOption
        };
        
        rootCommand.SetHandler(async backend =>
        {
            await Startup(backend);
        }, backendOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task Startup(int nodeCount)
    {
        foreach (var i in Enumerable.Range(0, nodeCount))
        {
            StartBackEnd(StartPort + i * 4);
        }

        StartFrontEnd(5000);
        Console.WriteLine("Press any key to stop.");
        Console.ReadKey();

        var tasks = RunningProcesses.Select(p =>
        {
            p.Kill();
            return p.WaitForExitAsync();
        });
        await Task.WhenAll(tasks);
    }

    private static Process StartBackEnd(int port)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                Arguments = "AkkaDotNet.BackEnd.dll",
                Environment = 
                {
                    ["EnableOpenTelemetry"] = "false",
                    ["StressOptions__AkkaClusterOptions__Port"] = (port + 0).ToString(),
                    ["StressOptions__AkkaClusterOptions__ManagementPort"] = (port + 1).ToString(),
                    ["petabridge__cmd__port"] = (port + 2).ToString(),
                    ["ASPNETCORE_URLS"] = $"http://localhost:{port + 3}",
                }, 
                WorkingDirectory = _workingDirectory
            }
        };
        RunningProcesses.Add(p);
        p.Start();
        return p;
    }

    private static Process StartFrontEnd(int port)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                Arguments = "AkkaDotNet.FrontEnd.dll",
                Environment = 
                {
                    ["EnableOpenTelemetry"] = "false",
                    ["ASPNETCORE_URLS"] = $"http://localhost:{port}",
                    ["petabridge__cmd__port"] = (port + 1).ToString(),
                    ["StressOptions__AkkaClusterOptions__Port"] = (port + 2).ToString(),
                    ["StressOptions__AkkaClusterOptions__ManagementPort"] = (port + 3).ToString(),
                }, 
                WorkingDirectory = _workingDirectory 
            }
        };
        RunningProcesses.Add(p);
        p.Start();
        return p;
    }
}

