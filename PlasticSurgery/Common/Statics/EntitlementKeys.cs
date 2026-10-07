using System.Globalization;
using Microsoft.Extensions.Options;

namespace PlasticSurgery.Common.Statics;

/// <summary>Entitlement keys a plan can set (billing.plan_entitlements.entitlement_key). To add one: add a
/// constant + a definition below, give it a ClinicEntitlements property if code asks it often, check it where the
/// feature lives, and set it on the plans (admin API). No schema change.</summary>
public static class EntitlementKeys
{
    public const string Campaigns = "campaigns";
    /// <summary>Automation: the AI agent answers conversations (n8n trigger).</summary>
    public const string AiAgent = "ai_agent";
    public const string ApiAccess = "api_access";
    public const string AdvancedReporting = "advanced_reporting";
    public const string MaxAgents = "max_agents";
    public const string MaxWhatsAppNumbers = "max_whatsapp_numbers";
    public const string MaxChannelConnections = "max_channel_connections";
}
