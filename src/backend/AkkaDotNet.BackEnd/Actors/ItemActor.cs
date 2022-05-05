using System;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Persistence;
using AkkaDotNet.Infrastructure;
using AkkaDotNet.Messages;
using AkkaDotNet.Messages.Commands;
using AkkaDotNet.Messages.Events;

namespace AkkaDotNet.BackEnd.Actors;

public class ItemActor : ReceivePersistentActor
{
    private int _count = 0;
    private readonly bool _pubSubEnabled;
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public static Props PropsFor(string itemId, bool pubSubEnabled)
    {
        return Props.Create(() => new ItemActor(itemId, pubSubEnabled));
    }

    public ItemActor(string persistenceId, bool pubSubEnabled)
    {
        PersistenceId = persistenceId;
        _pubSubEnabled = pubSubEnabled;

        Recover<ItemAdded>(i =>
        {
            _log.Info("Recovery: count was {0} - adding {1}", _count, i.Count);
            _count += i.Count;
        });
        
        Recover<ItemRemoved>(r =>
        {
            _log.Info("Recovery: count was {0} - subtracting {1}", _count, r.Count);
            _count -= r.Count;
        });

        Recover<SnapshotOffer>(o =>
        {
            if (o.Snapshot is int i)
            {
                _count = i;
                _log.Info("Recovered initial count value of [{0}]", i);
            }
        });

        Command<AddItem>(i =>
        {
            var evnt = new ItemAdded(PersistenceId, i.Count);
            Persist(evnt, added =>
            {
                _count += added.Count;
                Sender.Tell(new CommandResponse(i, CommandResult.Ok));
                SaveSnapshotWhenAble();
            });
        });
        
        Command<RemoveItem>(i =>
        {
            var evnt = new ItemRemoved(PersistenceId, i.Count);
            Persist(evnt, removed =>
            {
                _count -= removed.Count;
                Sender.Tell(new CommandResponse(i, CommandResult.Ok));
                SaveSnapshotWhenAble();
            });
        });

        Command<SaveSnapshotSuccess>(s =>
        {
            DeleteMessages(s.Metadata.SequenceNr);
            DeleteSnapshots(new SnapshotSelectionCriteria(s.Metadata.SequenceNr-1));
        });

        Command<ReceiveTimeout>(_ =>
        {
            Context.Parent.Tell(new Passivate(PoisonPill.Instance));
        });
        
        Command<SubscribeAck>(a =>
        {
            _log.Info("Confirmed subscription to [{0}]", a.Subscribe.Topic);
        });

        Command<Ping>(p =>
        {
            Sender.Tell(p);
        });
    }

    private void SaveSnapshotWhenAble()
    {
        if (LastSequenceNr % 25 == 0)
        {
            SaveSnapshot(_count);
        }
    }

    public override string PersistenceId { get; }

    protected override void PreStart()
    {
        Context.SetReceiveTimeout(TimeSpan.FromMinutes(2));
        if (_pubSubEnabled)
        {
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Subscribe(ActorSystemConstants.PingTopicName, Self), Self);
        }
    }
}