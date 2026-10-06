using System.Text;
using System.Text.RegularExpressions;

namespace PlasticSurgery.Business.Contracts.Engines.Knowledge;

/// <summary>
/// Splits a Knowledge Base document's text into chunks sized for embedding/retrieval — a few
/// sentences to a couple of paragraphs each, never one giant vector and never tiny fragments.
/// </summary>
public interface IKnowledgeChunkingService
{
    /// <summary>Chunk size and overlap are the clinic's persisted settings (see IKnowledgeSettingsService),
    /// in approximate tokens.</summary>
    IReadOnlyList<string> Chunk(string content, int chunkSizeTokens, int chunkOverlapTokens);
}
