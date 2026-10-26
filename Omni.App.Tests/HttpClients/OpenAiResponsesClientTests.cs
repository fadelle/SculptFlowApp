using System.Net;
using System.Text.Json.Nodes;
using Omni.App.Business.HttpClients.OpenAi;
using Omni.App.Tests.Support;

namespace Omni.App.Tests.HttpClients;

public class OpenAiResponsesClientTests
{
    private readonly FakeHttpHandler _http = new();
    private readonly Mock<IConfigManager> _config = new();

    public OpenAiResponsesClientTests()
    {
        _config.SetupGet(c => c.AiApiKey).Returns("sk-ai");
        _config.SetupGet(c => c.EmbeddingsApiKey).Returns("sk-embeddings");
        _config.SetupGet(c => c.AiBaseUrl).Returns("https://api.openai.example/v1/");
        _config.SetupGet(c => c.AiTimeoutSeconds).Returns(30);
    }

    private OpenAiResponsesClient Sut() => new(_http.CreateClient(), _config.Object);

    private static AiModelRequest Request(IReadOnlyList<AiInputItem>? input = null, IReadOnlyList<AiToolSpec>? tools = null, AiJsonSchema? output = null) =>
        new("gpt-6-luna", "You are the bot.", input ?? [AiInputItem.User("Hi")], tools, output);

    private const string FinalAnswer = """
        {"id":"resp_2","status":"completed","output":[
          {"type":"reasoning","id":"rs_1","summary":[]},
          {"type":"message","id":"msg_1","role":"assistant","content":[{"type":"output_text","text":"{\"action\":"},{"type":"output_text","text":"\"reply\"}"}]}],
         "usage":{"input_tokens":120,"input_tokens_details":{"cached_tokens":64},"output_tokens":30,"output_tokens_details":{"reasoning_tokens":12},"total_tokens":150}}
        """;

    [Fact]
    public async Task Sends_model_instructions_input_tools_and_the_json_schema_format_with_the_ai_key()
    {
        _http.Enqueue(HttpStatusCode.OK, FinalAnswer);
        var tool = new AiToolSpec("get_services", "Lists services.", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() });
        var schema = new AiJsonSchema("decision", new JsonObject { ["type"] = "object" });

        await Sut().CreateResponseAsync(Request(tools: [tool], output: schema));

