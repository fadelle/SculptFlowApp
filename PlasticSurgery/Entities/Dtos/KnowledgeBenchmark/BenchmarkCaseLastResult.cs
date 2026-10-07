namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

public record BenchmarkCaseLastResult(
    Guid ResultId,
    Guid RunId,
    string Classification,
    int? ExpectedChunkRank,
    int? ExpectedDocumentBestRank,
    DateTimeOffset RanAt);
