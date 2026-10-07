using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkDashboardResponse(
    int TotalCases,
    int GeneratedCases,
    int ManualCases,
    int ReviewedCases,
    int StaleCases,
    bool GeneratorConfigured,
    bool RunInProgress,
    /// <summary>A "Generate Test Cases" request is waiting for n8n's callback.</summary>
    bool GenerationInProgress,
    /// <summary>The generation currently waiting for n8n (what "Stop generating" cancels); null when none.</summary>
    Guid? PendingGenerationId,
    /// <summary>The clinic's CURRENT retrieval settings (what a run started now would use).</summary>
    BenchmarkSettingsSnapshot CurrentSettings,
    /// <summary>The most recent run of any status (drives the progress bar).</summary>
    BenchmarkRunSummary? LatestRun,
    /// <summary>The most recent COMPLETED run — the headline metrics come from this one.</summary>
    BenchmarkRunSummary? LatestCompletedRun);
