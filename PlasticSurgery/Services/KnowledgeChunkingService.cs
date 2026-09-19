using System.Text;
using System.Text.RegularExpressions;

namespace PlasticSurgery.Services;

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

/// <summary>
/// How it works: (1) split on blank lines into paragraphs; (2) split any paragraph longer than the
/// max into sentences, and sentences longer than the max at a word boundary; (3) greedily pack those
/// units back together up to the max size, so related paragraphs stay in one chunk; (4) a too-small
/// trailing chunk is merged into the one before it; (5) every chunk after the first starts with the
/// tail of the previous one (word-aligned overlap) so a fact straddling a boundary is still
/// retrievable. Sizes are configured in tokens but measured as ~4 characters per token (no tokenizer
/// dependency); a document shorter than the max is a single chunk. The minimum trailing-chunk size
/// stays a system setting (Knowledge:ChunkMinChars, default 200).
/// </summary>
public class KnowledgeChunkingService : IKnowledgeChunkingService
{
    /// <summary>Characters per token used to convert the token-based settings (a common rule of thumb
    /// for English text).</summary>
    public const int CharsPerToken = 4;

    private static readonly Regex ParagraphSplit = new(@"\n\s*\n", RegexOptions.Compiled);
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private readonly int _minChars;

    public KnowledgeChunkingService(IConfiguration configuration)
    {
        _minChars = int.TryParse(configuration["Knowledge:ChunkMinChars"], out var v) && v > 0 ? v : 200;
    }

    public IReadOnlyList<string> Chunk(string content, int chunkSizeTokens, int chunkOverlapTokens)
    {
        var max = Math.Max(200, chunkSizeTokens * CharsPerToken);
        var overlap = Math.Clamp(chunkOverlapTokens * CharsPerToken, 0, max / 2);
        var min = Math.Clamp(_minChars, 0, max);

        var text = (content ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return Array.Empty<string>();
        if (text.Length <= max) return new[] { text };

        var units = new List<string>();
        foreach (var paragraph in ParagraphSplit.Split(text).Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (paragraph.Length <= max) units.Add(paragraph);
            else units.AddRange(SplitLongParagraph(paragraph, max));
        }

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var unit in units)
        {
            if (current.Length == 0)
            {
                current.Append(unit);
            }
            else if (current.Length + 2 + unit.Length <= max)
            {
                current.Append("\n\n").Append(unit);
            }
            else
            {
                chunks.Add(current.ToString());
                current.Clear().Append(unit);
            }
        }
        if (current.Length > 0) chunks.Add(current.ToString());

        if (chunks.Count > 1 && chunks[^1].Length < min)
        {
            chunks[^2] = chunks[^2] + "\n\n" + chunks[^1];
            chunks.RemoveAt(chunks.Count - 1);
        }

        if (overlap == 0 || chunks.Count == 1) return chunks;

        var withOverlap = new List<string>(chunks.Count) { chunks[0] };
        for (var i = 1; i < chunks.Count; i++)
        {
            withOverlap.Add(TailAtWordBoundary(chunks[i - 1], overlap) + "\n" + chunks[i]);
        }
        return withOverlap;
    }

    private static IEnumerable<string> SplitLongParagraph(string paragraph, int max)
    {
        var current = new StringBuilder();
        foreach (var sentence in SentenceSplit.Split(paragraph).Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            foreach (var piece in sentence.Length <= max ? new[] { sentence } : HardSplit(sentence, max))
            {
                if (current.Length > 0 && current.Length + 1 + piece.Length > max)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(piece);
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static IEnumerable<string> HardSplit(string s, int max)
    {
        while (s.Length > max)
        {
            var cut = s.LastIndexOf(' ', max);
            if (cut < max / 2) cut = max;
            yield return s[..cut].Trim();
            s = s[cut..].TrimStart();
        }
        if (s.Length > 0) yield return s;
    }

    private static string TailAtWordBoundary(string text, int length)
    {
        if (text.Length <= length) return text;
        var start = text.Length - length;
        var space = text.IndexOf(' ', start);
        return space < 0 ? text[start..] : text[(space + 1)..];
    }
}
