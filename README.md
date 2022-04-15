# AkkaDotNet.LargeNetworkTests

This is a configurable Akka.NET application designed to test various dispatcher, heartbeat, and network settings inside large clusters (50-200 nodes) in a production Kubernetes environment.

## Build and Local Deployment
Start by cloning this repository to your local system.

Next - to build this solution you will need to [purchase a Phobos license key](https://phobos.petabridge.com/articles/setup/request.html). They cost $4,000 per year per organization with no node count or seat limitations and comes with a 30 day money-back guarantee.

Once you purchase a [Phobos NuGet keys for your organization](https://phobos.petabridge.com/articles/setup/index.html), you're going to want to open [`NuGet.config`](NuGet.config) and add your key:

```
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <solution>
    <add key="disableSourceControlIntegration" value="true" />
  </solution>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="phobos" value="{your key here}" />
  </packageSources>
</configuration>
```

From there, run the following commad on the prompt:

```
PS> build.cmd Docker
```

This will create the Docker images the solution needs to run inside Kubernetes:

* `akkadotnet.backend`
* `akkadotnet.frontend`

### Local Deployment

To deploy this solution into a local Kubernetes cluster:

```shell
PS> ./k8s/local/deployAll.cmd
```

This will deploy the following services and deployments into the `akkastress` namespace:

```
λ  kubectl -n akkastress get all
NAME                                         READY   STATUS    RESTARTS   AGE
pod/backend-0                                1/1     Running   0          10m
pod/backend-1                                1/1     Running   0          9m41s
pod/backend-2                                1/1     Running   0          9m20s
pod/frontend-0                               1/1     Running   0          10m
pod/frontend-1                               1/1     Running   0          9m41s
pod/frontend-2                               1/1     Running   0          9m18s
pod/grafana-648b8f56c7-c5bb6                 1/1     Running   0          10m
pod/jaeger-6d6c8558f-9rhst                   1/1     Running   0          10m
pod/prometheus-deployment-6b74f44ff4-nvncd   1/1     Running   0          10m
pod/seq-6494b7f6cc-cgvfm                     1/1     Running   0          10m

NAME                            TYPE           CLUSTER-IP       EXTERNAL-IP   PORT(S)                               AGE
service/backend-akka            ClusterIP      None             <none>        9228/TCP,9221/TCP                     10m
service/frontend-akka           ClusterIP      None             <none>        9228/TCP,9221/TCP                     10m
service/frontend-web            LoadBalancer   10.97.130.114    localhost     1880:31388/TCP                        10m
service/grafana-ip-service      LoadBalancer   10.105.167.59    localhost     3000:30982/TCP                        10m
service/jaeger-agent            ClusterIP      None             <none>        5775/UDP,6831/UDP,6832/UDP,5778/TCP   10m
service/jaeger-collector        ClusterIP      10.101.135.216   <none>        14267/TCP,14268/TCP,9411/TCP          10m
service/jaeger-query            LoadBalancer   10.107.162.113   localhost     16686:32493/TCP                       10m
service/prometheus-ip-service   LoadBalancer   10.107.198.85    localhost     9090:30901/TCP                        10m
service/seq                     LoadBalancer   10.107.222.212   localhost     8988:30204/TCP                        10m
service/zipkin                  ClusterIP      None             <none>        9411/TCP                              10m

NAME                                    READY   UP-TO-DATE   AVAILABLE   AGE
deployment.apps/grafana                 1/1     1            1           10m
deployment.apps/jaeger                  1/1     1            1           10m
deployment.apps/prometheus-deployment   1/1     1            1           10m
deployment.apps/seq                     1/1     1            1           10m

NAME                                               DESIRED   CURRENT   READY   AGE
replicaset.apps/grafana-648b8f56c7                 1         1         1       10m
replicaset.apps/jaeger-6d6c8558f                   1         1         1       10m
replicaset.apps/prometheus-deployment-6b74f44ff4   1         1         1       10m
replicaset.apps/seq-6494b7f6cc                     1         1         1       10m

NAME                        READY   AGE
statefulset.apps/backend    3/3     10m
statefulset.apps/frontend   3/3     10m
```

Once the cluster is fully up and running you can explore the application and its associated telemetry via the following Urls:

* [http://localhost:1880](http://localhost:1880) - generates traffic across the Akka.NET cluster inside the `phobos-web` service.
* [http://localhost:16686/](http://localhost:16686/) - Jaeger tracing UI. Allows to explore the traces that are distributed across the different nodes in the cluster.
* [http://localhost:9090/](http://localhost:9090/) - Prometheus query UI.
* [http://localhost:3000/](http://localhost:3000/) - Grafana metrics. Log in using the username **admin** and the password **admin**. It includes some ready-made dashboards you can use to explore Phobos + OpenTelemetry metrics:
    - [Akka.NET Cluster Metrics](http://localhost:3000/d/8Y4JcEfGk/akka-net-cluster-metrics?orgId=1&refresh=10s) - this is a pre-installed version of our [Akka.NET Cluster + Phobos Metrics (Prometheus Data Source) Dashboard](https://grafana.com/grafana/dashboards/13775) on Grafana Cloud, which you can install instantly into your own applications!
* [http://localhost:8988/](http://localhost:8988/) - Seq log aggregation.

There's many more metrics exported by Phobos that you can use to create your own dashboards or extend the existing ones - you can view all of them by going to [http://localhost:1880/metrics](http://localhost:1880/metrics)

### Tearing Down the Cluster
When you're done exploring this sample, you can tear down the cluster by running the following command:

```
PS> ./k8s/destroyAll.cmd
```

This will delete the `phobos-web` namespace and all of the resources inside it.

## Live Azure Kubernetes Service Deployments

To deploy this application for _live stress testing_ you will need to create a [Pulumi](https://pulumi.com/) acccount and [install Pulumi using Chocolatey](https://www.pulumi.com/docs/get-started/install/):

```shell
choco install pulumi
```

Next, you will need to login to the [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/) and login to an active Azure subscription:

```shell
az login
```

> **N.B.** your account will need to be set to a subscription to which you have sufficient access to create and manage your own resource groups.

### Creating the AKS Environment

To create the AKS environment, head to the [/prod](prod) folder:

```
d-----         aks-cluster
d-----         apm-kubernetes
d-----         azure-resource-group
```

Each of these contains a Pulumi project that will need to be deployed via the `pulumi up` command __in this specific order__:

```shell
cd azure-resource-group
pulumi up
cd ../aks-cluster
pulumi up
cd ../apm-kubernetes
pulumi up
```

That will take a few minutes to run - but afterwards you will have a fully functioning AKS environment into which we can deploy our stress testing application.

### Deploying the App

To deploy the stress test application we need to access the [Azure Container Registry](https://azure.microsoft.com/en-us/services/container-registry/) resource created via Pulumi and login to it via the `docker` CLI.

Use the [`docker login` command](https://docs.docker.com/engine/reference/commandline/login/):

```shell
docker login {yourACR}.azurecr.io --username {username} --password {password}
```

This will give you the ability to publish to ACR locally via the `build.cmd` / `build.sh` included inside this repository:

```shell
build.cmd PublishDockerImages --DockerRegistryUrl {yourACR}.azurecr.io
```

#### Troubleshooting

**In case ACR images can't be pulled by AKS** - this is a persistent challenge with Pulumi for some reason, but in case the Pulumi script doesn't resolve the issue automatically you can try the following:

```shell
az aks update -n myAKSCluster -g myResourceGroup --attach-acr <acr-name>
```

See [Authenticate with Azure Container Registry from Azure Kubernetes Service](https://docs.microsoft.com/en-us/azure/aks/cluster-container-registry-integration?tabs=azure-cli) for more details.
