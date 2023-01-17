using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using AkkaDotNet.Infrastructure;
using AkkaDotNet.Messages.Commands;

namespace AkkaDotNet.FrontEnd.Actors;

public class PingerActor : ReceiveActor, IWithTimers
{
    private sealed class DoPing
    {
        public static readonly DoPing Instance = new DoPing();
        private DoPing(){}
    }

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _mediator;

    public PingerActor()
    {
        _mediator = DistributedPubSub.Get(Context.System).Mediator;
        
        BecomePinging();
    }

    private void WaitingForPing()
    {
        Receive<DoPing>(d =>
        {
            _mediator.Tell(new Publish(ActorSystemConstants.PingTopicName, Ping.Instance));
            BecomePinging();
        });
    }

    private void BecomePinging()
    {
        Become(() => Pinging(new HashSet<IActorRef>()));
        Context.SetReceiveTimeout(TimeSpan.FromSeconds(3));
    }

    private void Pinging(HashSet<IActorRef> pings)
    {
        Receive<Ping>(p =>
        {
            pings.Add(Sender);
        });

        Receive<ReceiveTimeout>(r =>
        {
            _log.Info("Received [{0}] pings from [{1}] nodes", pings.Count, pings.Select(c => c.Path.Address).Distinct().Count());
            Become(WaitingForPing);
            Timers.StartSingleTimer("ping-timer", DoPing.Instance, TimeSpan.FromSeconds(5));
            Context.SetReceiveTimeout(null); // cancel receivetimeout
        });
    }

    public ITimerScheduler Timers { get; set; } = null!;

    protected override void PreStart()
    {
        Timers.StartSingleTimer("ping-timer", DoPing.Instance, TimeSpan.FromSeconds(5));
    }
}