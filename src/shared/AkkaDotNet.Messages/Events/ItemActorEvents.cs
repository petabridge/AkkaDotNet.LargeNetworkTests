namespace AkkaDotNet.Messages.Events;

public interface  IItemEvent : IWithItem{}

public sealed record ItemAdded(string ItemId, int Count) : IItemEvent;

public sealed record ItemRemoved(string ItemId, int Count) : IItemEvent;