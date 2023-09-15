using System;
using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Hosting.SBR;
using Akka.Coordination.KubernetesApi;
using Akka.Discovery.KubernetesApi;
using Akka.Hosting;
using Akka.Management;
using Akka.Management.Cluster.Bootstrap;
using Akka.Remote.Hosting;
using AkkaDotNet.Infrastructure.Actors;
using AkkaDotNet.Infrastructure.Persistence;
using AkkaDotNet.Messages;
using Microsoft.Extensions.Configuration;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Cluster.Sharding;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;
using Phobos.Hosting;

namespace AkkaDotNet.Infrastructure.Configuration;

public class OtherRegionMarker{ }

/// <summary>
/// Designed to add Akka.Cluster.Bootstrap and Akka.Management to our applications
/// </summary>
public static class StressHostingExtensions
{
    private const string DedicatedThreadPoolConfig32x16 = @"
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

    private const string ChannelExecutorConfig64 = @"
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

    private const string MaxFrameSize = @"
        akka.remote.dot-netty.tcp.send-buffer-size = 2m
        akka.remote.dot-netty.tcp.receive-buffer-size = 2m
        akka.remote.dot-netty.tcp.maximum-frame-size = 1m
    ";

    public static AkkaConfigurationBuilder WithStressCluster(this AkkaConfigurationBuilder builder, StressOptions options, IEnumerable<string> roles, IConfiguration configuration)
    {
        var clusterOptions = new ClusterOptions() { Roles = roles.ToArray() };
        
        if (options.AkkaClusterOptions.UseKubernetesLease)
        {
            var leaseOption = new KubernetesLeaseOption
            {
                HeartbeatTimeout = TimeSpan.FromSeconds(120),
                HeartbeatInterval = TimeSpan.FromSeconds(12),
                LeaseOperationTimeout = TimeSpan.FromSeconds(5)
            };
            builder.WithKubernetesLease(leaseOption);
            clusterOptions.SplitBrainResolver = new LeaseMajorityOption()
            {
                LeaseImplementation = leaseOption
            };
        }
        else
        {
            clusterOptions.SplitBrainResolver = new KeepMajorityOption();
        }

        if (options.AkkaClusterOptions.UseKubernetesDiscovery)
        {
            // Add Akka.Management support
            builder.WithAkkaManagement(setup =>
            {
                setup.Port = options.AkkaClusterOptions.ManagementPort;
            });
            
            // Add Akka.Management.Cluster.Bootstrap support
            builder.WithClusterBootstrap(setup =>
            {
                setup.ContactPointDiscovery.PortName = "management";
                setup.ContactPointDiscovery.RequiredContactPointsNr = 3;
                setup.ContactPointDiscovery.StableMargin = TimeSpan.FromSeconds(5);
                setup.ContactPointDiscovery.ContactWithAllContactPoints = true;
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
            clusterOptions.SeedNodes = options.AkkaClusterOptions.SeedNodes.ToArray();
        }

        switch (options.DispatcherConfig)
        {
            case DispatcherConfig.ChannelExecutor64:
                builder.AddHocon(ChannelExecutorConfig64, HoconAddMode.Prepend);
                break;
            case DispatcherConfig.DedicatedThreadpool32x16:
                builder.AddHocon(DedicatedThreadPoolConfig32x16, HoconAddMode.Prepend);
                break;
        }

        builder = builder
            .AddHocon(MaxFrameSize, HoconAddMode.Prepend)
            .AddHocon(configuration.GetSection("petabridge"), HoconAddMode.Prepend)
            .WithRemoting("0.0.0.0", options.AkkaClusterOptions.Port, options.AkkaClusterOptions.Hostname)
            .WithClustering(clusterOptions)
            .AddPersistence(options.PersistenceOptions)
            .WithPhobos(AkkaRunMode.AkkaCluster, _ =>
            {
                
            })
            .StartActors((system, _) =>
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