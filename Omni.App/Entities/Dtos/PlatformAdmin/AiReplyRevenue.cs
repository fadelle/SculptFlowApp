namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>What a client was charged for AI replies in a period (settled ai_reply usage after refunds).</summary>
public record AiReplyRevenue(Guid ClientId, decimal Amount, string Currency);
