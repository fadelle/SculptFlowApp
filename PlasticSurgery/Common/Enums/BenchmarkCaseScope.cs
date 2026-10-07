namespace PlasticSurgery.Common.Enums;

/// <summary>Which cases a run covers. "reviewed" = every case a human has marked reviewed (generated or manual).
/// "generation" = every case from ONE specific "Generate Test Cases" request — the run's GenerationId names it.</summary>
public static class BenchmarkCaseScope
{
    public const string All = "all";
    public const string Generated = "generated";
    public const string Reviewed = "reviewed";
    public const string Generation = "generation";

    public static bool IsValid(string? value) => value is All or Generated or Reviewed or Generation;
}
