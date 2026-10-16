using Omni.App.Entities.Dtos.Caching;
using Omni.App.Entities.Models;
using Models = Omni.App.Entities.Models;

namespace Omni.App.Common.Statics;

/// <summary>
/// Every cache key and its lifetime, in one place. Keys are "area:thing:id" so a whole area can be dropped by prefix.
/// A cached value is a copy (stored as JSON), never a tracked EF entity, so the same keys work on a shared cache
/// (Redis) later. Every write that changes a cached value removes its key; the lifetime is only a safety net.
/// <para>The table caches below (core, identity, scheduling, channels, billing rates) are read by the "ReadOnly" methods of
/// their repositories and cleared automatically: CacheInvalidationInterceptor removes the keys of every tracked row that a
/// SaveChanges adds, changes or deletes (again when its transaction commits or rolls back), via <see cref="EvictionsFor"/>.
/// Set-based deletes (client delete, user delete) remove their keys in the repository method. Their lifetimes are
/// settings, one per <see cref="Common.Enums.CacheArea"/> (Cache section, read through ICacheManager.LifetimeOf).</para>
/// </summary>
public static class CacheKeys
{
    // ---- Billing: the subscription facts behind entitlement checks (EntitlementService). SubscriptionService removes a
    // client's key on every subscription change; PlanService removes the whole prefix when a plan's entitlements change.
    public const string EntitlementsPrefix = "billing:entitlements:";
    public static string Entitlements(Guid clientId) => EntitlementsPrefix + clientId.ToString("N");
    public static readonly TimeSpan EntitlementsTtl = TimeSpan.FromMinutes(10);

    // ---- WhatsApp: which client/connection an incoming Meta webhook belongs to (MetaWebhookProcessor). The whole prefix
    // is removed whenever a channel connection is saved or disconnected.
    public const string WhatsAppRoutingPrefix = "channels:whatsapp:";
    public static string WhatsAppByPhoneNumberId(string phoneNumberId) => WhatsAppRoutingPrefix + "phone:" + phoneNumberId;
    public static string WhatsAppByWabaId(string wabaId) => WhatsAppRoutingPrefix + "waba:" + wabaId;
    public static readonly TimeSpan WhatsAppRoutingTtl = TimeSpan.FromMinutes(30);

    // ---- Core: a client row (ClientRepository.GetReadOnlyAsync, the signed-in client, the bot's client info).
    public const string ClientPrefix = "core:client:";
    public static string Client(Guid clientId) => ClientPrefix + clientId.ToString("N");

    // ---- Core: every business type with its pipeline stages (BusinessTypeRepository; one entry, the list is small). Read on
    // every dashboard request for the signed-in client's words and stage labels. Any saved type or stage drops it.
    public const string BusinessTypesPrefix = "core:business-type:";
    public const string BusinessTypes = BusinessTypesPrefix + "all";

    // ---- Core: a client's services, all of them by name (ServiceRepository.ListAsync / GetReadOnlyAsync filter it).
    public const string ServicesPrefix = "core:services:";
    public static string Services(Guid clientId) => ServicesPrefix + clientId.ToString("N");

    // ---- Identity: which client a signed-in user is an active member of (read on every signed-in request). Short
    // lifetime (Cache:StaffLookupMinutes) because a removed staff member must lose access quickly even if a removal were
    // ever missed.
    public const string MembershipPrefix = "identity:membership:";
    public static string Membership(string userId) => MembershipPrefix + userId;

    // ---- Crm: a client's client-wide Inbox settings row, or none (InboxSettingsRepository.GetForClientReadOnlyAsync; read
    // on every incoming patient message to decide whether the AI bot answers).
    public const string InboxSettingsPrefix = "crm:inbox-settings:";
    public static string InboxSettings(Guid clientId) => InboxSettingsPrefix + clientId.ToString("N");

    // ---- Scheduling: weekly rules, booking settings and date exceptions (every free-slot search reads all three).
    public const string AvailabilityPrefix = "scheduling:availability:";
    public static string AvailabilityRules(Guid clientId) => AvailabilityPrefix + "rules:" + clientId.ToString("N");
    public static string BookingSettings(Guid clientId) => AvailabilityPrefix + "booking-settings:" + clientId.ToString("N");
    public static string AvailabilityExceptions(Guid clientId) => AvailabilityPrefix + "exceptions:" + clientId.ToString("N");

    // ---- Scheduling: a client's calendar connections with their calendar lists (Settings page, and the "anything to
    // sync?" check on every booking change).
    public const string CalendarsPrefix = "scheduling:calendars:";
    public static string Calendars(Guid clientId) => CalendarsPrefix + clientId.ToString("N");

    // ---- Channels: a client's channel connections (senders, billing), one connection by id (Telegram webhooks), the
    // TikTok connection and the WhatsApp templates. LastWebhookAt in a cached copy can be behind: TouchLastWebhookAsync
    // doesn't clear it (it runs on every webhook); the Integrations page reads the table directly.
    public const string ChannelsPrefix = "channels:integrations:";
    public static string ChannelIntegrations(Guid clientId) => ChannelsPrefix + clientId.ToString("N");
    public const string ChannelByIdPrefix = "channels:integration:";
    public static string ChannelIntegration(Guid integrationId) => ChannelByIdPrefix + integrationId.ToString("N");
    public const string TikTokPrefix = "channels:tiktok:";
    public static string TikTok(Guid clientId) => TikTokPrefix + clientId.ToString("N");
    public const string WhatsAppTemplatesPrefix = "channels:templates:";
    public static string WhatsAppTemplates(Guid clientId) => WhatsAppTemplatesPrefix + clientId.ToString("N");

