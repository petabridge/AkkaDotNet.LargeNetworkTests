using System;
using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Event;
using Akka.Hosting;
using AkkaDotNet.BackEnd.Actors;
using AkkaDotNet.Infrastructure;
using AkkaDotNet.Infrastructure.Actors;
using AkkaDotNet.Infrastructure.Configuration;
using AkkaDotNet.Infrastructure.Logging;
using AkkaDotNet.Infrastructure.OpenTelemetry;
using AkkaDotNet.Infrastructure.Sharding;
using AkkaDotNet.Messages;
using Serilog;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder.Logging.ClearProviders().AddConsole().AddSerilog().AddFilter(null, LogLevel.Warning);

var akkaConfiguration = builder.Configuration.GetRequiredSection(nameof(StressOptions)).Get<StressOptions>();

builder.Services.AddAkka(ActorSystemConstants.ActorSystemName, configurationBuilder =>
{
    configurationBuilder.ConfigureLoggers(configBuilder =>
    {
        configBuilder.LogLevel = Akka.Event.LogLevel.DebugLevel;
    });
    configurationBuilder.WithClusterBootstrap(akkaConfiguration,
        new[] { ActorSystemConstants.BackendRole, ActorSystemConstants.DistributedPubSubRole });
    configurationBuilder.WithSerilog(akkaConfiguration.SerilogOptions);
    configurationBuilder.WithReadyCheckActors();
    if (akkaConfiguration.ShardingOptions.Enabled)
    {
        configurationBuilder.WithShardRegion<IWithItem>("items", s => ItemActor.PropsFor(s, akkaConfiguration.DistributedPubSubOptions.Enabled), new ItemShardExtractor(),
                new ShardOptions()
                {
                    RememberEntities = akkaConfiguration.ShardingOptions.RememberEntities,
                    Role = ActorSystemConstants.BackendRole,
                    StateStoreMode = akkaConfiguration.ShardingOptions.UseDData ? StateStoreMode.DData : StateStoreMode.Persistence
                })
            .WithItemMessagingActor();
    }
    //configurationBuilder.AddHoconFile("sharding-upgrade.conf");
});

builder.Services.AddOpenTelemetry();

var app = builder.Build();

// per https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.Prometheus/README.md
app.UseRouting();
app.UseOpenTelemetryPrometheusScrapingEndpoint();
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    
    endpoints.MapGet("/ready", async (ActorRegistry registry) =>
    {
        var readyCheck = registry.Get<ReadyCheckActor>();
        var checkResult = await readyCheck.Ask<ReadyResult>(ReadyCheck.Instance, TimeSpan.FromSeconds(3));
        //if (checkResult.IsReady)
        return Results.StatusCode(200);
        return Results.StatusCode(500);
    });

});


await app.RunAsync();