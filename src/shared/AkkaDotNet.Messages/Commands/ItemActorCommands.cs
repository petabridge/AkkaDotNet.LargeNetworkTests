namespace AkkaDotNet.Messages.Commands;

public interface IItemCommand : IWithItem{}

public sealed record AddItem(string ItemId, int Count) : IItemCommand;

public sealed record RemoveItem(string ItemId, int Count) : IItemCommand;