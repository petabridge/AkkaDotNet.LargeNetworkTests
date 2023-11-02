namespace OpenTracing.Infrastructure.Configuration;

public enum DispatcherConfig
{
    Defaults,
    ChannelExecutor64,
    DedicatedThreadpool32x16,
}