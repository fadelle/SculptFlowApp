using System.Text;
using System.Text.RegularExpressions;

namespace PlasticSurgery.Services;

/// <summary>
/// Splits a Knowledge Base document's text into chunks sized for embedding/retrieval — a few
/// sentences to a couple of paragraphs each, never one giant vector and never tiny fragments.
/// </summary>
public interface IKnowledgeChunkingService
{
    IReadOnlyList<string> Chunk(string content);
}

/// <summary>
/// How it works: (1) split on blank lines into paragraphs; (2) split any paragraph longer than the
/// max into sentences, and sentences longer than the max at a word boundary; (3) greedily pack those
/// units back together up to the max size, so related paragraphs stay in one chunk; (4) a too-small
/// trailing chunk is merged into the one before it; (5) every chunk after the first starts with the
/// tail of the previous one (word-aligned overlap) so a fact straddling a boundary is still
/// retrievable. Sizes come from Knowledge:ChunkMaxChars (1000), ChunkOverlapChars (150) and
/// ChunkMinChars (200); a document shorter than the max is a single chunk.
/// </summary>
public class KnowledgeChunkingService : IKnowledgeChunkingService
{
    private static readonly Regex ParagraphSplit = new(@"\n\s*\n", RegexOptions.Compiled);
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private readonly int _max;
    private readonly int _overlap;
    private readonly int _min;

    public KnowledgeChunkingService(IConfiguration configuration)
    {
        _max = Math.Max(200, ReadInt(configuration, "Knowledge:ChunkMaxChars", 1000));
        _overlap = Math.Clamp(ReadInt(configuration, "Knowledge:ChunkOverlapChars", 150), 0, _max / 2);
        _min = Math.Clamp(ReadInt(configuration, "Knowledge:ChunkMinChars", 200), 0, _max);
    }

    public IReadOnlyList<string> Chunk(string content)
    {
        var text = (content ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return Array.Empty<string>();
        if (text.Length <= _max) return new[] { text };

        var units = new List<string>();
        foreach (var paragraph in ParagraphSplit.Split(text).Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (paragraph.Length <= _max) units.Add(paragraph);
            else units.AddRange(SplitLongParagraph(paragraph));
        }

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var unit in units)
        {
            if (current.Length == 0)
            {
                current.Append(unit);
            }
            else if (current.Length + 2 + unit.Length <= _max)
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

        if (chunks.Count > 1 && chunks[^1].Length < _min)
        {
            chunks[^2] = chunks[^2] + "\n\n" + chunks[^1];
            chunks.RemoveAt(chunks.Count - 1);
        }

        if (_overlap == 0 || chunks.Count == 1) return chunks;

        var withOverlap = new List<string>(chunks.Count) { chunks[0] };
        for (var i = 1; i < chunks.Count; i++)
        {
            withOverlap.Add(TailAtWordBoundary(chunks[i - 1], _overlap) + "\n" + chunks[i]);
        }
        return withOverlap;
    }

    private IEnumerable<string> SplitLongParagraph(string paragraph)
    {
        var current = new StringBuilder();
        foreach (var sentence in SentenceSplit.Split(paragraph).Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            foreach (var piece in sentence.Length <= _max ? new[] { sentence } : HardSplit(sentence))
            {
                if (current.Length > 0 && current.Length + 1 + piece.Length > _max)
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

    private IEnumerable<string> HardSplit(string s)
    {
        while (s.Length > _max)
        {
            var cut = s.LastIndexOf(' ', _max);
            if (cut < _max / 2) cut = _max;
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

    private static int ReadInt(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var v) && v > 0 ? v : fallback;
}
