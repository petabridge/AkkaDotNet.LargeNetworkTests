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
    public static IServiceCollection AddOpenTelemetry(this IServiceCollection services)
    {
        // Prometheus exporter won't work without this
        services.AddControllers();
            
        var resource = ResourceBuilder.CreateDefault()
            .AddService(Assembly.GetEntryAssembly()!.GetName().Name, serviceInstanceId: $"{Dns.GetHostName()}");

        // enables OpenTelemetry for ASP.NET / .NET Core
        // TODO: leave tracing disabled for now
        // services.AddOpenTelemetryTracing(builder =>
        // {
        //     builder
        //         .SetResourceBuilder(resource)
        //         .AddHttpClientInstrumentation()
        //         .AddAspNetCoreInstrumentation()
        //         .AddJaegerExporter(opt =>
        //         {
        //             opt.AgentHost = Environment.GetEnvironmentVariable(JaegerAgentHostEnvironmentVar);
        //         });
        // });

        services.AddOpenTelemetryMetrics(builder =>
        {
            builder
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(Assembly.GetEntryAssembly()!.GetName().Name, serviceInstanceId: $"{Dns.GetHostName()}"))
                .AddPhobosInstrumentation()
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddPrometheusExporter(opt =>
                {
                });
        });

        return services;
    }
}