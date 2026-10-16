namespace Omni.App.Entities.Requests.Contacts;

/// <summary>StageKey is a stage key of the client's business type. Qualification and Source are both optional — null means
/// "leave unchanged". Source has no fixed enum (Contact.Source is free text, no CHECK constraint — see
/// Entities/Models/Contact.cs), so it isn't validated against a list the way StageKey/Qualification are.</summary>
public record UpdateContactStageRequest(
    string StageKey,
    string? Qualification,
    string? Source = null
);
