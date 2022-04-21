namespace AkkaDotNet.Messages.Commands;

public sealed class Ping
{
    public static readonly Ping Instance = new Ping();
    private Ping(){}
}