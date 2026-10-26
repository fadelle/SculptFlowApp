using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Tests.Guards;

namespace Omni.App.Tests.Helpers;

/// <summary>
/// The prompt files in Resources/ai (tools/extract-n8n-prompts.ps1) against the n8n workflows they came from: each prompt,
/// rendered by AiPromptTemplate with AiPromptValues for a sample AI trigger, must equal the n8n text with every expression
/// evaluated the way n8n does (this test's own small evaluator, with the JSON.stringify and date results written out by
/// hand); tool descriptions and argument schemas must equal the tool nodes'. A workflow edit without re-running the
/// script fails here.
/// </summary>
public class AiPromptTemplateTests
{
    private static readonly string Root = LeftoverNameScanner.FindRepoRoot();
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 9, 5, 0, TimeSpan.Zero); // a Wednesday; Beirut is UTC+3 then

    private static readonly AiTriggerPayload Sample = new(
        ClientId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ConversationId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        ContactId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
        MessageId: Guid.NewGuid(),
        Channel: "whatsapp",
        MessageType: "text",
        MessageText: "Can I book \"Friday\"?\nشكرا 🙏",
        ClientName: "Glow & Co",
        BusinessType: "salon",
        Language: "en",
        ContactLanguage: null,
        Timezone: "Asia/Beirut",
        AiProfile: "A hair salon in Beirut.\nWalk-ins welcome.",
        Terms: new Dictionary<string, TermForms> { ["contact"] = new("client", "clients"), ["booking"] = new("موعد", "مواعيد") },
        Bot: new AiBotConfig(Guid.NewGuid(), "Front desk", "custom", "Be <warm> & brief.\nNever discuss prices.", [BotCapability.Bookings], "handoff_on_request", "en"));

    // JSON.stringify(terms) and $now.setZone(...).toFormat(...) as n8n printed them, written out by hand.
    private const string SampleTermsJs = """{"contact":{"one":"client","other":"clients"},"booking":{"one":"موعد","other":"مواعيد"}}""";
    private static readonly Dictionary<string, string> NowByZone = new() { ["Asia/Beirut"] = "Wednesday, 2026-07-15 12:05", ["UTC"] = "Wednesday, 2026-07-15 09:05" };

    // A tool result as the formatter's $json, and JSON.stringify($json).
    private static readonly object SampleResult = new { success = false, code = "SLOT_UNAVAILABLE", message = "That time was just taken.", requestedLabel = "Friday at 10 <AM>", amount = 1.5, booking = (object?)null };
    private const string SampleResultJs = """{"success":false,"code":"SLOT_UNAVAILABLE","message":"That time was just taken.","requestedLabel":"Friday at 10 <AM>","amount":1.5,"booking":null}""";

    public static TheoryData<string> Triggers => new() { "full", "fallbacks" };

    private static (AiTriggerPayload Trigger, string TermsJs) TriggerFor(string name) => name == "full"
        ? (Sample, SampleTermsJs)
        : (Sample with { ContactLanguage = "", Timezone = null, Terms = null, AiProfile = null }, "null");

    [Theory]
    [MemberData(nameof(Triggers))]
    public void The_agent_prompt_renders_like_the_n8n_system_message(string trigger)
    {
        var (payload, termsJs) = TriggerFor(trigger);
        var agent = Node(Assistant, "AI Agent").GetProperty("parameters");
        var n8n = Evaluate(agent.GetProperty("options").GetProperty("systemMessage").GetString()!, payload, termsJs);

        Assert.Equal(n8n, AiPromptTemplate.RenderFile(AiPrompts.AgentSystem, AiPromptValues.ForMessage(payload, Now)));
        // The agent's user input is the message text itself (promptType define, text = messageText).
        Assert.Equal("={{ $('user_message').item.json.body.messageText }}", agent.GetProperty("text").GetString());
    }

    [Theory]
    [MemberData(nameof(Triggers))]
    public void The_formatter_prompts_render_like_the_result_formatter_node(string trigger)
    {
        var (payload, termsJs) = TriggerFor(trigger);
        var parameters = Node(Assistant, "result_formatter").GetProperty("parameters");
        var values = AiPromptValues.ForFormatter(payload, Now, SampleResult);

        var system = parameters.GetProperty("messages").GetProperty("messageValues")[0].GetProperty("message").GetString()!;
        Assert.Equal(Evaluate(system, payload, termsJs), AiPromptTemplate.RenderFile(AiPrompts.FormatterSystem, values));
        var user = AiPromptTemplate.RenderFile(AiPrompts.FormatterUser, values);
        Assert.Equal(Evaluate(parameters.GetProperty("text").GetString()!, payload, termsJs), user);
        Assert.StartsWith("VERIFIED BACKEND RESULT:\n" + SampleResultJs + "\n\nLATEST CONTACT MESSAGE:\n", user);
    }

    [Fact]
    public void The_benchmark_prompt_is_the_benchmark_agents_system_message()
    {
        var n8n = Node(Benchmark, "AI Agent").GetProperty("parameters").GetProperty("options").GetProperty("systemMessage").GetString()!;
        Assert.Equal(n8n.TrimStart('='), AiPromptTemplate.Load(AiPrompts.BenchmarkSystem));
        Assert.Empty(AiPromptTemplate.Placeholders(AiPromptTemplate.Load(AiPrompts.BenchmarkSystem)));
    }

    [Fact]
    public void Every_ai_tool_node_has_its_description_and_argument_schema()
    {
        var tools = Nodes(Assistant).Where(n => n.GetProperty("type").GetString() == "n8n-nodes-base.httpRequestTool").ToList();
        Assert.Equal(tools.Select(t => t.GetProperty("name").GetString()!).Order(), AiPrompts.ToolNames.Order());

        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString()!;
            var parameters = tool.GetProperty("parameters");
            Assert.Equal(parameters.GetProperty("toolDescription").GetString(), AiPromptTemplate.Load(AiPrompts.ToolDescription(name)));

            // Every $fromAI('name', 'description', 'type'[, default]) in the node's text values, in order; a default = optional.
            var expected = Strings(parameters)
                .SelectMany(text => Regex.Matches(text, @"\$fromAI\(\s*'([^']*)'\s*,\s*'([^']*)'\s*,\s*'(\w+)'\s*(?:,\s*'([^']*)'\s*)?\)"))
                .Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Success ? m.Groups[4].Value : null)).ToList();
            var schema = JsonNode.Parse(AiPromptTemplate.Load(AiPrompts.ToolParameters(name)))!.AsObject();
            Assert.Equal("object", (string?)schema["type"]);
            var actual = schema["properties"]!.AsObject()
                .Select(p => (p.Key, (string)p.Value!["description"]!, (string)p.Value!["type"]!, (string?)p.Value!["default"])).ToList();
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Where(e => e.Item4 is null).Select(e => e.Item1), schema["required"]!.AsArray().Select(r => (string)r!));
        }
    }

    [Fact]
    public void Rendering_fills_every_placeholder_and_refuses_a_missing_value()
    {
        Assert.Equal("Hi Glow, Glow!", AiPromptTemplate.Render("Hi {{name}}, {{name}}!", new Dictionary<string, string> { ["name"] = "Glow" }));
        var ex = Assert.Throws<InvalidOperationException>(() => AiPromptTemplate.Render("Hi {{name}} {{other}}", new Dictionary<string, string> { ["name"] = "x" }));
        Assert.Contains("{{other}}", ex.Message);
        Assert.Throws<KeyNotFoundException>(() => AiPromptTemplate.Load("missing.txt"));
        Assert.DoesNotContain('\r', AiPromptTemplate.Load(AiPrompts.AgentSystem));
        Assert.Equal(AiPromptValues.ForMessage(Sample, Now).Keys.Order(), AiPromptTemplate.Placeholders(AiPromptTemplate.Load(AiPrompts.AgentSystem)).Order());
    }

    [Fact]
    public void JsJson_prints_like_JSON_stringify()
    {
        Assert.Equal(SampleTermsJs, JsJson.Stringify(Sample.Terms));
        Assert.Equal(SampleResultJs, JsJson.Stringify(SampleResult));
        Assert.Equal("null", JsJson.Stringify((object?)null));
        // Only ", \ and control characters are escaped; numbers print like JavaScript's (2.0 -> 2).
        var source = "{\"t\":\"a\\\"b\\\\c\\nd\\te\\u0001 <&> 'x' \\u00e9 \\ud83d\\ude00\",\"n\":[1,0.5,-3,2.0],\"b\":[true,false,null]}";
        var expected = "{\"t\":\"a\\\"b\\\\c\\nd\\te\\u0001 <&> 'x' é \U0001F600\",\"n\":[1,0.5,-3,2],\"b\":[true,false,null]}";
        Assert.Equal(expected, JsJson.Stringify(JsonNode.Parse(source)));
    }

    // ---------------------------------------------------------------- n8n side

    private static readonly Lazy<JsonElement> AssistantJson = new(() => Load("omni-ai-assistant.json"));
    private static readonly Lazy<JsonElement> BenchmarkJson = new(() => Load("omni-retrieval-benchmark.json"));
    private static JsonElement Assistant => AssistantJson.Value;
    private static JsonElement Benchmark => BenchmarkJson.Value;

    private static JsonElement Load(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "n8n", file))).RootElement;

    private static IEnumerable<JsonElement> Nodes(JsonElement workflow) => workflow.GetProperty("nodes").EnumerateArray();

    private static JsonElement Node(JsonElement workflow, string name) => Nodes(workflow).Single(n => n.GetProperty("name").GetString() == name);

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Strings(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
        _ => []
    };

    // An n8n text ("=..." with {{ expressions }}) as n8n rendered it for the trigger.
    private static string Evaluate(string text, AiTriggerPayload trigger, string termsJs)
    {
        var body = JsonSerializer.SerializeToNode(trigger, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Regex.Replace(text.TrimStart('='), @"\{\{(.*?)\}\}", m => Expression(m.Groups[1].Value.Trim(), body, termsJs) ?? "", RegexOptions.Singleline);
    }

    private static string? Expression(string expression, JsonNode body, string termsJs)
    {
        if (expression == "JSON.stringify($json)") return SampleResultJs;
        if (expression == "JSON.stringify($('user_message').item.json.body.terms)") return termsJs;

        var now = Regex.Match(expression, @"^\$now\.setZone\((.+)\)\.toFormat\('cccc, yyyy-MM-dd HH:mm'\)$");
        if (now.Success) return NowByZone[Expression(now.Groups[1].Value, body, termsJs)!];

        var or = Regex.Match(expression, @"^(.+?) \|\| '([^']*)'$");
        if (or.Success)
        {
            var value = Expression(or.Groups[1].Value, body, termsJs);
            return string.IsNullOrEmpty(value) ? or.Groups[2].Value : value; // JavaScript: null and "" are falsy
        }

        var field = Regex.Match(expression, @"^\$\('user_message'\)\.item\.json\.body\.([\w.]+)$");
        Assert.True(field.Success, $"The test can't evaluate the n8n expression {{{{ {expression} }}}}; teach it (and the script) the new expression.");
        JsonNode? node = body;
        foreach (var part in field.Groups[1].Value.Split('.')) node = node?[part];
        return node is null ? null : node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
    }
}
