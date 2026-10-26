using System.Text.Json.Nodes;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>A function tool offered to the model: its name, the description the model reads, and the JSON schema of the
/// arguments it fills. <see cref="Strict"/> = OpenAI's strict mode (every property required, no extra ones).</summary>
public sealed record AiToolSpec(string Name, string Description, JsonObject Parameters, bool Strict = false);
