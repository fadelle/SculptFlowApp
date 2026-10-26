using System.Text.Json.Nodes;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>One item of an OpenAI Responses API <c>input</c> list, kept as the JSON the API reads: a message, a function
/// call the model made (handed back as it came, AiModelResponse.OutputItems) or a function call's output.</summary>
public sealed record AiInputItem(JsonObject Json)
{
    public static AiInputItem Message(string role, string text) =>
        new(new JsonObject { ["role"] = role, ["content"] = text });

    public static AiInputItem User(string text) => Message("user", text);

    public static AiInputItem Assistant(string text) => Message("assistant", text);

    /// <summary>The answer to the model's function call <paramref name="callId"/>, as the JSON text the tool returned.</summary>
    public static AiInputItem FunctionOutput(string callId, string output) =>
        new(new JsonObject { ["type"] = "function_call_output", ["call_id"] = callId, ["output"] = output });
}
