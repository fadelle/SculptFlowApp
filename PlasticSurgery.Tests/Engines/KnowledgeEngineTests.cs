using PlasticSurgery.Business.Engines.Knowledge;
using PlasticSurgery.Business.Engines.KnowledgeBenchmark;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Tests.Engines;

public class KnowledgeChunkingTests
{
    private static KnowledgeChunkingService Make(int minChars = 0)
    {
        var cfg = new Mock<IConfigManager>();
        cfg.SetupGet(c => c.KnowledgeChunkMinChars).Returns(minChars);
        return new KnowledgeChunkingService(cfg.Object);
    }

    [Fact]
    public void Empty_or_whitespace_gives_no_chunks()
    {
        Assert.Empty(Make().Chunk("   \n ", 100, 0));
        Assert.Empty(Make().Chunk(null!, 100, 0));
    }

    [Fact]
    public void Short_text_is_one_trimmed_chunk() =>
        Assert.Equal(["hello world"], Make().Chunk("  hello world \r\n", 100, 20));

    [Fact]
    public void Paragraphs_are_packed_up_to_the_limit_without_overlap()
    {
        var p = new string('a', 300);
        var text = string.Join("\n\n", p, p, p); // limit = 100 tokens*4 = 400 chars
        var chunks = Make().Chunk(text, 100, 0);
        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(300, c.Length));
    }

    [Fact]
    public void Small_paragraphs_merge_into_one_chunk()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 5).Select(i => new string((char)('a' + i), 150)));
        var chunks = Make().Chunk(text, 100, 0); // 400 max â†’ 2 paragraphs (302) per chunk
        Assert.Equal(3, chunks.Count);
        Assert.Contains("\n\n", chunks[0]);
    }

    [Fact]
    public void Long_paragraph_splits_on_sentences_and_hard_splits_giant_sentences()
    {
        var sentence = string.Concat(Enumerable.Repeat("Word ", 30)).Trim() + "."; // ~150 chars
        var paragraph = string.Join(" ", Enumerable.Repeat(sentence, 6));
        var chunks = Make().Chunk(paragraph, 100, 0);
        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Length <= 400));

        var giant = string.Concat(Enumerable.Repeat("abcdefghi ", 200)); // one 2000-char "sentence"
        var pieces = Make().Chunk(giant, 100, 0);
        Assert.All(pieces, c => Assert.True(c.Length <= 400));
        Assert.True(pieces.Count >= 5);

        var unbroken = new string('x', 1000);
        Assert.All(Make().Chunk(unbroken, 100, 0), c => Assert.True(c.Length <= 400));
    }

    [Fact]
    public void A_tiny_last_chunk_is_merged_into_the_previous_one()
    {
        var text = new string('a', 395) + "\n\n" + "tail";
        Assert.Equal(2, Make(0).Chunk(text, 100, 0).Count);
        var merged = Make(50).Chunk(text, 100, 0);
        Assert.Single(merged);
        Assert.EndsWith("tail", merged[0]);
    }

    [Fact]
    public void Overlap_prefixes_each_later_chunk_with_the_previous_tail_at_a_word_boundary()
    {
        var a = string.Join(" ", Enumerable.Range(0, 38).Select(i => $"wordA{i}"));
        var b = string.Join(" ", Enumerable.Range(0, 38).Select(i => $"wordB{i}"));
        var chunks = Make().Chunk(a + "\n\n" + b, 100, 10); // overlap 40 chars
        Assert.Equal(2, chunks.Count);
        Assert.Equal(a, chunks[0]);
        Assert.StartsWith("wordA", chunks[1]);
        Assert.EndsWith(b, chunks[1]);
        var tail = chunks[1][..(chunks[1].Length - b.Length - 1)];
        Assert.True(tail.Length <= 40 && a.EndsWith(tail));
        Assert.True(tail.Length > 0);
    }
}

public class KnowledgeBenchmarkScorerTests
{
    private readonly KnowledgeBenchmarkScorer _scorer = new();
    private readonly Guid _doc = Guid.NewGuid();
    private readonly Guid _chunk = Guid.NewGuid();

    [Fact]
    public void Exact_hit_at_rank_two()
    {
        var s = _scorer.Score(_doc, _chunk, [new(Guid.NewGuid(), Guid.NewGuid()), new(_doc, _chunk)]);
        Assert.Equal(2, s.ExpectedChunkRank);
        Assert.Equal(2, s.ExpectedDocumentBestRank);
        Assert.False(s.ChunkTop1);
        Assert.True(s.ChunkTop3);
        Assert.Equal(0.5, s.ChunkReciprocalRank);
        Assert.Equal(BenchmarkClassification.ExactChunkHit, s.Classification);
    }

