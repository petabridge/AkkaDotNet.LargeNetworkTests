using Akka.Persistence;

namespace AkkaDotNet.BackEnd.Actors;

public class ItemActor : ReceivePersistentActor
{
    private int _count = 0;
    
    public ItemActor(string persistenceId)
    {
        PersistenceId = persistenceId;
    }

    public override string PersistenceId { get; }
}