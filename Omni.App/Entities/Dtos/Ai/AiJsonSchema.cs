using System.Text.Json.Nodes;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>A structured output format (OpenAI <c>text.format</c> of type json_schema): the model's final text is JSON
/// matching <see cref="Schema"/>.</summary>
public sealed record AiJsonSchema(string Name, JsonObject Schema, bool Strict = true);
