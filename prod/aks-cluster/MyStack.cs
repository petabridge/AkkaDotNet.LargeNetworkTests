using System;
using System.Collections.Generic;
using System.Text;
using Pulumi;
using Pulumi.AzureAD;
using Pulumi.AzureNative.ContainerService;
using Pulumi.AzureNative.ContainerService.Inputs;
using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.Network.Inputs;
using Pulumi.AzureNative.Security;
using Pulumi.Kubernetes.Types.Inputs.Core.V1;
using Pulumi.Kubernetes.Types.Inputs.Meta.V1;
using Pulumi.Random;
using Pulumi.Tls;
using GetClientConfig = Pulumi.AzureNative.Authorization.GetClientConfig;
using ResourceIdentityType = Pulumi.AzureNative.ContainerService.ResourceIdentityType;

class MyStack : Stack
{
    public MyStack()
    {
        var config = new Pulumi.Config();
        var rgStack = new StackReference("petabridge/akkadotnet-largescale-tests/largetests-resource-group");

        // get the Azure Subscription ID of the current sub
        var currentSubscriptionId = Output.Create(GetClientConfig.InvokeAsync()).Apply(c => c.SubscriptionId);

        // need access to resources created in the previous stack
        var rgId = rgStack.RequireOutput("ResourceGroupId").Apply(c => c.ToString());
        var rgName = rgStack.RequireOutput("ResourceGroupName").Apply(c => c.ToString());
        var acrInstanceId = rgStack.RequireOutput("RegistryId").Apply(c => c.ToString());

        var azureStorageConnectionString = rgStack.RequireOutput("StorageConnectionString").Apply(c => c.ToString());

        var aksSku = config.Require("aks.VmSku");
        var aksNodeCount = config.RequireInt32("aks.NodeCount");
        var environmentTag = config.Require("environment");
        var aksDiskSize = config.RequireInt32("diskSize");

        // Create the AD service principal for the K8s cluster.
        var adApp = new Application("aks", new ApplicationArgs
        {
            DisplayName = "largescale-aks",
        });

        var adSp = new ServicePrincipal("aksSp", new ServicePrincipalArgs
        {
            ApplicationId = adApp.ApplicationId
        });
        
        var adSpPassword = new ServicePrincipalPassword("aksSpPassword", new ServicePrincipalPasswordArgs
        {
            ServicePrincipalId = adSp.Id,
            Value = config.RequireSecret("aks.SpPassword"),
            EndDate = "2099-01-01T00:00:00Z",
        });

        // Generate an SSH key
        var sshKey = new PrivateKey("ssh-key", new PrivateKeyArgs
        {
            Algorithm = "RSA",
            RsaBits = 4096
        });

        // Grant networking permissions to the SP (needed e.g. to provision Load Balancers).
        // list of built-in roles https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles
        var assignment = new RoleAssignment("network-role-assignment", new RoleAssignmentArgs
        {
            PrincipalId = adSp.Id,
            PrincipalType = PrincipalType.ServicePrincipal,
            Scope = rgId,
            // "Network Contributor" role definition
            // Need this for creating LoadBalancers et al
            RoleDefinitionId = currentSubscriptionId.Apply(s =>
                $"/subscriptions/{s}/providers/Microsoft.Authorization/roleDefinitions/4d97b98b-1d4f-4787-a291-c67834d212e7"),
        });

        // Create a Virtual Network for the cluster.
        var vnet = new Pulumi.AzureNative.Network.VirtualNetwork("vnet",
            new Pulumi.AzureNative.Network.VirtualNetworkArgs
            {
                ResourceGroupName = rgName,
                AddressSpace = new AddressSpaceArgs()
                {
                    AddressPrefixes = { "10.0.0.0/16" }
                },
            });

        // Create a Subnet for the cluster.
        var subnet = new Pulumi.AzureNative.Network.Subnet("aks-subnet", new Pulumi.AzureNative.Network.SubnetArgs
        {
            ResourceGroupName = rgName,
            VirtualNetworkName = vnet.Name,
            AddressPrefix = "10.0.0.0/16",
        });

        var cluster = new ManagedCluster("akkastress", new ManagedClusterArgs()
        {
            ResourceGroupName = rgName,
            AgentPoolProfiles =
            {
                new ManagedClusterAgentPoolProfileArgs
                {
                    Count = aksNodeCount,
                    MaxPods = 200,
                    Mode = "System",
                    Name = "agentpool",
                    OsDiskSizeGB = aksDiskSize,
                    OsType = "Linux",
                    Type = "VirtualMachineScaleSets",
                    VmSize = aksSku,
                    VnetSubnetID = subnet.Id,
                    AvailabilityZones = "1" // only deploy in a single AZ to keep bandwidth costs down
                }
            },
            DnsPrefix = "akkastress",
            EnableRBAC = true,
            KubernetesVersion = "1.22.6",
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
            ServicePrincipalProfile = new ManagedClusterServicePrincipalProfileArgs
            {
                ClientId = adApp.ApplicationId,
                Secret = adSpPassword.Value
            },
            NetworkProfile = new ContainerServiceNetworkProfileArgs()
            {
                NetworkPlugin = "azure",
                DnsServiceIP = "10.2.3.254",
                ServiceCidr = "10.2.3.0/24",
                DockerBridgeCidr = "172.17.0.1/16",
            },
            Tags =
            {
                { "environment", environmentTag }
            }
        });

        // need to assign ACR Pull permissions to AKS service principal
        var acrAssignment = new RoleAssignment("acr-pull", new RoleAssignmentArgs()
        {
            PrincipalId = adSp.Id,
            PrincipalType = PrincipalType.ServicePrincipal,
            Scope = acrInstanceId,
            RoleDefinitionId = currentSubscriptionId.Apply(s =>
                $"/subscriptions/{s}/providers/Microsoft.Authorization/roleDefinitions/7f951dda-4ed3-4680-a7ca-43fe172d538d"),
        }, new CustomResourceOptions()
        {
            // have to delete and recreate Azure AD role assignments 
            DeleteBeforeReplace = true
        });


        // Export the KubeConfig
        KubeConfig = GetKubeConfig(rgName, cluster.Name);

        // Create a k8s provider pointing to the kubeconfig.
        var k8sProvider = new Pulumi.Kubernetes.Provider("k8s", new Pulumi.Kubernetes.ProviderArgs
        {
            KubeConfig = KubeConfig
        });

        var k8sCustomResourceOptions = new CustomResourceOptions
        {
            Provider = k8sProvider,
        };

        var registryLoginServer = rgStack.RequireOutput("RegistryLoginServer").Apply(c => c.ToString());
        var registryAdminUsername = rgStack.RequireOutput("AdminUsername").Apply(c => c.ToString());
        var registryAdminPassword = rgStack.RequireOutput("AdminPassword").Apply(c => c.ToString());

        // Create a k8s secret for use when pulling images from the container registry when deploying the sample application.
        var dockerCfg = Output.All<string>(registryLoginServer, registryAdminUsername, registryAdminPassword).Apply(
            values =>
            {
                var r = new Dictionary<string, object>();
                var server = values[0];
                var username = values[1];
                var dockerPassword = values[2];

                r[server] = new
                {
                    email = "notneeded@notneeded.com",
                    username,
                    dockerPassword
                };

                return r;
            });

        var dockerCfgString = dockerCfg.Apply(x =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(x))));

        var dockerCfgSecretName = "dockercfg-secret";

        /*
         * This needs to go into the default namespace
         * new SecretArgs
       
         */
        var dockerCfgSecret = new Pulumi.Kubernetes.Core.V1.Secret(dockerCfgSecretName, new SecretArgs()
        {
            Data =
            {
                { ".dockercfg", dockerCfgString }
            },
            Type = "kubernetes.io/dockercfg",
            Metadata = new ObjectMetaArgs
            {
                Name = dockerCfgSecretName,
            }
        }, k8sCustomResourceOptions);

        var azureConnectionStringSecret = new Pulumi.Kubernetes.Core.V1.Secret("azure-storage-secret", new SecretArgs()
        {
            StringData =
            {
                { "AzureConnectionString",  azureStorageConnectionString }   
            },
            Type = "Opaque",
            Metadata = new ObjectMetaArgs
            {
                Name = "azure-connection-string"
            }
        });
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