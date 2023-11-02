using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using AkkaDotNet.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTracing.BackEnd.Actors;
using OpenTracing.Infrastructure;
using OpenTracing.Infrastructure.Actors;
using OpenTracing.Infrastructure.Configuration;
using OpenTracing.Infrastructure.Logging;
using OpenTracing.Infrastructure.Sharding;
using Serilog;

namespace OpenTracing.BackEnd;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = new WebHostBuilder()
            .UseKestrel()
            .ConfigureAppConfiguration(builder =>
            {
                builder
                    .AddCommandLine(args)
                    .AddEnvironmentVariables()
                    .AddJsonFile("appsettings.json", false);
            })
            .ConfigureLogging(logging =>
            {
                logging
                    .ClearProviders()
                    .AddConsole()
                    .AddSerilog()
                    .AddFilter(null, LogLevel.Warning);;
            })
            .ConfigureServices( (context, services) =>
            {
                var akkaConfiguration = context.Configuration.GetRequiredSection(nameof(StressOptions)).Get<StressOptions>();
                
                // sets up Akka.NET
                services.AddAkka(ActorSystemConstants.ActorSystemName, configurationBuilder =>
                {
                    configurationBuilder.WithClusterBootstrap(akkaConfiguration,
                        new[] { ActorSystemConstants.BackendRole, ActorSystemConstants.DistributedPubSubRole });
                    configurationBuilder.WithSerilog(akkaConfiguration.SerilogOptions);
                    configurationBuilder.WithReadyCheckActors();
                    
                    if (akkaConfiguration.DistributedPubSubOptions.Enabled)
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
                    
                });
                    
                // enables OpenTracing for ASP.NET Core
                services.AddOpenTracing(o =>
                {
                    o.ConfigureAspNetCore(a =>
                    {
                        a.Hosting.OperationNameResolver = context => $"{context.Request.Method} {context.Request.Path}";

                        // skip Prometheus HTTP /metrics collection from appearing in our tracing system
                        a.Hosting.IgnorePatterns.Add(x => x.Request.Path.StartsWithSegments(new PathString("/metrics")));
                    });
                    o.ConfigureGenericDiagnostics(c => { });
                });

                // sets up Prometheus + ASP.NET Core metrics
                services.ConfigureAppMetrics();

                // sets up Jaeger tracing
                services.ConfigureJaegerTracing();

                services.AddRouting();
                services.AddControllers();
            })
            .Configure((context, app) =>
            {
                if (context.HostingEnvironment.IsDevelopment()) 
                    app.UseDeveloperExceptionPage();

                app.UseRouting();

                // enable App.Metrics routes
                app.UseMetricsAllMiddleware();
                app.UseMetricsAllEndpoints();

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
            })
            .Build();

        await host.RunAsync();
    }
}