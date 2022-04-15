using AkkaDotNet.Messages.Commands;

namespace AkkaDotNet.Messages;

public enum CommandResult
{
    Ok,
    Fail,
    Timeout
}

public sealed record CommandResponse(IItemCommand Command, CommandResult Result);