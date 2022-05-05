using System.Text;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Event;

namespace AkkaDotNet.Infrastructure.Actors
{
    public sealed class DispatcherConfigLogger : ReceiveActor
    {
        private sealed class CheckConfig
        {
            public static readonly CheckConfig Instance = new CheckConfig();
            private CheckConfig(){}
        }

        private readonly ILoggingAdapter _log = Context.GetLogger();

        public DispatcherConfigLogger()
        {
            Receive<CheckConfig>(_ =>
            {
                var sysConfig = Context.System.Settings.Config;
                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine("ALL CONFIGS")
                    .AppendLine("---- akka.actor.default-dispatcher ----")
                    .AppendLine(sysConfig.GetConfig("akka.actor.default-dispatcher").ToString(false))
                    .AppendLine()
                    .AppendLine("---- akka.actor.internal-dispatcher ----")
                    .AppendLine(sysConfig.GetConfig("akka.actor.internal-dispatcher").ToString(false))
                    .AppendLine()
                    .AppendLine("---- akka.remote.default-remote-dispatcher ----")
                    .AppendLine(sysConfig.GetConfig("akka.remote.default-remote-dispatcher").ToString(false))
                    .AppendLine()
                    .AppendLine("---- akka.remote.backoff-remote-dispatcher ----")
                    .AppendLine(sysConfig.GetConfig("akka.remote.backoff-remote-dispatcher").ToString(false));
            
                _log.Warning(stringBuilder.ToString());
            
                var t = typeof(ShardRegion).Assembly;
                _log.Warning("Running with version {0} of Akka.Cluster.Sharding", t);
            });
        }

        protected override void PreStart()
        {
            Self.Tell(CheckConfig.Instance);
        }
    }
}

