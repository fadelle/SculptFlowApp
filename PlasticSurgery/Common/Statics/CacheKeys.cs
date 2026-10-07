namespace PlasticSurgery.Common.Statics;

/// <summary>
/// Every cache key and its lifetime, in one place. Keys are "area:thing:id" so a whole area can be dropped by prefix.
/// A cached value is a copy (stored as JSON), never a tracked EF entity, so the same keys work on a shared cache
/// (Redis) later. Every write that changes a cached value removes its key; the lifetime is only a safety net.
/// </summary>
public static class CacheKeys
{
    // ---- Billing: the subscription facts behind entitlement checks (EntitlementService). SubscriptionService removes a
    // clinic's key on every subscription change; PlanService removes the whole prefix when a plan's entitlements change.
    public const string EntitlementsPrefix = "billing:entitlements:";
    public static string Entitlements(Guid clinicId) => EntitlementsPrefix + clinicId.ToString("N");
    public static readonly TimeSpan EntitlementsTtl = TimeSpan.FromMinutes(10);

    // ---- WhatsApp: which clinic/connection an incoming Meta webhook belongs to (MetaWebhookProcessor). The whole prefix
    // is removed whenever a channel connection is saved or disconnected.
    public const string WhatsAppRoutingPrefix = "channels:whatsapp:";
    public static string WhatsAppByPhoneNumberId(string phoneNumberId) => WhatsAppRoutingPrefix + "phone:" + phoneNumberId;
    public static string WhatsAppByWabaId(string wabaId) => WhatsAppRoutingPrefix + "waba:" + wabaId;
    public static readonly TimeSpan WhatsAppRoutingTtl = TimeSpan.FromMinutes(30);
}
