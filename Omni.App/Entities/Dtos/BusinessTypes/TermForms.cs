namespace Omni.App.Entities.Dtos.BusinessTypes;

/// <summary>One word of a business type's terminology in one language: singular ("one") and plural ("other"), written as
/// they read mid-sentence (lower case in English: "patient" / "patients").</summary>
public record TermForms(string One, string Other);
