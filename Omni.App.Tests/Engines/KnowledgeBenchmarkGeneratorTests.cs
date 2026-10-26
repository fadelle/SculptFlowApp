using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Engines.KnowledgeBenchmark;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Business.Jobs;
using Omni.App.Tests.Support;

namespace Omni.App.Tests.Engines;

/// <summary>The retrieval benchmark workflow (docs/n8n/omni-retrieval-benchmark.json) as code, and the Benchmark /
/// GeneratorEngine switch in front of it.</summary>
public class KnowledgeBenchmarkGeneratorTests
{
    private static readonly Guid GenerationId = Guid.Parse("60000000-0000-0000-0000-000000000006");
    private static readonly Guid ClientId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid DocumentId = Guid.Parse("70000000-0000-0000-0000-000000000007");
    private static readonly Guid ChunkId = Guid.Parse("80000000-0000-0000-0000-000000000008");

    private readonly FakeAiModel _model = new();
    private readonly Mock<IKnowledgeBenchmarkService> _benchmark = new();
    private readonly Mock<IConfigManager> _config = new();

    public KnowledgeBenchmarkGeneratorTests() => _config.SetupGet(c => c.BenchmarkGeneratorModel).Returns("gpt-4o");

    private static BenchmarkGeneratorPayload Payload() => new(GenerationId, ClientId, [new BenchmarkSourceChunk(DocumentId, ChunkId, "Prices", "A cut is $20.")]);

    private KnowledgeBenchmarkGenerator Sut() => new(_model, _benchmark.Object, _config.Object, NullLogger<KnowledgeBenchmarkGenerator>.Instance);

    [Fact]
    public async Task The_agent_gets_the_extracted_prompt_and_the_payload_and_its_questions_are_stored_like_the_callback()
    {
        var output = $$"""{"generationId":"{{GenerationId}}","questions":[{"documentId":"{{DocumentId}}","chunkId":"{{ChunkId}}","question":"How much is a cut?"}]}""";
        _model.Text(output);

        await Sut().GenerateAsync(Payload());

        var request = Assert.Single(_model.Requests);
        Assert.Equal("gpt-4o", request.Model);
        Assert.Equal(AiPromptTemplate.Load(AiPrompts.BenchmarkSystem), request.Instructions);
        Assert.Null(request.Tools);
        Assert.Equal($$"""{"generationId":"{{GenerationId}}","clientId":"{{ClientId}}","chunks":[{"documentId":"{{DocumentId}}","chunkId":"{{ChunkId}}","documentTitle":"Prices","content":"A cut is $20."}]}""",
            (string?)Assert.Single(request.Input).Json["content"]);
        _benchmark.Verify(b => b.ReceiveGenerationResultAsync(GenerationId,
            It.Is<GeneratorResponse>(r => r.GenerationId == GenerationId.ToString() && r.Questions.Count == 1), output, false, It.IsAny<CancellationToken>()), Times.Once);
        _benchmark.Verify(b => b.FailGenerationAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_model_failure_fails_the_generation()
    {
        _model.Throw(new AiModelException("The AI model API answered HTTP 429 (rate_limit_exceeded)."));

        await Sut().GenerateAsync(Payload());

        _benchmark.Verify(b => b.FailGenerationAsync(GenerationId, "The question generator failed: The AI model API answered HTTP 429 (rate_limit_exceeded).", It.IsAny<CancellationToken>()), Times.Once);
        _benchmark.Verify(b => b.ReceiveGenerationResultAsync(It.IsAny<Guid>(), It.IsAny<GeneratorResponse>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Here are your questions!")]
    [InlineData("{\"items\":[]}")]
    public async Task An_output_that_is_not_a_questions_reply_fails_the_generation(string output)
    {
        _model.Text(output);

        await Sut().GenerateAsync(Payload());

        _benchmark.Verify(b => b.FailGenerationAsync(GenerationId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_reply_the_callback_would_refuse_fails_the_generation()
    {
        _model.Text("""{"generationId":"other","questions":[]}""");
        _benchmark.Setup(b => b.ReceiveGenerationResultAsync(GenerationId, It.IsAny<GeneratorResponse>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("The generationId in the body doesn't match the generationId in the URL."));

        await Sut().GenerateAsync(Payload());

        _benchmark.Verify(b => b.FailGenerationAsync(GenerationId, "The generationId in the body doesn't match the generationId in the URL.", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------- Benchmark / GeneratorEngine

    private readonly FakeHttpHandler _http = new();

    private (KnowledgeBenchmarkGeneratorDispatcher Sut, KnowledgeBenchmarkGenerationQueue Queue) Dispatcher(string engine, string? n8nUrl = "https://n8n.example/webhook/bench")
    {
        _config.SetupGet(c => c.BenchmarkGeneratorEngine).Returns(engine);
        var n8n = new N8nKnowledgeBenchmarkGeneratorClient(_http.CreateClient(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["N8n:KnowledgeBenchmarkWebhookUrl"] = n8nUrl }).Build(),
            NullLogger<N8nKnowledgeBenchmarkGeneratorClient>.Instance);
        var queue = new KnowledgeBenchmarkGenerationQueue();
        return (new KnowledgeBenchmarkGeneratorDispatcher(_config.Object, n8n, queue), queue);
    }

    [Fact]
    public async Task On_n8n_the_chunks_are_posted_to_the_workflow_as_before()
    {
        var (sut, _) = Dispatcher(WorkflowEngines.N8n);
        Assert.True(sut.IsConfigured);
        _http.Enqueue(HttpStatusCode.OK, """{"message":"Workflow was started"}""");

        var sent = await sut.SendAsync(ClientId, GenerationId, Payload().Chunks);

        Assert.Null(sent.Immediate);
        Assert.Equal("https://n8n.example/webhook/bench", Assert.Single(_http.Requests).Uri.ToString());
        Assert.False(Dispatcher(WorkflowEngines.N8n, n8nUrl: null).Sut.IsConfigured);
    }

    [Fact]
    public async Task On_code_the_generation_is_queued_acknowledged_and_written_by_the_worker()
    {
        var (sut, queue) = Dispatcher(WorkflowEngines.Code, n8nUrl: null);
        _config.SetupGet(c => c.AiApiKey).Returns("");
        _config.SetupGet(c => c.EmbeddingsApiKey).Returns("");
        Assert.False(sut.IsConfigured);
        _config.SetupGet(c => c.EmbeddingsApiKey).Returns("sk-embeddings");
        Assert.True(sut.IsConfigured);

        var sent = await sut.SendAsync(ClientId, GenerationId, Payload().Chunks);
        Assert.Equal(new GeneratorSendResult(null, null), sent);
        Assert.Empty(_http.Requests);

        var generated = new TaskCompletionSource<BenchmarkGeneratorPayload>();
        var generator = new Mock<IKnowledgeBenchmarkGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<BenchmarkGeneratorPayload>(), It.IsAny<CancellationToken>()))
            .Callback((BenchmarkGeneratorPayload p, CancellationToken _) => generated.TrySetResult(p)).Returns(Task.CompletedTask);
        var provider = new ServiceCollection().AddSingleton(generator.Object).BuildServiceProvider();
        var worker = new KnowledgeBenchmarkGenerationWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<KnowledgeBenchmarkGenerationWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);

        var payload = await generated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((GenerationId, ClientId, ChunkId), (payload.GenerationId, payload.ClientId, payload.Chunks[0].ChunkId));

        await worker.StopAsync(CancellationToken.None);
        await Assert.ThrowsAsync<BenchmarkGenerationException>(() => sut.SendAsync(ClientId, GenerationId, Payload().Chunks));
    }
}
