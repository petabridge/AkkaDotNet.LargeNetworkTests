## [0.6.4] / 14 December 2022
- Bumped version

## [0.6.2] / 14 December 2022
- Bumped version

## [0.6.1] / 14 December 2022
- Enabled shard trace compressor and made add / remove item commands more visible in tracing

## [0.5.12] / 25 April 2022
- Added `DistributedPubSub` - but made it optional
- Disabled `DistributedPubSub`
- Set `state-store-mode=persistence`
- Hard code disable DData
- Actually use `ShardOptions`
- Run with Akka.NET v1.5.0-beta

## [0.5.11] / 25 April 2022
- Added `DistributedPubSub` - but made it optional
- Disabled `DistributedPubSub`
- Set `state-store-mode=persistence`
- Hard code disable DData
- Run with Akka.NET v1.5.0-beta


## [0.5.10] / 25 April 2022
- Added `DistributedPubSub` - but made it optional
- Disabled `DistributedPubSub`
- Set `state-store-mode=persistence`
- Run with Akka.NET v1.5.0-beta


## [0.5.9] / 21 April 2022
- Added `DistributedPubSub`
- Run with Akka.NET v1.5.0-beta

## [0.5.5] / 21 April 2022
- Disable `remember-entities`, but keep DData
- Disable K8s lease
- Run with Akka.NET v1.5.0-beta

## [0.5.4] / 20 April 2022
- Disable `remember-entities`, but keep DData
- Enable K8s lease
- Run with Akka.NET v1.5.0-beta

## [0.5.3] / 20 April 2022
- Enable `remember-entities` with `state-store-mode=ddata`
- Log the version of Akka.Cluster.Sharding being used
- Run with Akka.NET v1.5.0-beta

## [0.5.2] / 20 April 2022
- Enable `remember-entities` with `state-store-mode=ddata`
- Log the version of Akka.Cluster.Sharding being used
- Run with Akka.NET v1.4.37

## [0.5.1] / 20 April 2022
- Enable `remember-entities`
- Log the version of Akka.Cluster.Sharding being used
- Run with Akka.NET v1.5.0-beta

## [0.4.9] / 20 April 2022
- Enable `remember-entities`
- Log the version of Akka.Cluster.Sharding being used
- Run with Akka.NET v1.4.37

## [0.4.8] / 20 April 2022
- Enable `remember-entities`
- Log the version of Akka.Cluster.Sharding being used
- Run with Akka.NET v1.4.37

## [0.4.7] / 20 April 2022
- Test with Akka.NET v1.5 with https://github.com/akkadotnet/akka.net/pull/4629 merged in

## [0.4.6] / 19 April 2022
- Arbitrary bump for deployment purposes

## [0.4.5] / 19 April 2022
- Switched Cluster.Sharding `state-store-mode=persistence`

## [0.4.4] / 19 April 2022
- Bumped Phobos to 2.0.2

## [0.4.3] / 15 April 2022
- Bumped Akka.Remote max-frame-size to 1mb, buffer sizes to 2mb.

## [0.4.2] / 15 April 2022
- Bumped Akka.Remote max-frame-size to 1mb, buffer sizes to 2mb.

## [0.3.4] / 15 April 2022
- Added `ObservableGauge` to track total thread counts per process

## [0.3.3] / 15 April 2022
- Upgraded to Akka.NET v1.4.37
- Set `akka.logevel=INFO`
- Added `DispatcherConfigLogger` at `ActorSystem` startup
- Scaled AKS cluster to more cores