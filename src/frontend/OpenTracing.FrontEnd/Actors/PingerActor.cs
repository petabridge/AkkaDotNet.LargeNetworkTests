using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using AkkaDotNet.Messages.Commands;
using OpenTracing.Infrastructure;

namespace OpenTracing.FrontEnd.Actors;

public class PingerActor : ReceiveActor, IWithTimers
{
    private sealed class DoPing
    {
        public static readonly DoPing Instance = new DoPing();
        private DoPing(){}
    }

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _mediator;
    private readonly ITracer _tracer;

    public PingerActor(ITracer tracer)
    {
        _tracer = tracer;
        _mediator = DistributedPubSub.Get(Context.System).Mediator;
        
        BecomePinging();
    }

    private void WaitingForPing()
    {
        Receive<DoPing>(d =>
        {
            _tracer.BuildSpan("Ping")
                .IgnoreActiveSpan()
                .StartActive();
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
            _tracer.ActiveSpan.Finish();
            _log.Info("Received [{0}] pings from [{1}] nodes", pings.Count, pings.Select(c => c.Path.Address).Distinct().Count());
            Become(WaitingForPing);
            Timers.StartSingleTimer("ping-timer", DoPing.Instance, TimeSpan.FromSeconds(5));
            Context.SetReceiveTimeout(null); // cancel receivetimeout
        });
    }

    public ITimerScheduler Timers { get; set; }

    protected override void PreStart()
    {
        Timers.StartSingleTimer("ping-timer", DoPing.Instance, TimeSpan.FromSeconds(5));
    }
}