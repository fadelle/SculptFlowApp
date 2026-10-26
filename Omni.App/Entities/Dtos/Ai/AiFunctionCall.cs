namespace Omni.App.Entities.Dtos.Ai;

/// <summary>A function call the model asked for: answer it with AiInputItem.FunctionOutput(CallId, ...).
/// <see cref="Arguments"/> is the JSON text of the arguments.</summary>
public sealed record AiFunctionCall(string CallId, string Name, string Arguments);
