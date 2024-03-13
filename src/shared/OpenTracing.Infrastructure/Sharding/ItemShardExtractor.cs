using Akka.Cluster.Sharding;
using AkkaDotNet.Messages;

namespace OpenTracing.Infrastructure.Sharding;

public class ItemShardExtractor : HashCodeMessageExtractor
{
    // 200 nodes, 10 shards per node
    public ItemShardExtractor() : base(2000)
    {
    }

    public override string? EntityId(object message)
    {
        if (message is IWithItem itemId)
        {
            return itemId.ItemId;
        }

        return null;
    }
}