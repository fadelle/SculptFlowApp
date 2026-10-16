namespace Omni.App.Entities.Dtos.BusinessTypes;

/// <summary>Where the contacts in one stage go when a client changes business type. FromLabel is the old type's label (or
/// the key itself when the old type has no such stage); Category is the old stage's category (empty when unknown).</summary>
public record StageMove(string FromKey, string FromLabel, string Category, int Count, string ToKey, string ToLabel);