    // ---- Billing: the cards a client's events are rated against (depends on client cards, the client's plan and the
    // default card), every rate of a card, and who pays the provider per channel account. Read on every charged event.
    public const string RatingCardsPrefix = "billing:rating-cards:";
    public static string RatingCards(Guid clientId) => RatingCardsPrefix + clientId.ToString("N");
    public const string RatesPrefix = "billing:rates:";
    public static string Rates(Guid rateCardId) => RatesPrefix + rateCardId.ToString("N");
    public const string ChannelAccountBillingPrefix = "billing:channel-account:";
    public static string ChannelAccountBilling(Guid channelIntegrationId) => ChannelAccountBillingPrefix + channelIntegrationId.ToString("N");

    /// <summary>The keys (exact) and prefixes to remove when this row is added, changed or deleted; empty for rows that
    /// aren't cached. Called with the row's current values and again with its original ones, so a moved row (another
    /// client, another card) clears both places.</summary>
    public static IEnumerable<CacheEviction> EvictionsFor(object row)
    {
        switch (row)
        {
            case Models.Client c:
                yield return CacheEviction.Key(Client(c.Id));
                break;
            case BusinessType:
            case PipelineStage:
                yield return CacheEviction.Prefix(BusinessTypesPrefix);
                break;
            case ClientUser m:
                yield return CacheEviction.Key(Membership(m.UserId));
                break;
            case Service p:
                yield return CacheEviction.Key(Services(p.ClientId));
                break;
            case AvailabilityRule r:
                yield return CacheEviction.Key(AvailabilityRules(r.ClientId));
                break;
            case BookingSettings b:
                yield return CacheEviction.Key(BookingSettings(b.ClientId));
                break;
            case Models.InboxSettings i:
                yield return CacheEviction.Key(InboxSettings(i.ClientId));
                break;
            case AvailabilityException e:
                yield return CacheEviction.Key(AvailabilityExceptions(e.ClientId));
                break;
            case CalendarIntegration ci:
                yield return CacheEviction.Key(Calendars(ci.ClientId));
                break;
            case CalendarIntegrationCalendar:
                // The row doesn't carry its client; the interceptor resolves it from the tracked connection when it can.
                yield return CacheEviction.Prefix(CalendarsPrefix);
                break;
            case Models.ChannelIntegration ch:
                yield return CacheEviction.Key(ChannelIntegrations(ch.ClientId));
                yield return CacheEviction.Key(ChannelIntegration(ch.Id));
                break;
            case TikTokIntegration t:
                yield return CacheEviction.Key(TikTok(t.ClientId));
                break;
            case WhatsAppTemplate w:
                yield return CacheEviction.Key(WhatsAppTemplates(w.ClientId));
                break;
            case BillingRateCard card:
                // Active/default/client flags decide which cards any client is rated against.
                yield return CacheEviction.Prefix(RatingCardsPrefix);
                yield return CacheEviction.Key(Rates(card.Id));
                break;
            case BillingRate rate:
                yield return CacheEviction.Key(Rates(rate.RateCardId));
                break;
            case SubscriptionPlan:
                // A plan's rate card applies to every client on it.
                yield return CacheEviction.Prefix(RatingCardsPrefix);
                break;
            case ClientSubscription s:
                yield return CacheEviction.Key(RatingCards(s.ClientId));
                break;
            case ChannelAccountBillingSettings a:
                yield return CacheEviction.Key(ChannelAccountBilling(a.ChannelIntegrationId));
                break;
        }
    }

    /// <summary>Everything cached about one client, for a delete that cascades through all of its rows.</summary>
    public static IEnumerable<CacheEviction> EvictionsForClient(Guid clientId) =>
    [
        CacheEviction.Key(Client(clientId)),
        CacheEviction.Key(Services(clientId)),
        CacheEviction.Key(AvailabilityRules(clientId)),
        CacheEviction.Key(BookingSettings(clientId)),
        CacheEviction.Key(InboxSettings(clientId)),
        CacheEviction.Key(AvailabilityExceptions(clientId)),
        CacheEviction.Key(Calendars(clientId)),
        CacheEviction.Key(ChannelIntegrations(clientId)),
        CacheEviction.Key(TikTok(clientId)),
        CacheEviction.Key(WhatsAppTemplates(clientId)),
        CacheEviction.Key(RatingCards(clientId)),
        CacheEviction.Key(Entitlements(clientId)),
        // Keyed by user, connection or card id rather than client: drop the whole area (a client delete is rare).
        CacheEviction.Prefix(MembershipPrefix),
        CacheEviction.Prefix(ChannelByIdPrefix),
        CacheEviction.Prefix(ChannelAccountBillingPrefix),
        CacheEviction.Prefix(RatesPrefix),
        CacheEviction.Prefix(WhatsAppRoutingPrefix),
    ];
}
