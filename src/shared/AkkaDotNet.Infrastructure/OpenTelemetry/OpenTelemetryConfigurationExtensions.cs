using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Phobos.Actor;

namespace AkkaDotNet.Infrastructure.OpenTelemetry;

public static class OpenTelemetryConfigurationExtensions
{
    public const string AkkaStressSource = "akkastress";
    public static readonly Meter AkkaStressMeter = new Meter(AkkaStressSource);
    
    public static IServiceCollection AddAppInstrumentation(this IServiceCollection services)
    {
        var threadCountGauage =
            AkkaStressMeter.CreateObservableGauge("ProcessThreads", () => Process.GetCurrentProcess().Threads.Count, "threads", "Total threads belonging to this process");
        return services;
    }
    
    public static IServiceCollection AddOpenTelemetry(this IServiceCollection services)
    {
        // Prometheus exporter won't work without this
        services.AddControllers();
        services.AddAppInstrumentation();
            
        var resource = ResourceBuilder.CreateDefault()
            .AddService(Assembly.GetEntryAssembly()!.GetName().Name, serviceInstanceId: $"{Dns.GetHostName()}");

        // enables OpenTelemetry for ASP.NET / .NET Core
        // TODO: need to add config toggle for this
        services.AddOpenTelemetryTracing(builder =>
        {
            builder
                .SetResourceBuilder(resource)
                .AddPhobosInstrumentation(compressShardTraces:true) // eliminate sharding infrastructure from traces
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/metrics");
                })
                .SetSampler(new TraceIdRatioBasedSampler(1.0d))
                .AddJaegerExporter(opt =>
                {
                    opt.AgentHost = Environment.GetEnvironmentVariable(JaegerAgentHostEnvironmentVar) ?? "localhost";
                });
        });

        services.AddOpenTelemetryMetrics(builder =>
        {
            builder
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(Assembly.GetEntryAssembly()!.GetName().Name, serviceInstanceId: $"{Dns.GetHostName()}"))
                .AddPhobosInstrumentation()
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddMeter(AkkaStressSource)
                .AddPrometheusExporter(opt =>
                {
                });
        });

        return services;
    }

    public const string JaegerAgentHostEnvironmentVar = "JAEGER_AGENT_HOST";
}