    [Fact]
    public void Same_document_other_chunk_is_a_document_only_hit()
    {
        var s = _scorer.Score(_doc, _chunk, [new(_doc, Guid.NewGuid())]);
        Assert.Null(s.ExpectedChunkRank);
        Assert.Equal(1, s.ExpectedDocumentBestRank);
        Assert.True(s.DocumentTop1);
        Assert.False(s.ChunkTop5);
        Assert.Equal(BenchmarkClassification.DocumentOnlyHit, s.Classification);
    }

    [Fact]
    public void Same_chunk_id_in_another_document_does_not_count()
    {
        var s = _scorer.Score(_doc, _chunk, [new(Guid.NewGuid(), _chunk)]);
        Assert.Equal(BenchmarkClassification.Miss, s.Classification);
        Assert.Equal(0, s.ChunkReciprocalRank);
        Assert.Equal(0, s.DocumentReciprocalRank);
    }

    [Fact]
    public void First_matching_rank_wins_and_top_k_boundaries()
    {
        var s = _scorer.ScoreFromRanks(5, 3);
        Assert.False(s.ChunkTop3);
        Assert.True(s.ChunkTop5);
        Assert.True(s.DocumentTop3);
        Assert.False(s.DocumentTop1);
        Assert.False(_scorer.ScoreFromRanks(6, 6).ChunkTop5);
    }

    [Fact]
    public void Summarize_averages_rates_and_handles_empty()
    {
        Assert.Equal(0, _scorer.Summarize([]).ScoredCases);
        Assert.Null(_scorer.Summarize([]).ChunkTop1);
        var m = _scorer.Summarize([_scorer.ScoreFromRanks(1, 1), _scorer.ScoreFromRanks(null, 2), _scorer.ScoreFromRanks(null, null), _scorer.ScoreFromRanks(4, 4)]);
        Assert.Equal(4, m.ScoredCases);
        Assert.Equal(0.25, m.ChunkTop1);
        Assert.Equal(0.25, m.ChunkTop3);
        Assert.Equal(0.5, m.ChunkTop5);
        Assert.Equal((1 + 0 + 0 + 0.25) / 4, m.ChunkMrr);
        Assert.Equal(0.25, m.DocumentTop1);
        Assert.Equal(0.5, m.DocumentTop3);
        Assert.Equal(0.75, m.DocumentTop5);
    }
}

public class GeneratorResponseParserTests
{
    [Fact]
    public void Parses_object_with_questions_and_case_insensitive_keys()
    {
        var r = GeneratorResponseParser.Parse("""{"GenerationId":"g1","Questions":[{"documentId":"d","chunkId":"c","question":"q?"},"junk"]}""");
        Assert.Equal("g1", r.GenerationId);
        var q = Assert.Single(r.Questions);
        Assert.Equal(("d", "c", "q?"), (q.DocumentId, q.ChunkId, q.Question));
    }

    [Fact]
    public void Parses_n8n_style_array_wrapping_the_object()
    {
        var r = GeneratorResponseParser.Parse("""[{"generationId":"g2","questions":[{"question":"a"}]}]""");
        Assert.Equal("g2", r.GenerationId);
        Assert.Equal("a", Assert.Single(r.Questions).Question);
    }

    [Fact]
    public void Parses_bare_array_of_questions_and_ignores_non_string_ids()
    {
        var r = GeneratorResponseParser.Parse("""[{"question":"a","documentId":5}]""");
        Assert.Null(r.GenerationId);
        Assert.Null(Assert.Single(r.Questions).DocumentId);
        Assert.Empty(GeneratorResponseParser.Parse("[]").Questions);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("\"str\"")]
    [InlineData("{\"questions\":5}")]
    public void Rejects_bad_shapes(string body) =>
        Assert.Throws<BenchmarkGenerationException>(() => GeneratorResponseParser.Parse(body));

    [Fact]
    public void TryParse_never_throws()
    {
        Assert.False(GeneratorResponseParser.TryParse(null, out _));
        Assert.False(GeneratorResponseParser.TryParse("  ", out _));
        Assert.False(GeneratorResponseParser.TryParse("{", out _));
        Assert.True(GeneratorResponseParser.TryParse("[]", out var r));
        Assert.Empty(r.Questions);
    }
}


