namespace AkkaDotNet.Messages.Queries
{
    public sealed class FetchItemCount : IWithItem
    {
        public string ItemId { get; }

        public FetchItemCount(string itemId)
        {
            ItemId = itemId;
        }
    }

    public sealed class FetchItemCountResponse : IWithItem
    {
        public string ItemId { get; }
        public int Count { get; }

        public FetchItemCountResponse(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }
    }
}

