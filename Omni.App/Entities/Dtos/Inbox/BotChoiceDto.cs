namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>A bot the Inbox Settings page offers for an account (only an active one can be picked).</summary>
public record BotChoiceDto(Guid Id, string Name, string Status);
