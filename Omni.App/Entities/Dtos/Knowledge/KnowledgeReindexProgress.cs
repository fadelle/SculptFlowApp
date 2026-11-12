namespace Omni.App.Entities.Dtos.Knowledge;

/// <summary>How far a client's Knowledge Base re-index is (QA t4-ai-15): every entry re-chunked and re-embedded with the
/// current settings. Kept in memory; a restart forgets it (and a re-index that was running stops: start it again).</summary>
public sealed record KnowledgeReindexProgress(int Total, int Done, int Failed, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt)
{
    public bool Running => FinishedAt is null;
}