        var sent = _http.Requests.Single();
        Assert.Equal("https://api.openai.example/v1/responses", sent.Uri.AbsoluteUri);
        Assert.Equal("Bearer sk-ai", sent.Headers["Authorization"]);
        var body = JsonNode.Parse(sent.Body!)!;
        Assert.Equal("gpt-6-luna", (string?)body["model"]);
        Assert.Equal("You are the bot.", (string?)body["instructions"]);
        Assert.Equal("user", (string?)body["input"]![0]!["role"]);
        Assert.Equal("Hi", (string?)body["input"]![0]!["content"]);
        var sentTool = body["tools"]![0]!;
        Assert.Equal(("function", "get_services", "Lists services.", false), ((string?)sentTool["type"], (string?)sentTool["name"], (string?)sentTool["description"], (bool)sentTool["strict"]!));
        Assert.Equal("object", (string?)sentTool["parameters"]!["type"]);
        var format = body["text"]!["format"]!;
        Assert.Equal(("json_schema", "decision", true), ((string?)format["type"], (string?)format["name"], (bool)format["strict"]!));
        Assert.Equal("object", (string?)format["schema"]!["type"]);
        Assert.Null(body["temperature"]);
    }

    [Fact]
    public async Task Reads_the_output_text_every_output_item_and_the_token_usage()
    {
        _http.Enqueue(HttpStatusCode.OK, FinalAnswer);

        var answer = await Sut().CreateResponseAsync(Request());

        Assert.Equal(("resp_2", "completed"), (answer.Id, answer.Status));
        Assert.Equal("{\"action\":\"reply\"}", answer.OutputText);
        Assert.False(answer.WantsTools);
        Assert.Equal(["reasoning", "message"], answer.OutputItems.Select(i => (string?)i.Json["type"]));
        Assert.Equal(new AiTokenUsage(120, 30, 150, 64, 12), answer.Usage);
        Assert.True(answer.DurationMs >= 0);
    }

    [Fact]
    public async Task A_function_call_loop_hands_the_calls_back_with_their_outputs()
    {
        _http.Enqueue(HttpStatusCode.OK, """
            {"id":"resp_1","status":"completed","output":[
              {"type":"reasoning","id":"rs_1","summary":[]},
              {"type":"function_call","id":"fc_1","call_id":"call_a","name":"get_available_slots","arguments":"{\"date\":\"2026-11-02\"}"},
              {"type":"function_call","id":"fc_2","call_id":"call_b","name":"get_services","arguments":"{}"}],
             "usage":{"input_tokens":100,"output_tokens":20,"total_tokens":120}}
            """);
        _http.Enqueue(HttpStatusCode.OK, FinalAnswer);
        var client = Sut();
        var input = new List<AiInputItem> { AiInputItem.User("Any time Monday?") };

        var first = await client.CreateResponseAsync(Request(input));
        Assert.True(first.WantsTools);
        Assert.Null(first.OutputText);
        Assert.Equal([new AiFunctionCall("call_a", "get_available_slots", "{\"date\":\"2026-11-02\"}"), new AiFunctionCall("call_b", "get_services", "{}")], first.FunctionCalls);
        Assert.Equal(new AiTokenUsage(100, 20, 120), first.Usage);

        input.AddRange(first.OutputItems);
        input.AddRange(first.FunctionCalls.Select(c => AiInputItem.FunctionOutput(c.CallId, "{\"ok\":true}")));
        var second = await client.CreateResponseAsync(Request(input));
        Assert.Equal("{\"action\":\"reply\"}", second.OutputText);

        var sentInput = JsonNode.Parse(_http.Requests[1].Body!)!["input"]!.AsArray();
        Assert.Equal(["message", "reasoning", "function_call", "function_call", "function_call_output", "function_call_output"],
            sentInput.Select(i => (string?)i!["type"] ?? "message"));
        Assert.Equal("rs_1", (string?)sentInput[1]!["id"]);
        Assert.Equal(("call_a", "{\"ok\":true}"), ((string?)sentInput[4]!["call_id"], (string?)sentInput[4]!["output"]));
    }

    [Fact]
    public async Task An_empty_ai_key_falls_back_to_the_embeddings_key_and_neither_is_a_clear_error()
    {
        _config.SetupGet(c => c.AiApiKey).Returns(" ");
        _http.Enqueue(HttpStatusCode.OK, FinalAnswer);
        await Sut().CreateResponseAsync(Request());
        Assert.Equal("Bearer sk-embeddings", _http.Requests[0].Headers["Authorization"]);

        _config.SetupGet(c => c.EmbeddingsApiKey).Returns("");
        var ex = await Assert.ThrowsAsync<AiModelException>(() => Sut().CreateResponseAsync(Request()));
        Assert.Contains("Ai / ApiKey", ex.Message);
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Error_answers_report_the_status_and_code_but_never_the_body()
    {
        _http.Enqueue(HttpStatusCode.TooManyRequests, """{"error":{"message":"Rate limit for key sk-ai***","type":"requests","code":"rate_limit_exceeded"}}""");
        var ex = await Assert.ThrowsAsync<AiModelException>(() => Sut().CreateResponseAsync(Request()));
        Assert.Equal("The AI model API answered HTTP 429 (rate_limit_exceeded).", ex.Message);

        _http.Enqueue(HttpStatusCode.InternalServerError, "<html>oops</html>");
        Assert.Equal("The AI model API answered HTTP 500.", (await Assert.ThrowsAsync<AiModelException>(() => Sut().CreateResponseAsync(Request()))).Message);

        _http.Enqueue(HttpStatusCode.OK, "not json");
        Assert.Contains("unexpected", (await Assert.ThrowsAsync<AiModelException>(() => Sut().CreateResponseAsync(Request()))).Message);

        _http.EnqueueThrow(new HttpRequestException("no route"));
        Assert.Contains("unreachable", (await Assert.ThrowsAsync<AiModelException>(() => Sut().CreateResponseAsync(Request()))).Message);
    }

    [Fact]
    public async Task A_slow_model_times_out_after_the_setting_but_a_canceled_caller_stays_canceled()
    {
        _config.SetupGet(c => c.AiTimeoutSeconds).Returns(1);
        var slow = new SlowHandler();
        var client = new OpenAiResponsesClient(new HttpClient(slow), _config.Object);
        var ex = await Assert.ThrowsAsync<AiModelException>(() => client.CreateResponseAsync(Request()));
        Assert.Equal("The AI model didn't answer within 1 seconds.", ex.Message);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateResponseAsync(Request(), canceled.Token));
    }

    [Fact]
    public async Task An_incomplete_answer_without_usage_is_returned_as_is()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"id":"resp_3","status":"incomplete","output":[]}""");
        var answer = await Sut().CreateResponseAsync(Request(output: null) with { Temperature = 0.2 });
        Assert.Equal(("incomplete", (string?)null, AiTokenUsage.None), (answer.Status, answer.OutputText, answer.Usage));
        Assert.Equal(0.2, (double)JsonNode.Parse(_http.Requests[0].Body!)!["temperature"]!);
        Assert.Null(JsonNode.Parse(_http.Requests[0].Body!)!["tools"]);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
