using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using AkkaDotNet.FrontEnd.Actors;
using AkkaDotNet.Infrastructure;
using AkkaDotNet.Infrastructure.Actors;
using AkkaDotNet.Infrastructure.Configuration;
using AkkaDotNet.Infrastructure.Logging;
using AkkaDotNet.Infrastructure.OpenTelemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AkkaDotNet.FrontEnd
{
    class Program
    {
        public static async Task Main(params string[] args)
        {
            await Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(config =>
                {
                    config.AddEnvironmentVariables();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders().AddConsole().AddSerilog().AddFilter(null, LogLevel.Warning);
                })
                .ConfigureServices((context, services) =>
                { 
                    var akkaConfiguration = context.Configuration.GetRequiredSection(nameof(StressOptions)).Get<StressOptions>();
                    services.AddOpenTelemetry();
                    services.AddAkka(ActorSystemConstants.ActorSystemName, configurationBuilder =>
                    {
                        configurationBuilder.WithClusterBootstrap(akkaConfiguration,
                            new[] { ActorSystemConstants.FrontendRole, ActorSystemConstants.DistributedPubSubRole });
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
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.Configure(app =>
                    {
                        
                        // per https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.Prometheus/README.md
                        app.UseRouting();
                        app.UseOpenTelemetryPrometheusScrapingEndpoint();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();
    
                            endpoints.MapGet("/ready", async req =>
                            {
                                var registry = req.RequestServices.GetRequiredService<ActorRegistry>();
                                var readyCheck = registry.Get<ReadyCheckActor>();
                                var checkResult = await readyCheck.Ask<ReadyResult>(ReadyCheck.Instance, TimeSpan.FromSeconds(3));
                                //if (checkResult.IsReady)
                                req.Response.StatusCode = 200;
                                //req.Response.StatusCode = 500;
                            });
                        });

                    });
                })
                .Build().RunAsync();
        }
    }
}
