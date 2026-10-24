namespace Omni.App.Entities.Dtos.Reports;

/// <summary>Replies sent in the period: by the AI or by the team (Inbox or the WhatsApp Business app), and conversations
/// the AI answered in (AiOnlyConversations: with no team reply in the period).</summary>
public sealed record ReportReplyCounts(int AiReplies, int TeamReplies, int AiConversations, int AiOnlyConversations);
