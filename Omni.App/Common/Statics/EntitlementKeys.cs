using System.Globalization;
using Microsoft.Extensions.Options;

namespace Omni.App.Common.Statics;

/// <summary>Entitlement keys a plan can set (billing.plan_entitlements.entitlement_key). To add one: add a
/// constant + a definition below, give it a ClientEntitlements property if code asks it often, check it where the
/// feature lives, and set it on the plans (admin API). No schema change.</summary>
public static class EntitlementKeys
{
    public const string Campaigns = "campaigns";
    /// <summary>Bookings: the Bookings pages and API, the AI's availability/booking tools and calendar sync.</summary>
    public const string Bookings = "bookings";
    /// <summary>Automation: the AI assistant answers conversations (n8n trigger).</summary>
    public const string AiAssistant = "ai_assistant";
    public const string ApiAccess = "api_access";
    /// <summary>Reports: the Reports page's basic sections (conversations, contacts, bookings, campaigns).</summary>
    public const string Reporting = "reporting";
    /// <summary>Advanced reports: response times, AI replies, daily activity and usage on the Reports page.</summary>
    public const string AdvancedReporting = "advanced_reporting";
    public const string MaxUsers = "max_users";
    public const string MaxWhatsAppNumbers = "max_whatsapp_numbers";
    public const string MaxChannelConnections = "max_channel_connections";
    /// <summary>AI bots: how many connected channels the AI assistant answers on (in connection order; the others go to the
    /// team). Only matters while ai_assistant is on.</summary>
    public const string MaxAiBots = "max_ai_bots";
}
