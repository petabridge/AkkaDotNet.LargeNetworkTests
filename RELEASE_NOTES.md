## [0.4.1] / 15 April 2022
- Disabled ASP.NET Core boilerplate logs

## [0.4.0] / 15 April 2022
- Added `Akka.Persistence.Azure` and Akka.Cluster.Sharding support

## [0.3.4] / 15 April 2022
- Added `ObservableGauge` to track total thread counts per process

## [0.3.3] / 15 April 2022
- Upgraded to Akka.NET v1.4.37
- Set `akka.logevel=INFO`
- Added `DispatcherConfigLogger` at `ActorSystem` startup
- Scaled AKS cluster to more cores