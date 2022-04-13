using System;
using System.Text;
using Pulumi;
using Pulumi.AzureAD;
using Pulumi.AzureNative.ContainerService;
using Pulumi.AzureNative.ContainerService.Inputs;
using Pulumi.Random;
using Pulumi.Tls;

class MyStack : Stack
{
    public MyStack()
    {
        var config = new Pulumi.Config();
        var rgStack = new StackReference("petabridge/akkadotnet-largescale-tests/largetests-resource-group");

        // need access to resources created in the previous stack
        var rgName = rgStack.RequireOutput("ResourceGroupId").Apply(c => c.ToString());
        var acrInstanceId = rgStack.RequireOutput("RegistryId").Apply(c => c.ToString());

        var aksSku = config.Require("aks.VmSku");
        var aksNodeCount = config.RequireInt32("aks.NodeCount");
        var environmentTag = config.Require("environment");

        // Create the AD service principal for the K8s cluster.
        var adApp = new Application("aks", new ApplicationArgs
        {
            DisplayName = "largescale-aks",
        });

        var adSp = new ServicePrincipal("aksSp", new ServicePrincipalArgs
        {
            ApplicationId = adApp.ApplicationId
        });

        var randomPassword = new RandomPassword("password", new RandomPasswordArgs
        {
            Length = 20,
            Special = true,
        }).Result;

        var adSpPassword = new ServicePrincipalPassword("aksSpPassword", new ServicePrincipalPasswordArgs
        {
            ServicePrincipalId = adSp.Id,
            Value = randomPassword,
            EndDate = "2099-01-01T00:00:00Z",
        });

        // Generate an SSH key
        var sshKey = new PrivateKey("ssh-key", new PrivateKeyArgs
        {
            Algorithm = "RSA",
            RsaBits = 4096
        });

        var cluster = new ManagedCluster("akkastress", new ManagedClusterArgs()
        {
            ResourceGroupName = rgName,
            AddonProfiles =
            {
                { "KubeDashboard", new ManagedClusterAddonProfileArgs { Enabled = true } }
            },
            AgentPoolProfiles =
            {
                new ManagedClusterAgentPoolProfileArgs
                {
                    Count = aksNodeCount,
                    MaxPods = 200,
                    Mode = "System",
                    Name = "agentpool",
                    OsDiskSizeGB = 100,
                    OsType = "Linux",
                    Type = "VirtualMachineScaleSets",
                    VmSize = aksSku,
                }
            },
            DnsPrefix = "AzureNativeprovider",
            EnableRBAC = true,
            KubernetesVersion = "1.23",
            LinuxProfile = new ContainerServiceLinuxProfileArgs
            {
                AdminUsername = "testuser",
                Ssh = new ContainerServiceSshConfigurationArgs
                {
                    PublicKeys =
                    {
                        new ContainerServiceSshPublicKeyArgs
                        {
                            KeyData = sshKey.PublicKeyOpenssh,
                        }
                    }
                }
            },
            Identity = new ManagedClusterIdentityArgs { Type = ResourceIdentityType.SystemAssigned },
            ServicePrincipalProfile = new ManagedClusterServicePrincipalProfileArgs
            {
                ClientId = adApp.ApplicationId,
                Secret = adSpPassword.Value
            },
            Tags =
            {
                { "environment", environmentTag }
            }
        });

        // Export the KubeConfig
        KubeConfig = GetKubeConfig(rgName, cluster.Name);
    }

    [Output("kubeconfig")] public Output<string> KubeConfig { get; set; }

    private static Output<string> GetKubeConfig(Output<string> resourceGroupName, Output<string> clusterName)
        => ListManagedClusterUserCredentials.Invoke(new ListManagedClusterUserCredentialsInvokeArgs
        {
            ResourceGroupName = resourceGroupName,
            ResourceName = clusterName
        }).Apply(credentials =>
        {
            var encoded = credentials.Kubeconfigs[0].Value;
            var data = Convert.FromBase64String(encoded);
            return Encoding.UTF8.GetString(data);
        });
}