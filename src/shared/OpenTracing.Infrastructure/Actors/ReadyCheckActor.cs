using Akka.Actor;
using Akka.Cluster;

namespace OpenTracing.Infrastructure.Actors;

public sealed class ReadyCheck
{
    public static readonly ReadyCheck Instance = new ReadyCheck();
    private ReadyCheck(){}
}

public sealed class ReadyResult
{
    public ReadyResult(bool isReady)
    {
        IsReady = isReady;
    }

    public bool IsReady { get; }
}

/// <summary>
/// Responsible for processing health-check pings from /ready endpoint.
///
/// Also used for generating message traffic, optionally, when configured to do so.
/// </summary>
public sealed class ReadyCheckActor : ReceiveActor
{
    private readonly Cluster _cluster = Cluster.Get(Context.System);
    
    public ReadyCheckActor()
    {
        Receive<ReadyCheck>(r =>
        {
            var isReady = _cluster.SelfMember.Status == MemberStatus.Up && !_cluster.IsTerminated;
            Sender.Tell(new ReadyResult(isReady));
        });
    }
}