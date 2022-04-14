using System.Collections.Generic;
using Pulumi;
using Pulumi.Kubernetes;
using Pulumi.Kubernetes.Core.V1;
using Pulumi.Kubernetes.Helm.V3;
using Pulumi.Kubernetes.Types.Inputs.Core.V1;
using Pulumi.Kubernetes.Types.Inputs.Meta.V1;

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
        
        
        DeployPrometheusAndGrafana(customResourceOptions, ns);
        DeploySeq(customResourceOptions, seqDiskSize, ns);

    }

    private void DeploySeq(CustomResourceOptions options, int seqDiskSize, Namespace apmNamespace)
    {
        // 2022.1.7378
        
        var seqChartValues = new Dictionary<string, object>()
        {
            /* define Kubelet metrics scraping */
            ["persistence"]  = new Dictionary<string, object>{
                ["enabled"] = true,
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
            }
        };
        
        var seqChart = new Chart("seq-chart", new Pulumi.Kubernetes.Helm.ChartArgs
        {
            Values = seqChartValues,
            Chart = "seq",
            Repo = "datalust",
            Version = "2022.1.7378",
            Namespace = apmNamespace.Metadata.Apply(c => c.Name),
        }, new ComponentResourceOptions()
        {
            Provider = options.Provider,
        });
    }

    private void DeployPrometheusAndGrafana(CustomResourceOptions options, Namespace apmNamespace)
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
                Namespace = apmNamespace.Metadata.Apply(c => c.Name),
                Repo = "prometheus-community",
                Values = prometheusChartValues,
            }, new ComponentResourceOptions()
            {
                Provider = options.Provider,
            });
    }
}
