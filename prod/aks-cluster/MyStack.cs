using System.Threading.Tasks;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Storage;
using Pulumi.AzureNative.Storage.Inputs;

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
    }
}
