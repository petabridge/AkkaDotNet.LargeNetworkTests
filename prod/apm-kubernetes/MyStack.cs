using System.Collections.Generic;
using System.IO;
using Pulumi;
using Pulumi.Kubernetes;
using Pulumi.Kubernetes.Core.V1;
using Pulumi.Kubernetes.Helm.V3;
using Pulumi.Kubernetes.Types.Inputs.Core.V1;
using Pulumi.Kubernetes.Types.Inputs.Meta.V1;
using Pulumi.Kubernetes.Yaml;

class MyStack : Stack
{
    public MyStack()
    {
        var config = new Pulumi.Config();
        var requiredStack = config.Require("requiredStack");
        var aksStack = new StackReference($"petabridge/largescale-aks/{requiredStack}");
        var apmNamespace = config.Require("apmNamespace");

        var seqDiskSize = config.RequireInt32("seqDiskSize");
        
        var provider = new Pulumi.Kubernetes.Provider("k8s-provider", new ProviderArgs()
        {
            KubeConfig = aksStack.RequireOutput("kubeconfig").Apply(c => c.ToString())!
        });

        var customResourceOptions = new CustomResourceOptions()
        {
            Provider = provider
        };

        var ns = new Pulumi.Kubernetes.Core.V1.Namespace("monitoring-k8s-namespace", new NamespaceArgs()
        {
            Metadata = new ObjectMetaArgs
            {
                Name = apmNamespace
            },
            ApiVersion = "v1",
            Kind = "Namespace"
        }, customResourceOptions);
        
        var monitoringProvider = new Pulumi.Kubernetes.Provider("k8s-monitoring-provider", new ProviderArgs()
        {
            KubeConfig = aksStack.RequireOutput("kubeconfig").Apply(c => c.ToString())!,
            Namespace = ns.Metadata.Apply(c => c.Name)
        });
        
        // forces all resources to be created inside the same namespace
        var monitoringResourceOptions = new CustomResourceOptions()
        {
            Provider = monitoringProvider
        };

        
        DeployPrometheusAndGrafana(monitoringResourceOptions, ns);
        DeploySeq(monitoringResourceOptions, seqDiskSize, ns);

    }

    private void DeploySeq(CustomResourceOptions options, int seqDiskSize, Namespace ns)
    {
        // 2022.1.7378
        
        var seqChartValues = new Dictionary<string, object>()
        {
            /* define Kubelet metrics scraping */
            ["persistence"]  = new Dictionary<string, object>{
                ["enabled"] = false,
                ["size"] = seqDiskSize
            },
            ["cache"]  = new Dictionary<string, object>{
                ["targetSize"] = "0.2" // use a smaller cache size https://hub.helm.sh/charts/stable/seq
            },
            ["resources"] = new Dictionary<string, object>{ // set 4Gb memory limit for Seq https://docs.datalust.co/docs/kubernetes-memory
                ["limits"] = new Dictionary<string, object>{
                    ["memory"] = "3Gi"
                },
                ["requests"] = new Dictionary<string, object>{
                    ["memory"] = "3Gi"
                }
            },
            ["service"] = new Dictionary<string, object>
            {
                ["type"] = "LoadBalancer"
            }
        };
        
        var seqChart = new Chart("seq-chart", new Pulumi.Kubernetes.Helm.ChartArgs
        {
            Values = seqChartValues,
            Chart = "seq",
            Repo = "datalust",
            Version = "2022.1.7378",
            Namespace = ns.Metadata.Apply(c => c.Name)
        }, new ComponentResourceOptions()
        {
            Provider = options.Provider,
        });
    }

    private void DeployPrometheusAndGrafana(CustomResourceOptions options, Namespace ns)
    {
            /*
             * Enable Prometheus data storage on a persistent volume claim
             */
            var prometheusChartValues = new Dictionary<string, object>()
            {
                /* define Kubelet metrics scraping */
                ["kubelet"] = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["serviceMonitor"] = new Dictionary<string, object>
                    {
                        ["https"] = false
                    },
                },
                ["kubeControllerManager"] = new Dictionary<string, object>
                {
                    ["enabled"] = false
                },
                ["kubeScheduler"] = new Dictionary<string, object>
                {
                    ["enabled"] = false
                },
                ["kubeEtcd"] = new Dictionary<string, object>
                {
                    ["enabled"] = false
                },
                ["kubeProxy"] = new Dictionary<string, object>
                {
                    ["enabled"] = false
                },
                ["alertManager"] = new Dictionary<string, object>()
                {
                    ["enabled"] = false
                },
                ["configMapReload"] = new Dictionary<string, object>
                {
                    ["enabled"] = false
                },
                ["pushgateway"] = new Dictionary<string, object>
                {
                    ["enabled"] = false
                },
            };

            var prometheusOperatorChart = new Chart("pm", new Pulumi.Kubernetes.Helm.ChartArgs
            {
                Chart = "prometheus",
                Version = "15.8.1",
                Repo = "prometheus-community",
                Values = prometheusChartValues,
                Namespace = ns.Metadata.Apply(c => c.Name)
            }, new ComponentResourceOptions()
            {
                Provider = options.Provider,
            });
            
            
            /*
             * Create secret for loading Prometheus data source
             */
            var dataSourcesSecret = new ConfigFile("grafana-datasource-secret", new ConfigFileArgs()
            {
                File = Path.Combine("Grafana", "datasource-providers.yml")
            }, new ComponentResourceOptions() { Provider = options.Provider, DependsOn = prometheusOperatorChart });
            
            var grafanaChartValues = new Dictionary<string, object>()
            {
                /* define Ingress routing */
                ["dashboardsProvider"] = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                },
                // ["dashboardsConfigMaps"] = new List<object>
                // {
                //     new {
                //         configMapName = "grafana-dash-k8s",
                //         fileName = "k8s-dashboard.json"
                //     },
                //     new {
                //         configMapName = "grafana-dash-akkadotnet",
                //         fileName = "akkadotnet-dashboard.json"
                //     },
                //     new {
                //         configMapName = "grafana-dash-aspnet",
                //         fileName = "aspnetcore-dashboard.json"
                //     }
                // },
                ["datasources"] = new Dictionary<string, object>()
                {
                    ["secretName"] = "grafana-prometheus-datasource"
                },
                ["metrics"] = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                },
                ["imageRenderer"] = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                },
                ["updateStrategy"] = new
                {
                    Type = "Recreate"
                }, // need this or Grafana will hang during an update due to volume mount contention
                ["admin"] = new Dictionary<string, object>
                {
                    ["password"] = "akkastress" // Grafana is protected by CloudFlare Access.
                },
                ["testFramework"] = new Dictionary<string, object>
                {
                    ["enabled"] = false,
                },
                ["persistence"] = new Dictionary<string, object>
                {
                    // have to just disable persistence or re-deploys aren't possible, even with multi-pod volume read access
                    // enabled the underlying SQLite database gets locked and becomes unavailable, so the new pods fail their
                    // readiness and liveness checks
                    ["enabled"] = false, 
                },
                ["service"] = new Dictionary<string, object>
                {
                    ["type"] = "LoadBalancer"
                }
            };

            var grafanaChart = new Chart("grafana", new Pulumi.Kubernetes.Helm.ChartArgs()
            {
                Chart = "grafana",
                Version = "7.6.28",
                Repo = "bitnami",
                Values = grafanaChartValues,
                Namespace = ns.Metadata.Apply(c => c.Name)
            }, new ComponentResourceOptions()
            {
                Provider = options.Provider,
                DependsOn = dataSourcesSecret
            });
    }
}
