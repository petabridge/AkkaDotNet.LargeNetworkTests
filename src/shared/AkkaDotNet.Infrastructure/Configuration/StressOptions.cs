using AkkaDotNet.Infrastructure.Logging;

namespace AkkaDotNet.Infrastructure.Configuration;

public class StressOptions
{
    public DistributedPubSubOptions DistributedPubSubOptions { get; set; }
    public ShardingOptions ShardingOptions { get; set; }
    public AkkaClusterOptions AkkaClusterOptions { get; set; }
    
    public SerilogOptions SerilogOptions { get; set; }
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
    public string Hostname { get; set; }
    public int Port { get;set; }
    
    /// <summary>
    /// Port used by Akka.Management HTTP
    /// </summary>
    public int ManagementPort { get; set; }
    
    public List<string> Roles { get; set; }
    public bool UseKubernetesDiscovery { get; set; }

    public KubernetesDiscoveryOptions KubernetesDiscoveryOptions { get; set; }

    /// <summary>
    /// Used when we aren't doing Kubernetes discovery
    /// </summary>
    public List<string> SeedNodes { get; set; }
}

public class KubernetesDiscoveryOptions
{
    public string PodNamespace { get; set; }
    public string PodLabelSelector { get; set; }
}