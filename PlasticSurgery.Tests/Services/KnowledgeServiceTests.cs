using Microsoft.Extensions.Configuration;
using PlasticSurgery.Business.Services.Knowledge;

namespace PlasticSurgery.Tests.Services;

public class KnowledgeServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IKnowledgeDocumentRepository> _documents = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<IKnowledgeChunkingService> _chunking = new();
    private readonly Mock<IEmbeddingService> _embeddings = new();
    private readonly Mock<IKnowledgeSettingsService> _settings = new();
    private readonly Mock<IDocumentTextExtractor> _extractor = new();
    private readonly Mock<IConfigManager> _config = new();

    public KnowledgeServiceTests()
    {
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _config.SetupGet(c => c.KnowledgeMaxUploadBytes).Returns(1024 * 1024);
        _settings.Setup(s => s.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new KnowledgeSettingsResponse("model-x", 8, "cosine", "none", 200, 20, 5, 0.3, DateTimeOffset.UtcNow));
        _chunking.Setup(c => c.Chunk(It.IsAny<string>(), 200, 20)).Returns((string text, int _, int _) => new[] { text[..Math.Min(5, text.Length)], "second" });
        _embeddings.Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), "model-x", 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> t, string? _, int? _, CancellationToken _) => t.Select(_ => new float[8]).ToList());
    }

    private KnowledgeService Sut() => new(_documents.Object, _uow.Object, _chunking.Object, _embeddings.Object, _settings.Object, _extractor.Object, _config.Object);

    private KnowledgeDocument Doc(string source = KnowledgeSourceType.Manual) => new()
    {
        Id = Guid.NewGuid(), ClinicId = _clinicId, Title = "T", Category = "faq", Content = "old content", SourceType = source, IsActive = true
    };

    private KnowledgeDocument Found(KnowledgeDocument d)
    {
        _documents.Setup(r => r.GetAsync(_clinicId, d.Id, It.IsAny<CancellationToken>())).ReturnsAsync(d);
        _documents.Setup(r => r.CountChunksAsync(d.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        return d;
    }

    [Fact]
    public void MaxUploadBytes_comes_from_config() => Assert.Equal(1024 * 1024, Sut().MaxUploadBytes);

    [Fact]
    public async Task List_includes_chunk_counts()
    {
        var d1 = Doc();
        var d2 = Doc();
        _documents.Setup(r => r.ListForClinicAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeDocument> { d1, d2 });
        _documents.Setup(r => r.CountChunksByDocumentAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, int> { [d1.Id] = 4 });
        var list = await Sut().ListAsync(_clinicId);
        Assert.Equal([4, 0], list.Select(l => l.ChunkCount));
    }

    [Fact]
    public async Task GetById_maps_or_returns_null()
    {
        Assert.Null(await Sut().GetByIdAsync(_clinicId, Guid.NewGuid()));
        var d = Found(Doc());
        Assert.Equal(2, (await Sut().GetByIdAsync(_clinicId, d.Id))!.ChunkCount);
    }

    [Fact]
    public async Task Create_normalises_chunks_embeds_with_the_title_and_saves_in_one_transaction()
    {
        KnowledgeDocument? added = null;
        _documents.Setup(r => r.Add(It.IsAny<KnowledgeDocument>())).Callback<KnowledgeDocument>(d => added = d);

        var r = await Sut().CreateAsync(_clinicId, new SaveKnowledgeRequest("  Pricing  ", " Pricing ", "  Rhinoplasty costs X  "));

        Assert.Equal(("Pricing", "pricing", "Rhinoplasty costs X", KnowledgeSourceType.Manual), (added!.Title, added.Category, added.Content, added.SourceType));
        Assert.Equal(2, r.ChunkCount);
        _embeddings.Verify(e => e.EmbedBatchAsync(It.Is<IReadOnlyList<string>>(t => t[0].StartsWith("Pricing\n\nRhino")), "model-x", 8, It.IsAny<CancellationToken>()), Times.Once);
        _documents.Verify(d => d.InsertChunksAsync(_clinicId, added.Id, It.Is<IReadOnlyList<string>>(p => p.Count == 2), It.Is<IReadOnlyList<float[]>>(v => v.Count == 2), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_defaults_blank_category_to_general()
    {
        KnowledgeDocument? added = null;
        _documents.Setup(r => r.Add(It.IsAny<KnowledgeDocument>())).Callback<KnowledgeDocument>(d => added = d);
        await Sut().CreateAsync(_clinicId, new SaveKnowledgeRequest("T", " ", "body"));
        Assert.Equal(KnowledgeCategory.General, added!.Category);
    }

    [Theory]
    [InlineData("", "general", "body")]
    [InlineData("   ", "general", "body")]
    [InlineData("T", "general", "  ")]
    public async Task Create_rejects_blank_title_or_content(string title, string category, string content)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clinicId, new SaveKnowledgeRequest(title, category, content)));
        _documents.Verify(d => d.Add(It.IsAny<KnowledgeDocument>()), Times.Never);
    }

    [Fact]
    public async Task Create_rejects_overlong_title_and_category()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clinicId, new SaveKnowledgeRequest(new string('x', 201), "faq", "b")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clinicId, new SaveKnowledgeRequest("T", new string('x', 51), "b")));
    }

    [Fact]
    public async Task Create_fails_without_saving_when_chunking_yields_nothing_or_embedding_fails()
    {
        _chunking.Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).Returns(Array.Empty<string>());
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clinicId, new SaveKnowledgeRequest("T", "faq", "body")));

        _chunking.Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).Returns(new[] { "a" });
        _embeddings.Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("openai down"));
        await Assert.ThrowsAsync<HttpRequestException>(() => Sut().CreateAsync(_clinicId, new SaveKnowledgeRequest("T", "faq", "body")));
        _documents.Verify(d => d.Add(It.IsAny<KnowledgeDocument>()), Times.Never);
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- upload

    [Theory]
    [InlineData("", 10, "Choose a file")]
    [InlineData("a.txt", 0, "empty")]
    [InlineData("a.txt", -1, "Choose a file")]
    [InlineData("a.txt", 2 * 1024 * 1024, "maximum upload size")]
    public async Task Upload_rejects_bad_files(string name, long length, string message)
    {
        var ex = await Assert.ThrowsAsync<InvalidDocumentException>(() => Sut().CreateFromUploadAsync(_clinicId, new UploadKnowledgeRequest(null, "faq"), name, new MemoryStream(), length));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public async Task Upload_extracts_text_and_defaults_the_title_to_the_file_name()
    {
        _extractor.Setup(e => e.ExtractAsync(It.IsAny<Stream>(), "price list.pdf", It.IsAny<CancellationToken>())).ReturnsAsync("Extracted text");
        KnowledgeDocument? added = null;
        _documents.Setup(r => r.Add(It.IsAny<KnowledgeDocument>())).Callback<KnowledgeDocument>(d => added = d);

        var r = await Sut().CreateFromUploadAsync(_clinicId, new UploadKnowledgeRequest(null, "pricing"), @"C:\fakepath\price list.pdf", new MemoryStream(), 1234);

        Assert.Equal(("price list", KnowledgeSourceType.Upload, "price list.pdf", 1234, "Extracted text"), (added!.Title, added.SourceType, added.OriginalFileName, added.FileSizeBytes, added.Content));
        Assert.Equal("application/pdf", added.MimeType);
        Assert.Equal(added.Id, r.Id);
    }

    [Fact]
    public async Task Upload_truncates_an_overlong_title_and_keeps_an_explicit_one()
    {
        _extractor.Setup(e => e.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("text");
        KnowledgeDocument? added = null;
        _documents.Setup(r => r.Add(It.IsAny<KnowledgeDocument>())).Callback<KnowledgeDocument>(d => added = d);
        await Sut().CreateFromUploadAsync(_clinicId, new UploadKnowledgeRequest(new string('t', 250), "faq"), "a.txt", new MemoryStream(), 5);
        Assert.Equal(200, added!.Title.Length);
        await Sut().CreateFromUploadAsync(_clinicId, new UploadKnowledgeRequest("My title", "faq", false), "a.txt", new MemoryStream(), 5);
        Assert.Equal(("My title", false), (added.Title, added.IsActive));
    }

    [Fact]
    public async Task CreateFromSource_keeps_the_source_url_and_type()
    {
        KnowledgeDocument? added = null;
        _documents.Setup(r => r.Add(It.IsAny<KnowledgeDocument>())).Callback<KnowledgeDocument>(d => added = d);
        await Sut().CreateFromSourceAsync(_clinicId, new ExternalKnowledgeDocument(KnowledgeSourceType.Website, "Home", "general", "text", true, "https://x.com/"));
        Assert.Equal((KnowledgeSourceType.Website, "https://x.com/"), (added!.SourceType, added.SourceUrl));
    }

    // ---------------------------------------------------------------- update / replace / toggle / delete

    [Fact]
    public async Task Update_rewrites_the_chunks_for_a_manual_document()
    {
        Assert.Null(await Sut().UpdateAsync(_clinicId, Guid.NewGuid(), new SaveKnowledgeRequest("T", "faq", "c")));
        var d = Found(Doc());
        var r = await Sut().UpdateAsync(_clinicId, d.Id, new SaveKnowledgeRequest("New", "policy", "new content", false));
        Assert.Equal(("New", "policy", "new content", false), (d.Title, d.Category, d.Content, d.IsActive));
        _documents.Verify(x => x.DeleteChunksAsync(d.Id, It.IsAny<CancellationToken>()), Times.Once);
        _documents.Verify(x => x.InsertChunksAsync(_clinicId, d.Id, It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<float[]>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, r!.ChunkCount);
    }

    [Fact]
    public async Task Update_never_changes_the_content_of_uploaded_or_website_documents()
    {
        var d = Found(Doc(KnowledgeSourceType.Upload));
        await Sut().UpdateAsync(_clinicId, d.Id, new SaveKnowledgeRequest("Renamed", "faq", "attempted overwrite"));
        Assert.Equal(("Renamed", "old content"), (d.Title, d.Content));
    }

    [Fact]
    public async Task ReplaceSourceContent_swaps_text_and_chunks_but_keeps_category_and_active_flag()
    {
        Assert.Null(await Sut().ReplaceSourceContentAsync(_clinicId, Guid.NewGuid(), "T", "c"));
        var d = Doc(KnowledgeSourceType.Website);
        d.IsActive = false;
        Found(d);
        var r = await Sut().ReplaceSourceContentAsync(_clinicId, d.Id, " Page ", " fresh ");
        Assert.Equal(("Page", "fresh", "faq", false), (d.Title, d.Content, d.Category, d.IsActive));
        Assert.Equal(2, r!.ChunkCount);
        _documents.Verify(x => x.DeleteChunksAsync(d.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetActive_toggles_without_touching_chunks()
    {
        Assert.Null(await Sut().SetActiveAsync(_clinicId, Guid.NewGuid(), false));
        var d = Found(Doc());
        var r = await Sut().SetActiveAsync(_clinicId, d.Id, false);
        Assert.False(d.IsActive);
        Assert.False(r!.IsActive);
        _documents.Verify(x => x.DeleteChunksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_removes_chunks_then_document()
    {
        Assert.False(await Sut().DeleteAsync(_clinicId, Guid.NewGuid()));
        var d = Found(Doc());
        Assert.True(await Sut().DeleteAsync(_clinicId, d.Id));
        _documents.Verify(x => x.DeleteChunksAsync(d.Id, It.IsAny<CancellationToken>()), Times.Once);
        _documents.Verify(x => x.Remove(d), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class KnowledgeSettingsServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IKnowledgeSettingsRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IConfigManager> _config = new();

    public KnowledgeSettingsServiceTests()
    {
        _config.SetupGet(c => c.KnowledgeMinChunkSizeTokens).Returns(50);
        _config.SetupGet(c => c.KnowledgeMaxChunkSizeTokens).Returns(1000);
        _config.SetupGet(c => c.KnowledgeMinTopK).Returns(1);
        _config.SetupGet(c => c.KnowledgeMaxTopK).Returns(20);
        _config.SetupGet(c => c.KnowledgeChunkMaxChars).Returns(3200);
        _config.SetupGet(c => c.KnowledgeChunkOverlapChars).Returns(400);
        _config.SetupGet(c => c.KnowledgeMinScore).Returns(0.25m);
        _config.SetupGet(c => c.EmbeddingsModel).Returns("text-embedding-3-small");
        _config.SetupGet(c => c.EmbeddingsDimensions).Returns(1536);
    }

    private KnowledgeSettingsService Sut() => new(_repo.Object, _uow.Object, new ConfigurationBuilder().Build(), _config.Object);

    [Fact]
    public async Task Get_returns_the_stored_row_without_creating()
    {
        _repo.Setup(r => r.GetReadOnlyAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSearchSettings { ChunkSizeTokens = 123, TopK = 7 });
        var s = await Sut().GetAsync(_clinicId);
        Assert.Equal((123, 7), (s.ChunkSizeTokens, s.TopK));
        _repo.Verify(r => r.Add(It.IsAny<KnowledgeSearchSettings>()), Times.Never);
    }

    [Fact]
    public async Task Get_creates_defaults_from_configuration_on_first_use()
    {
        KnowledgeSearchSettings? created = null;
        _repo.Setup(r => r.Add(It.IsAny<KnowledgeSearchSettings>())).Callback<KnowledgeSearchSettings>(s => created = s);
        var s = await Sut().GetAsync(_clinicId);
        Assert.Equal((800, 100, 5, 0.25, "text-embedding-3-small", 1536), (s.ChunkSizeTokens, s.ChunkOverlapTokens, s.TopK, s.MinimumSimilarity, s.EmbeddingModel, s.VectorDimension));
        Assert.Equal(_clinicId, created!.ClinicId);
        Assert.Equal(KnowledgeSimilarityMethod.Cosine, s.SimilarityMethod);
    }

    [Fact]
    public async Task Get_clamps_default_chunk_size_and_overlap()
    {
        _config.SetupGet(c => c.KnowledgeChunkMaxChars).Returns(40);        // 10 tokens < min 50
        _config.SetupGet(c => c.KnowledgeChunkOverlapChars).Returns(4000);  // 1000 tokens > half of chunk
        var s = await Sut().GetAsync(_clinicId);
        Assert.Equal((50, 25), (s.ChunkSizeTokens, s.ChunkOverlapTokens));
    }

    [Fact]
    public async Task Get_yields_to_a_concurrent_creator()
    {
        var existing = new KnowledgeSearchSettings { ChunkSizeTokens = 321 };
        var calls = 0;
        _repo.Setup(r => r.GetReadOnlyAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => ++calls == 1 ? null : existing);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DuplicateRecordException(new Exception("dup")));
        var s = await Sut().GetAsync(_clinicId);
        Assert.Equal(321, s.ChunkSizeTokens);
        _repo.Verify(r => r.Detach(It.IsAny<KnowledgeSearchSettings>()), Times.Once);

        _repo.Setup(r => r.GetReadOnlyAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync((KnowledgeSearchSettings?)null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().GetAsync(_clinicId));
    }

    [Theory]
    [InlineData(49, 0, 5, 0.5)]
    [InlineData(1001, 0, 5, 0.5)]
    [InlineData(100, -1, 5, 0.5)]
    [InlineData(100, 51, 5, 0.5)]
    [InlineData(100, 10, 0, 0.5)]
    [InlineData(100, 10, 21, 0.5)]
    [InlineData(100, 10, 5, -0.1)]
    [InlineData(100, 10, 5, 1.1)]
    [InlineData(100, 10, 5, double.NaN)]
    public async Task Update_validates_ranges(int size, int overlap, int topK, double min)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clinicId, new UpdateKnowledgeSettingsRequest(size, overlap, topK, min)));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_saves_new_values()
    {
        var row = new KnowledgeSearchSettings { ClinicId = _clinicId };
        _repo.Setup(r => r.GetReadOnlyAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _repo.Setup(r => r.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var s = await Sut().UpdateAsync(_clinicId, new UpdateKnowledgeSettingsRequest(100, 50, 3, 0.4));
        Assert.Equal((100, 50, 3, 0.4), (row.ChunkSizeTokens, row.ChunkOverlapTokens, row.TopK, row.MinimumSimilarity));
        Assert.Equal(3, s.TopK);
    }
}

public class KnowledgeSearchServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IKnowledgeDocumentRepository> _documents = new();
    private readonly Mock<IEmbeddingService> _embeddings = new();
    private readonly Mock<IKnowledgeSettingsService> _settings = new();

    public KnowledgeSearchServiceTests()
    {
        _settings.Setup(s => s.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSettingsResponse("m", 4, "cosine", "none", 200, 20, 3, 0.5, DateTimeOffset.UtcNow));
        _embeddings.Setup(e => e.EmbedAsync(It.IsAny<string>(), "m", 4, It.IsAny<CancellationToken>())).ReturnsAsync(new float[4]);
    }

    private KnowledgeSearchService Sut() => new(_documents.Object, _embeddings.Object, _settings.Object);

    private static KnowledgeChunkMatch Match(double score) => new(Guid.NewGuid(), Guid.NewGuid(), "Title", "faq", "content", score);

    [Fact]
    public async Task Blank_query_or_empty_clinic_returns_nothing_without_calling_openai()
    {
        Assert.Empty(await Sut().SearchRankedAsync(_clinicId, "  "));
        Assert.Empty(await Sut().SearchRankedAsync(Guid.Empty, "price"));
        _embeddings.Verify(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData(1, 1)]
    [InlineData(10, 3)]
    [InlineData(0, 1)]
    public async Task Take_is_clamped_to_the_clinic_top_k(int? limit, int expected)
    {
        _documents.Setup(d => d.SearchChunksAsync(_clinicId, It.IsAny<float[]>(), expected, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeChunkMatch>());
        await Sut().SearchRankedAsync(_clinicId, "price", limit);
        _documents.Verify(d => d.SearchChunksAsync(_clinicId, It.IsAny<float[]>(), expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ranked_results_flag_the_minimum_similarity_and_search_filters_and_rounds()
    {
        _documents.Setup(d => d.SearchChunksAsync(_clinicId, It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeChunkMatch> { Match(0.91234567), Match(0.5), Match(0.49) });
        var ranked = await Sut().SearchRankedAsync(_clinicId, " price ");
        Assert.Equal([true, true, false], ranked.Select(r => r.MeetsMinimumSimilarity));
        _embeddings.Verify(e => e.EmbedAsync("price", "m", 4, It.IsAny<CancellationToken>()), Times.Once);

        var search = await Sut().SearchAsync(_clinicId, "price");
        Assert.Equal([0.9123, 0.5], search.Results.Select(r => r.Score));
    }
}
