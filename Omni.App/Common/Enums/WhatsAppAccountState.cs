namespace Omni.App.Common.Enums;

/// <summary>The WhatsApp account's standing as channels.channel_integrations.account_status holds it, set from Meta's
/// account_update webhooks (WhatsAppHealthService.AccountState; QA t10-02). Each has a label values.{camelCase}.</summary>
public static class WhatsAppAccountState
{
    /// <summary>Banned by Meta (ban_info DISABLE): nothing can be sent. Problem.</summary>
    public const string Disabled = "disabled";
    /// <summary>n8n's older value for a ban. Problem.</summary>
    public const string Banned = "banned";
    /// <summary>Some sending blocked (restriction_info, e.g. business-started messages) until it expires. Problem.</summary>
    public const string Restricted = "restricted";
    /// <summary>Meta will disable the account unless the issue is fixed. Warning.</summary>
    public const string ScheduledForDisable = "scheduled_for_disable";
    /// <summary>A policy violation was reported. Warning.</summary>
    public const string Violation = "violation";
    /// <summary>Deleted, offboarded or the partner (us) removed: the connection no longer works. Disconnected.</summary>
    public const string Removed = "removed";
    /// <summary>Reinstated or unrestricted: in good standing again.</summary>
    public const string Active = "active";
}
