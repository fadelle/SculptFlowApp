namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>Raw per-client counts read from the message trace (repository result; column names match the SQL).</summary>
public class AiUsageCounts
{
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public long Runs { get; set; }
    public long FailedRuns { get; set; }
    public long ModelCalls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}
