namespace PlasticSurgery.Common.Enums;

/// <summary>What kind of normalized event MetaWebhookParser extracted — the only thing
/// MetaWebhookProcessor and the Handlers switch on. Never Meta's raw "field" string.</summary>
public enum ParsedMetaEventKind
{
    CustomerMessage,
    BusinessAppEcho,
    MessageStatus,
    TemplateStatus,
    WhatsAppHealth,
    /// <summary>change.field == "history" — Coexistence history sync. Never treated as a live
    /// message and never AI-eligible; see HistoryHandler.</summary>
    HistorySync,
    /// <summary>change.field == "smb_app_state_sync" — WhatsApp Business App/Coexistence state
    /// sync. Never AI-eligible; see AppStateSyncHandler.</summary>
    AppStateSync,
    Unknown
}
