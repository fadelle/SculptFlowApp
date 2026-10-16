namespace Omni.App.Entities.Dtos.BusinessTypes;

/// <summary>What changing a client's business type does: contact counts per current stage and the stage each group moves
/// to (labels in English, for the admin portal).</summary>
public record BusinessTypeChangePreview(Guid ClientId, string FromCode, string ToCode, int ContactCount, IReadOnlyList<StageMove> Moves);
