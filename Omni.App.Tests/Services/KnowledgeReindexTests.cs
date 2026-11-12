using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Jobs;
using Omni.App.Business.Services.Knowledge;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Knowledge;

namespace Omni.App.Tests.Services;

/// <summary>QA t4-ai-15: a changed chunk size re-indexes the whole Knowledge Base in the background, with progress.</summary>
public class KnowledgeReindexTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);
    private readonly Guid _clientId = Guid.NewGuid();

    [Fact]
    public void One_reindex_per_client_at_a_time_and_a_finished_one_can_start_again()
    {
        var queue = new KnowledgeReindexQueue();
        Assert.Null(queue.Progress(_clientId));
        Assert.True(queue.Start(_clientId, T0));
        Assert.False(queue.Start(_clientId, T0)); // queued: not twice
        Assert.True(queue.Progress(_clientId)!.Running);
        Assert.True(queue.TryDequeue(out var dequeued));
        Assert.Equal(_clientId, dequeued);
        Assert.False(queue.TryDequeue(out _));

        queue.Report(_clientId, new(3, 3, 0, T0, T0.AddMinutes(1)));
        Assert.False(queue.Progress(_clientId)!.Running);
        Assert.True(queue.Start(_clientId, T0.AddMinutes(2)));
        Assert.True(queue.Start(Guid.NewGuid(), T0)); // another client isn't blocked
    }

    [Fact]
    public async Task The_worker_reindexes_every_entry_counts_failures_and_finishes()
    {
        var ok1 = Guid.NewGuid();
        var broken = Guid.NewGuid();
        var ok2 = Guid.NewGuid();
        var knowledge = new Mock<IKnowledgeService>();
        knowledge.Setup(k => k.ListAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new[] { ok1, broken, ok2 }.Select(id => new KnowledgeDocumentResponse(id, _clientId, "T", "faq", "c", true, 1, T0, T0)).ToList());
        knowledge.Setup(k => k.ReindexAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        knowledge.Setup(k => k.ReindexAsync(_clientId, broken, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("provider down"));
        var provider = new ServiceCollection().AddScoped(_ => knowledge.Object).BuildServiceProvider();
        var queue = new KnowledgeReindexQueue();
        var worker = new KnowledgeReindexWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(), new ManualTimeProvider(T0),
            NullLogger<KnowledgeReindexWorker>.Instance);

        queue.Start(_clientId, T0);
        await worker.RunAsync(_clientId, default);

        var progress = queue.Progress(_clientId)!;
        Assert.Equal((3, 3, 1, false), (progress.Total, progress.Done, progress.Failed, progress.Running));
        knowledge.Verify(k => k.ReindexAsync(_clientId, ok2, It.IsAny<CancellationToken>()), Times.Once); // went on after the failure
    }

    [Fact]
    public async Task Changing_the_chunk_size_starts_a_reindex_but_top_k_does_not()
    {
        var row = new KnowledgeSearchSettings
        {
            Id = Guid.NewGuid(), ClientId = _clientId, EmbeddingModel = "m", VectorDimension = 8, SimilarityMethod = "cosine",
            VectorIndexType = "none", ChunkSizeTokens = 200, ChunkOverlapTokens = 20, TopK = 5, MinimumSimilarity = 0.3, CreatedAt = T0, UpdatedAt = T0
        };
        var repo = new Mock<IKnowledgeSettingsRepository>();
        repo.Setup(r => r.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        repo.Setup(r => r.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var queue = new KnowledgeReindexQueue();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.KnowledgeMinChunkSizeTokens).Returns(50);
        config.SetupGet(c => c.KnowledgeMaxChunkSizeTokens).Returns(1000);
        config.SetupGet(c => c.KnowledgeMinTopK).Returns(1);
        config.SetupGet(c => c.KnowledgeMaxTopK).Returns(10);
        var sut = new KnowledgeSettingsService(repo.Object, Mock.Of<IUnitOfWork>(), new ConfigurationBuilder().Build(), config.Object, queue);

        await sut.UpdateAsync(_clientId, new UpdateKnowledgeSettingsRequest(200, 20, 8, 0.4));
        Assert.Null(queue.Progress(_clientId)); // only search-time values changed

        await sut.UpdateAsync(_clientId, new UpdateKnowledgeSettingsRequest(300, 20, 8, 0.4));
        Assert.True(queue.Progress(_clientId)!.Running);
    }
}
