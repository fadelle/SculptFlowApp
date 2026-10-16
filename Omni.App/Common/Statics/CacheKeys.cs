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
/// Set-based deletes (clinic delete, user delete) remove their keys in the repository method. Their lifetimes are
/// settings, one per <see cref="Common.Enums.CacheArea"/> (Cache section, read through ICacheManager.LifetimeOf).</para>
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

    // ---- Core: a clinic row (ClinicRepository.GetReadOnlyAsync, the signed-in clinic, the bot's clinic info).
    public const string ClinicPrefix = "core:clinic:";
    public static string Clinic(Guid clinicId) => ClinicPrefix + clinicId.ToString("N");

    // ---- Core: a clinic's procedures, all of them by name (ProcedureRepository.ListAsync / GetReadOnlyAsync filter it).
    public const string ProceduresPrefix = "core:procedures:";
    public static string Procedures(Guid clinicId) => ProceduresPrefix + clinicId.ToString("N");

    // ---- Identity: which clinic a signed-in user is an active member of (read on every signed-in request). Short
    // lifetime (Cache:StaffLookupMinutes) because a removed staff member must lose access quickly even if a removal were
    // ever missed.
    public const string MembershipPrefix = "identity:membership:";
    public static string Membership(string userId) => MembershipPrefix + userId;

    // ---- Crm: a clinic's clinic-wide Inbox settings row, or none (InboxSettingsRepository.GetForClinicReadOnlyAsync; read
    // on every incoming patient message to decide whether the AI bot answers).
    public const string InboxSettingsPrefix = "crm:inbox-settings:";
    public static string InboxSettings(Guid clinicId) => InboxSettingsPrefix + clinicId.ToString("N");

    // ---- Scheduling: weekly rules, booking settings and date exceptions (every free-slot search reads all three).
    public const string AvailabilityPrefix = "scheduling:availability:";
    public static string AvailabilityRules(Guid clinicId) => AvailabilityPrefix + "rules:" + clinicId.ToString("N");
    public static string BookingSettings(Guid clinicId) => AvailabilityPrefix + "booking:" + clinicId.ToString("N");
    public static string AvailabilityExceptions(Guid clinicId) => AvailabilityPrefix + "exceptions:" + clinicId.ToString("N");

    // ---- Scheduling: a clinic's calendar connections with their calendar lists (Settings page, and the "anything to
    // sync?" check on every appointment change).
    public const string CalendarsPrefix = "scheduling:calendars:";
    public static string Calendars(Guid clinicId) => CalendarsPrefix + clinicId.ToString("N");

    // ---- Channels: a clinic's channel connections (senders, billing), one connection by id (Telegram webhooks), the
    // TikTok connection and the WhatsApp templates. LastWebhookAt in a cached copy can be behind: TouchLastWebhookAsync
    // doesn't clear it (it runs on every webhook); the Integrations page reads the table directly.
    public const string ChannelsPrefix = "channels:integrations:";
    public static string ChannelIntegrations(Guid clinicId) => ChannelsPrefix + clinicId.ToString("N");
    public const string ChannelByIdPrefix = "channels:integration:";
    public static string ChannelIntegration(Guid integrationId) => ChannelByIdPrefix + integrationId.ToString("N");
    public const string TikTokPrefix = "channels:tiktok:";
    public static string TikTok(Guid clinicId) => TikTokPrefix + clinicId.ToString("N");
    public const string WhatsAppTemplatesPrefix = "channels:templates:";
    public static string WhatsAppTemplates(Guid clinicId) => WhatsAppTemplatesPrefix + clinicId.ToString("N");

    // ---- Billing: the cards a clinic's events are rated against (depends on client cards, the clinic's plan and the
    // default card), every rate of a card, and who pays the provider per channel account. Read on every charged event.
    public const string RatingCardsPrefix = "billing:rating-cards:";
    public static string RatingCards(Guid clinicId) => RatingCardsPrefix + clinicId.ToString("N");
    public const string RatesPrefix = "billing:rates:";
    public static string Rates(Guid rateCardId) => RatesPrefix + rateCardId.ToString("N");
    public const string ChannelAccountBillingPrefix = "billing:channel-account:";
    public static string ChannelAccountBilling(Guid channelIntegrationId) => ChannelAccountBillingPrefix + channelIntegrationId.ToString("N");

    /// <summary>The keys (exact) and prefixes to remove when this row is added, changed or deleted; empty for rows that
    /// aren't cached. Called with the row's current values and again with its original ones, so a moved row (another
    /// clinic, another card) clears both places.</summary>
    public static IEnumerable<CacheEviction> EvictionsFor(object row)
    {
        switch (row)
        {
            case Models.Clinic c:
                yield return CacheEviction.Key(Clinic(c.Id));
                break;
            case ClinicUser m:
                yield return CacheEviction.Key(Membership(m.UserId));
                break;
            case Procedure p:
                yield return CacheEviction.Key(Procedures(p.ClinicId));
                break;
            case ClinicAvailabilityRule r:
                yield return CacheEviction.Key(AvailabilityRules(r.ClinicId));
                break;
            case ClinicBookingSettings b:
                yield return CacheEviction.Key(BookingSettings(b.ClinicId));
                break;
            case Models.InboxSettings i:
                yield return CacheEviction.Key(InboxSettings(i.ClinicId));
                break;
            case ClinicAvailabilityException e:
                yield return CacheEviction.Key(AvailabilityExceptions(e.ClinicId));
                break;
            case CalendarIntegration ci:
                yield return CacheEviction.Key(Calendars(ci.ClinicId));
                break;
            case CalendarIntegrationCalendar:
                // The row doesn't carry its clinic; the interceptor resolves it from the tracked connection when it can.
                yield return CacheEviction.Prefix(CalendarsPrefix);
                break;
            case Models.ChannelIntegration ch:
                yield return CacheEviction.Key(ChannelIntegrations(ch.ClinicId));
                yield return CacheEviction.Key(ChannelIntegration(ch.Id));
                break;
            case TikTokIntegration t:
                yield return CacheEviction.Key(TikTok(t.ClinicId));
                break;
            case WhatsAppTemplate w:
                yield return CacheEviction.Key(WhatsAppTemplates(w.ClinicId));
                break;
            case BillingRateCard card:
                // Active/default/client flags decide which cards any clinic is rated against.
                yield return CacheEviction.Prefix(RatingCardsPrefix);
                yield return CacheEviction.Key(Rates(card.Id));
                break;
            case BillingRate rate:
                yield return CacheEviction.Key(Rates(rate.RateCardId));
                break;
            case SubscriptionPlan:
                // A plan's rate card applies to every clinic on it.
                yield return CacheEviction.Prefix(RatingCardsPrefix);
                break;
            case ClinicSubscription s:
                yield return CacheEviction.Key(RatingCards(s.ClinicId));
                break;
            case ChannelAccountBillingSettings a:
                yield return CacheEviction.Key(ChannelAccountBilling(a.ChannelIntegrationId));
                break;
        }
    }

    /// <summary>Everything cached about one clinic, for a delete that cascades through all of its rows.</summary>
    public static IEnumerable<CacheEviction> EvictionsForClinic(Guid clinicId) =>
    [
        CacheEviction.Key(Clinic(clinicId)),
        CacheEviction.Key(Procedures(clinicId)),
        CacheEviction.Key(AvailabilityRules(clinicId)),
        CacheEviction.Key(BookingSettings(clinicId)),
        CacheEviction.Key(InboxSettings(clinicId)),
        CacheEviction.Key(AvailabilityExceptions(clinicId)),
        CacheEviction.Key(Calendars(clinicId)),
        CacheEviction.Key(ChannelIntegrations(clinicId)),
        CacheEviction.Key(TikTok(clinicId)),
        CacheEviction.Key(WhatsAppTemplates(clinicId)),
        CacheEviction.Key(RatingCards(clinicId)),
        CacheEviction.Key(Entitlements(clinicId)),
        // Keyed by user, connection or card id rather than clinic: drop the whole area (a clinic delete is rare).
        CacheEviction.Prefix(MembershipPrefix),
        CacheEviction.Prefix(ChannelByIdPrefix),
        CacheEviction.Prefix(ChannelAccountBillingPrefix),
        CacheEviction.Prefix(RatesPrefix),
        CacheEviction.Prefix(WhatsAppRoutingPrefix),
    ];
}
