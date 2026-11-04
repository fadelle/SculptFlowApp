namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>AI bot use per client from <see cref="From"/> to <see cref="To"/>, most model calls first. The estimate uses the
/// prices per million tokens in the settings Ai / InputTokenPriceUsdPerMillion and Ai / OutputTokenPriceUsdPerMillion; only
/// what the trace still holds is counted (MessageTrace / RetentionDays).</summary>
public record AiUsageReport(DateTimeOffset From, DateTimeOffset To, decimal InputTokenPriceUsdPerMillion, decimal OutputTokenPriceUsdPerMillion,
    int TraceRetentionDays, IReadOnlyList<AiUsageRow> Rows);
