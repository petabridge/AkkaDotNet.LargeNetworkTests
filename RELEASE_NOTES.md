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