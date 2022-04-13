using System.Threading.Tasks;
using Pulumi;
using Pulumi.AzureNative.ContainerRegistry;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;
using SkuName = Pulumi.AzureNative.Storage.SkuName;

class MyStack : Stack
{
    public MyStack()
    {
        var config = new Pulumi.Config();
        var rgName = config.Require("resourceGroupName");
        
        // Create an Azure Resource Group
        var resourceGroup = new ResourceGroup(rgName);

        ResourceGroupId = resourceGroup.Name;

        // Create an Azure resource (Storage Account)
        var storageAccount = new StorageAccount("sa", new StorageAccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Sku = new SkuArgs
            {
                Name = SkuName.Standard_LRS
            },
            Kind = Kind.StorageV2,
        });
        
        // create an Azure Container Registry
        var registry = new Registry($"{rgName}images".ToLowerInvariant(), new RegistryArgs
        {
            ResourceGroupName = resourceGroup.Name, Sku = new Pulumi.AzureNative.ContainerRegistry.Inputs.SkuArgs
            {
                Name = "Standard"
            }
        });
        
        RegistryLoginServer = registry.LoginServer;
        RegistryId = registry.Id;

        // Export the primary key of the Storage Account
        this.PrimaryStorageKey = Output.Tuple(resourceGroup.Name, storageAccount.Name).Apply(names =>
            Output.CreateSecret(GetStorageAccountPrimaryKey(names.Item1, names.Item2)));
    }
    
    [Output]
    public Output<string> ResourceGroupId { get; set; }

    [Output]
    public Output<string> PrimaryStorageKey { get; set; }
    
    
    [Output]
    public Output<string> RegistryLoginServer { get; set; }

    /// <summary>
    /// Needed to assign AD permissions to allow AKS to pull images from this ACR
    /// </summary>
    [Output]
    public Output<string> RegistryId { get; set; }
    
    private static async Task<string> GetStorageAccountPrimaryKey(string resourceGroupName, string accountName)
    {
        var accountKeys = await ListStorageAccountKeys.InvokeAsync(new ListStorageAccountKeysArgs
        {
            ResourceGroupName = resourceGroupName,
            AccountName = accountName
        });
        return accountKeys.Keys[0].Value;
    }
}
