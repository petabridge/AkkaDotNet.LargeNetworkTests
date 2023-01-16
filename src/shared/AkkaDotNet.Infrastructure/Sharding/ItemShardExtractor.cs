using Akka.Cluster.Sharding;
using AkkaDotNet.Messages;

namespace AkkaDotNet.Infrastructure.Sharding;

public class ItemShardExtractor : HashCodeMessageExtractor
{
    // 200 nodes, 10 shards per node
    public ItemShardExtractor() : base(30)
    {
    }

    public override string? EntityId(object message)
    {
        if (message is IWithItem itemId)
        {
            return itemId.ItemId;
        }
        
        if(message is ShardRegion.StartEntity startEntity)
        {
            return startEntity.EntityId;
        }

        return null;
    }
}