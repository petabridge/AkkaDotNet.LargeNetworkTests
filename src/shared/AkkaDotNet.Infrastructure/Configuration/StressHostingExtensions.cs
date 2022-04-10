using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Management;
using Akka.Management.Cluster.Bootstrap;
using Akka.Remote.Hosting;
using AkkaDotNet.Infrastructure.Actors;

namespace AkkaDotNet.Infrastructure.Configuration;

/// <summary>
/// Designed to add Akka.Cluster.Bootstrap and Akka.Management to our applications
/// </summary>
public static class StressHostingExtensions
{
    public static Config CreateDiscoveryConfig(AkkaClusterOptions options)
    {
        return $@"
            akka{{
              management{{
                http.port = {options.ManagementPort}
                http.hostname = ""
                cluster.bootstrap {{
                    contact-point-discovery {{
                        discovery-method = akka.discovery
                        port-name = management
                        required-contact-point-nr = 2
                        stable-margin = 5s
                        contact-with-all-contact-points = true
                    }}
                }}
              }}
              discovery {{
                method = kubernetes-api
                
                kubernetes-api {{
                    class = ""Akka.Discovery.KubernetesApi.KubernetesApiServiceDiscovery, Akka.Discovery.KubernetesApi""

                    # Namespace to query for pods.
                    # Set this value to a specific string to override discovering the namespace using pod-namespace-path.
                    pod-namespace = ""{options.KubernetesDiscoveryOptions.PodNamespace}""

                    pod-label-selector = ""{options.KubernetesDiscoveryOptions.PodLabelSelector}""
                }}
            }}  
        }}
        ";
    }

    /// <summary>
    /// TODO: can probably incorporate this into Akka.Hosting
    /// </summary>
    public static Config SbrConfig => @"
            akka.cluster{
	        downing-provider-class = ""Akka.Cluster.SplitBrainResolver, Akka.Cluster""
            
            split-brain-resolver {
                active-strategy = keep-majority
            }
        }";
    
    public static AkkaConfigurationBuilder WithClusterBootstrap(this AkkaConfigurationBuilder builder, AkkaClusterOptions options, IEnumerable<string> roles)
    {
        var clusterOptions = new ClusterOptions() { Roles = roles.ToArray() };

        if (options.UseKubernetesDiscovery)
        {
            var bootstrapConfig = CreateDiscoveryConfig(options)
                .WithFallback(ClusterBootstrap.DefaultConfiguration())
                .WithFallback(AkkaManagementProvider.DefaultConfiguration());
            
            builder.AddHocon(bootstrapConfig).WithActors(async (system, registry) =>
            {
                // Akka Management hosts the HTTP routes used by bootstrap
                await AkkaManagement.Get(system).Start();

                // Starting the bootstrap process needs to be done explicitly
                await ClusterBootstrap.Get(system).Start();
            });
        }
        else
        {
            // not using K8s discovery - need to populate some seed nodes
            clusterOptions.SeedNodes = options.SeedNodes.Select(c => Address.Parse(c)).ToArray();
        }

        builder = builder
            .AddHocon(SbrConfig) // need to add SBR regardless of options
            .WithRemoting(options.Hostname, options.Port)
            .WithClustering(clusterOptions);

        return builder;
    }

    public static AkkaConfigurationBuilder WithReadyCheckActors(this AkkaConfigurationBuilder builder)
    {
        return builder.StartActors((system, registry) =>
        {
            var readyCheckActor = system.ActorOf(Props.Create(() => new ReadyCheckActor()), "ready");
            registry.TryRegister<ReadyCheckActor>(readyCheckActor);
        });
    }
}