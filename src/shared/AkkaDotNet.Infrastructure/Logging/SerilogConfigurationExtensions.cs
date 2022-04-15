using System;
using System.Net;
using System.Reflection;
using Akka.Configuration;
using Akka.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace AkkaDotNet.Infrastructure.Logging;

public static class SerilogConfigurationExtensions
{
    public const string ServiceNameProperty = "SERVICE_NAME";
    public const string PodNameProperty = "POD_NAME";
    
    public static readonly Config SerilogConfig =
        @"
        akka.loglevel = INFO
        akka.loggers =[""Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog""]";
    
    public static string GetServiceName()
    {
        var podName = Environment.GetEnvironmentVariable(PodNameProperty);

        return !string.IsNullOrEmpty(podName) ? podName : Dns.GetHostName();
    }

    
    public static AkkaConfigurationBuilder WithSerilog(this AkkaConfigurationBuilder builder, SerilogOptions options)
    {
        var loggerConfiguration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithProperty(PodNameProperty, GetServiceName())
            .Enrich.WithProperty(ServiceNameProperty, Assembly.GetEntryAssembly()?.GetName().Name)
            .WriteTo.Console(
                outputTemplate:
                "[{SERVICE_NAME}][{POD_NAME}][{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}",
                theme: AnsiConsoleTheme.Literate)
            .Filter.ByExcluding(ExcludeHealthChecksNormalEvents).MinimumLevel.Debug(); // Do not want lots of health check info logs in console

        if (options.EnableSeq)
        {
            var serverUrl = $"http://{options.SeqHost}:{options.SeqPort}";
            Console.WriteLine($"Writing logs to {serverUrl}");
            loggerConfiguration = loggerConfiguration
                .WriteTo.Seq(serverUrl)
                .Filter.ByExcluding(ExcludeHealthChecksNormalEvents); // Do not want lots of health check info logs in Seq
        }
        
        // Configure Serilog
        Log.Logger = loggerConfiguration.CreateLogger();
        
        // add to Akka.NET
        return builder.AddHocon(SerilogConfig);
    }
    
    /// <summary>
    /// Filter out health check noise from Seq
    /// </summary>
    /// <param name="ev">The log event to filter</param>
    /// <returns><c>true</c> if we're going to exclude this event.</returns>
    private static bool ExcludeHealthChecksNormalEvents(LogEvent ev)
    {
        var healthCheckRequest = ev.Properties.ContainsKey("RequestPath") &&
                                 (ev.Properties["RequestPath"].ToString() == "\"/env\"" ||
                                  ev.Properties["RequestPath"].ToString() == "\"/ready\"");

        var metricsLog = ev.Properties.ContainsKey("kubernetes_annotations_prometheus.io_path") &&
                         ev.Properties["kubernetes_annotations_prometheus.io_path"].ToString() == "\"/metrics\"";

        return ev.Level < LogEventLevel.Warning && (healthCheckRequest || metricsLog);
    }
}