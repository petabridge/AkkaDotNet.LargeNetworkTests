using AkkaDotNet.Infrastructure.Logging;
using AkkaDotNet.Infrastructure.Persistence;

namespace AkkaDotNet.Infrastructure.Configuration;

public class StressOptions
{
    public bool EnablePhobos { get; set; } = false;

    public DistributedPubSubOptions DistributedPubSubOptions { get; set; } = new DistributedPubSubOptions();
    public ShardingOptions ShardingOptions { get; set; } = new ShardingOptions();
    public AkkaClusterOptions AkkaClusterOptions { get; set; } = new AkkaClusterOptions();

    public SerilogOptions SerilogOptions { get; set; } = new SerilogOptions();

    public DispatcherConfig DispatcherConfig { get; set; } = DispatcherConfig.Defaults;

    public PersistenceOptions PersistenceOptions { get; set; } = new PersistenceOptions();
}

public class DistributedPubSubOptions
{
    public bool Enabled { get; set; }
    public int NumTopics { get; set; }
}

public class ShardingOptions
{
    public bool Enabled { get; set; }
    public bool UseDData { get; set; }
    public bool RememberEntities { get; set; }
}

public class AkkaClusterOptions
{
    public string Hostname { get; set; } = "localhost";
    public int Port { get; set; } = 9229;

    /// <summary>
    /// Port used by Akka.Management HTTP
    /// </summary>
    public int ManagementPort { get; set; } = 9228;

    public string[] Roles { get; set; } = Array.Empty<string>();
    public bool UseKubernetesDiscovery { get; set; } = false;

    public bool UseKubernetesLease { get; set; } = false;

    public KubernetesDiscoveryOptions KubernetesDiscoveryOptions { get; set; } = new KubernetesDiscoveryOptions();

    /// <summary>
    /// Used when we aren't doing Kubernetes discovery
    /// </summary>
    public string[] SeedNodes { get; set; } = Array.Empty<string>();
}

public class KubernetesDiscoveryOptions
{
    public string PodNamespace { get; set; } = string.Empty;
    public string PodLabelSelector { get; set; }  = string.Empty;
}