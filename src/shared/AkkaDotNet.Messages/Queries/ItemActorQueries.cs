namespace AkkaDotNet.Messages.Queries;

public sealed record FetchItemCount(string ItemId) : IWithItem;

public sealed record FetchItemCountResponse(string ItemId, int Count) : IWithItem;