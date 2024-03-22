using Akka.Actor;
using Akka.Event;
using Akka.Util;
using AkkaDotNet.Messages;
using AkkaDotNet.Messages.Commands;

namespace AkkaDotNet.Infrastructure.Actors;

public sealed class ItemMessagingActor : ReceiveActor, IWithTimers
{
    private const string ScheduleKey = "writeShard";
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private sealed class WriteShard
    {
        public static readonly WriteShard Instance = new();
        private WriteShard(){}
    }
    
    public ItemMessagingActor(IActorRef itemShardRegion)
    {
        _itemShardRegion = itemShardRegion;

        Receive<WriteShard>(_ =>
        {
            var productId = ThreadLocalRandom.Current.Next(0, 1_000_000).ToString();
            var shouldWrite = ThreadLocalRandom.Current.Next(0,1);
            var countValue = ThreadLocalRandom.Current.Next(0, 10);

            var self = Self;

                /*
                 * N.B. We use `Ask<T>` here because any messages sent by timers are not traced by default.
                 */
            if (shouldWrite == 0)
            {
                _itemShardRegion.Ask<CommandResponse>(new AddItem(productId, countValue)).PipeTo(self);
            }
            else
            {
                _itemShardRegion.Ask<CommandResponse>(new RemoveItem(productId, countValue)).PipeTo(self);
            }
        });

        Receive<CommandResponse>(resp =>
        {
            _log.Info("Command: {0} resulted in {1}", resp.Command, resp.Result);   
        });
    }

    protected override void PreStart()
    {
        Timers!.StartPeriodicTimer(ScheduleKey, WriteShard.Instance, TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(0.1));
    }

    public ITimerScheduler? Timers { get; set; }
    private readonly IActorRef _itemShardRegion;
}