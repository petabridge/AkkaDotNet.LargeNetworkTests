using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Configuration;
using Akka.Coordination.KubernetesApi;
using Akka.Discovery.KubernetesApi;
using Akka.Hosting;
using Akka.Management;
using Akka.Management.Cluster.Bootstrap;
using Akka.Remote.Hosting;
using AkkaDotNet.Infrastructure.Actors;
using AkkaDotNet.Infrastructure.Persistence;
using AkkaDotNet.Messages;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Cluster.Sharding;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;
using Phobos.Hosting;

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
                http.hostname = """"
                cluster.bootstrap {{
                    contact-point-discovery {{                        
                        port-name = management
                        discovery-method = akka.discovery
                        required-contact-point-nr = 3
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

    public static Config DedicatedThreadPoolConfig32x16 = @"
        akka.actor.internal-dispatcher {
        type = ""Dispatcher""
        executor = ""fork-join-executor""
        throughput = 5

          fork-join-executor {
            parallelism-min = 4
            parallelism-factor = 1.0
            parallelism-max = 32
          }
        }

        akka.remote.default-remote-dispatcher {
            type = Dispatcher
            executor = fork-join-executor
            fork-join-executor {
              parallelism-min = 2
              parallelism-factor = 0.5
              parallelism-max = 16
            }
        }

        akka.remote.backoff-remote-dispatcher {
          executor = fork-join-executor
          fork-join-executor {
            parallelism-min = 2
            parallelism-max = 2
          }
        }
    ";

    public static Config ChannelExecutorConfig64 = @"
        akka.actor.default-dispatcher = {
            executor = channel-executor
            fork-join-executor { #channelexecutor will re-use these settings
              parallelism-min = 2
              parallelism-factor = 1
              parallelism-max = 64
            }
        }

        akka.actor.internal-dispatcher = {
            executor = channel-executor
            throughput = 5
            fork-join-executor {
              parallelism-min = 4
              parallelism-factor = 1.0
              parallelism-max = 64
            }
        }

        akka.remote.default-remote-dispatcher {
            type = Dispatcher
            executor = channel-executor
            fork-join-executor {
              parallelism-min = 2
              parallelism-factor = 0.5
              parallelism-max = 16
            }
        }

        akka.remote.backoff-remote-dispatcher {
          executor = channel-executor
          fork-join-executor {
            parallelism-min = 2
            parallelism-max = 2
          }
        }
    ";

    public static readonly Config MaxFrameSize = @"
        akka.remote.dot-netty.tcp.send-buffer-size = 2m
        akka.remote.dot-netty.tcp.receive-buffer-size = 2m
        akka.remote.dot-netty.tcp.maximum-frame-size = 1m
    ";

    /// <summary>
    /// TODO: can probably incorporate this into Akka.Hosting
    /// </summary>
    public static Config DefaultSbrConfig => @"
            akka.cluster{
	        downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster""
            
            split-brain-resolver {
                active-strategy = keep-majority
                down-all-when-unstable = off
            }
        }";

    public static Config K8sLeaseSbrConfig => ConfigurationFactory.ParseString(@"
         akka.cluster{
	        downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster""
            
            split-brain-resolver {
                active-strategy = lease-majority
                down-all-when-unstable = off
                lease-majority {
                    lease-implementation = ""akka.coordination.lease.kubernetes""
                }
            }
        }
    ").WithFallback(KubernetesLease.DefaultConfiguration);
    
    public static AkkaConfigurationBuilder WithStressCluster(this AkkaConfigurationBuilder builder, StressOptions options, IEnumerable<string> roles)
    {
        var clusterOptions = new ClusterOptions() { Roles = roles.ToArray() };

        if (options.AkkaClusterOptions.UseKubernetesDiscovery)
        {
            // Add Akka.Management support
            builder.WithAkkaManagement(setup =>
            {
                setup.Http.Port = options.AkkaClusterOptions.ManagementPort;
            });
            
            // Add Akka.Management.Cluster.Bootstrap support
            builder.WithClusterBootstrap(setup =>
            {
                setup.ContactPointDiscovery.PortName = "management";
                setup.ContactPointDiscovery.RequiredContactPointsNr = 3;
            }, autoStart: true);
            
            // Add Akka.Discovery.KubernetesApi support
            builder.WithKubernetesDiscovery(c =>
            {
                c.PodNamespace = options.AkkaClusterOptions.KubernetesDiscoveryOptions.PodNamespace;
                c.PodLabelSelector = options.AkkaClusterOptions.KubernetesDiscoveryOptions.PodLabelSelector;
            });
        }
        else
        {
            // not using K8s discovery - need to populate some seed nodes
            if (options.AkkaClusterOptions.SeedNodes != null)
                clusterOptions.SeedNodes = options.AkkaClusterOptions.SeedNodes.ToArray();
        }

        switch (options.DispatcherConfig)
        {
            case DispatcherConfig.ChannelExecutor64:
                builder = builder.AddHocon(ChannelExecutorConfig64, HoconAddMode.Prepend);
                break;
            case DispatcherConfig.DedicatedThreadpool32x16:
                builder = builder.AddHocon(DedicatedThreadPoolConfig32x16, HoconAddMode.Prepend);
                break;
        }

        Debug.Assert(options.AkkaClusterOptions.Port != null, "options.Port != null");
        builder = builder
            .ConfigureLoggers(configBuilder =>
            {
                configBuilder.LogConfigOnStart = true;
            })
            .AddHocon(options.AkkaClusterOptions.UseKubernetesLease ? K8sLeaseSbrConfig : DefaultSbrConfig, HoconAddMode.Prepend) // need to add SBR 
            .AddHocon(MaxFrameSize, HoconAddMode.Prepend)
            .WithRemoting("0.0.0.0", options.AkkaClusterOptions.Port.Value, options.AkkaClusterOptions.Hostname)
            .WithClustering(clusterOptions)
            .AddPersistence(options.PersistenceOptions)
            .WithPhobos(AkkaRunMode.AkkaCluster, configBuilder =>
            {
                configBuilder.WithTracing(tracingConfigBuilder =>
                {
                    tracingConfigBuilder.SetTraceUserActors(false).SetTraceSystemActors(false);
                });
            })
            .StartActors((system, registry) =>
            {
                system.ActorOf(Props.Create(() => new DispatcherConfigLogger()));
            })
            .WithPetabridgeCmd(); // start PetabridgeCmd actors too

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

    public static AkkaConfigurationBuilder WithItemMessagingActor(this AkkaConfigurationBuilder builder)
    {
        return builder.StartActors((system, registry) =>
        {
            var cluster = Akka.Cluster.Cluster.Get(system);
            
            cluster.RegisterOnMemberUp(() =>
            {
                var shardRegion = registry.Get<IWithItem>();
                system.ActorOf(Props.Create(() => new ItemMessagingActor(shardRegion)), "item-messaging");
            });
        });
    }

    public static AkkaConfigurationBuilder WithPetabridgeCmd(this AkkaConfigurationBuilder builder)
    {
        return builder.StartActors((system, registry) =>
        {
            var petabridgeCmd = PetabridgeCmd.Get(system);
            petabridgeCmd.RegisterCommandPalette(ClusterCommands.Instance);
            petabridgeCmd.RegisterCommandPalette(ClusterShardingCommands.Instance);
            petabridgeCmd.RegisterCommandPalette(new RemoteCommands());
            petabridgeCmd.Start();
        });
    }
}