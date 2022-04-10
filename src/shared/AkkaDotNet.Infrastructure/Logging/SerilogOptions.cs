namespace AkkaDotNet.Infrastructure.Logging;

public class SerilogOptions
{
    public bool EnableSeq { get; set; } = false;
    public string SeqHost { get; set; }
    public int SeqPort { get; set; }
}