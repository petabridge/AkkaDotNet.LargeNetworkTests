using Akka.Actor;
using Akka.Hosting;
using AkkaDotNet.FrontEnd.Actors;
using AkkaDotNet.Infrastructure;
using AkkaDotNet.Infrastructure.Actors;
using AkkaDotNet.Infrastructure.Configuration;
using AkkaDotNet.Infrastructure.Logging;
using AkkaDotNet.Infrastructure.OpenTelemetry;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder.Logging.ClearProviders().AddConsole().AddSerilog().AddFilter(null, LogLevel.Warning);

var akkaConfiguration = builder.Configuration.GetRequiredSection(nameof(StressOptions)).Get<StressOptions>() ?? new StressOptions();

builder.Services.AddAkka(ActorSystemConstants.ActorSystemName, configurationBuilder =>
{
    configurationBuilder.WithStressCluster(akkaConfiguration,
        new[] { ActorSystemConstants.FrontendRole, ActorSystemConstants.DistributedPubSubRole }, builder.Configuration);
    configurationBuilder.WithSerilog(akkaConfiguration.SerilogOptions);
    configurationBuilder.WithReadyCheckActors();
    if (akkaConfiguration.DistributedPubSubOptions.Enabled)
    {
        configurationBuilder.StartActors((system, registry) =>
        {
            var cluster = Akka.Cluster.Cluster.Get(system);
            cluster.RegisterOnMemberUp(() =>
            {
                system.ActorOf(Props.Create(() => new PingerActor()), "pinger");
            });
        });
    }
});

if (akkaConfiguration.EnableOpenTelemetry)
{
    builder.Services.WithOpenTelemetry();    
}

var app = builder.Build();

app.UseRouting();
if (akkaConfiguration.EnableOpenTelemetry)
{
    // per https://github.com/open-telemetry/opentelemetry-dotnet/blob/fe78453c03feb8dbe506b2a0284312bdfa1367c5/src/OpenTelemetry.Exporter.Prometheus.AspNetCore/README.md
    app.UseOpenTelemetryPrometheusScrapingEndpoint();
}
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    
    endpoints.MapGet("/ready", async (ActorRegistry registry) =>
    {
        var readyCheck = registry.Get<ReadyCheckActor>();
        var checkResult = await readyCheck.Ask<ReadyResult>(ReadyCheck.Instance, TimeSpan.FromSeconds(3));
        //if (checkResult.IsReady)
            return Results.StatusCode(200);
        //return Results.StatusCode(500);
    });

});


await app.RunAsync();