﻿using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Phobos.Actor;

namespace AkkaDotNet.Infrastructure.OpenTelemetry;

public static class OpenTelemetryConfigurationExtensions
{
    public const string AkkaStressSource = "akkastress";
    public static readonly Meter AkkaStressMeter = new(AkkaStressSource);
    
    public static IServiceCollection AddAppInstrumentation(this IServiceCollection services)
    {
        _ =
            AkkaStressMeter.CreateObservableGauge("ProcessThreads", () => Process.GetCurrentProcess().Threads.Count, "threads", "Total threads belonging to this process");
        return services;
    }
    
    public static IServiceCollection WithOpenTelemetry(this IServiceCollection services)
    {
        // Prometheus exporter won't work without this
        services.AddControllers();
        services.AddAppInstrumentation();
            
        // enables OpenTelemetry for ASP.NET / .NET Core
        // TODO: need to add config toggle for this
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                var resource = ResourceBuilder.CreateDefault()
                    .AddService(Assembly.GetEntryAssembly()?.GetName().Name ?? "OpenTelemetryResource", serviceInstanceId: $"{Dns.GetHostName()}");
                
                builder
                    .SetResourceBuilder(resource)
                    .AddPhobosInstrumentation(compressShardTraces:true) // eliminate sharding infrastructure from traces
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = context => !context.Request.Path.StartsWithSegments("/metrics");
                    })
                    .SetSampler(new TraceIdRatioBasedSampler(1.0d))
                    .AddOtlpExporter(options =>
                    {
                        var endpoint = Environment.GetEnvironmentVariable(JaegerAgentHostEnvironmentVar);
                        if (endpoint is not null)
                        {
                            options.Endpoint = new Uri($"http://{endpoint}:4317");
                            options.Protocol = OtlpExportProtocol.Grpc;
                        }
                    });
            })
            .WithMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(Assembly.GetEntryAssembly()?.GetName().Name ?? "OpenTelemetryMetrics", serviceInstanceId: $"{Dns.GetHostName()}"))
                    .AddPhobosInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    .AddMeter(AkkaStressSource)
                    .AddPrometheusExporter(_ => { });
            });

        return services;
    }

    public const string JaegerAgentHostEnvironmentVar = "JAEGER_AGENT_HOST";
}