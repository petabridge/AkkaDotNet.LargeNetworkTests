using AkkaDotNet.Messages.Commands;

namespace AkkaDotNet.Messages
{
    public enum CommandResult
    {
        Ok,
        Fail,
        Timeout
    }

    public sealed class CommandResponse
    {
        public IItemCommand Command { get; }
        public CommandResult Result { get; }

        public CommandResponse(IItemCommand command, CommandResult result)
        {
            Command = command;
            Result = result;
        }
    }
}

