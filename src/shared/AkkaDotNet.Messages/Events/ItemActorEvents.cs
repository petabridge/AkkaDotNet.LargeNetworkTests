namespace AkkaDotNet.Messages.Events
{
    public interface  IItemEvent : IWithItem{}

    public sealed class ItemAdded : IItemEvent
    {
        public string ItemId { get; }
        public int Count { get; }

        public ItemAdded(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public ItemAdded WithCount(int newCount)
        {
            return new ItemAdded(ItemId, newCount);
        }
    }

    public sealed class ItemRemoved : IItemEvent
    {
        public string ItemId { get; }
        public int Count { get; }

        public ItemRemoved(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public ItemRemoved WithCount(int newCount)
        {
            return new ItemRemoved(ItemId, newCount);
        }
    }
}

