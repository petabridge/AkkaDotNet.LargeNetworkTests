namespace AkkaDotNet.Messages.Commands
{
    public interface IItemCommand : IWithItem{}

    public sealed class AddItem : IItemCommand
    {
        public string ItemId { get; }
        public int Count { get; }

        public AddItem(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public AddItem WithCount(int newCount)
        {
            return new AddItem(ItemId, newCount);
        }
    }

    public sealed class RemoveItem : IItemCommand
    {
        public string ItemId { get; }
        public int Count { get; }

        public RemoveItem(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public RemoveItem WithCount(int newCount)
        {
            return new RemoveItem(ItemId, newCount);
        }
    }
}

