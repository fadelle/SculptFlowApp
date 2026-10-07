using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PlasticSurgery.Business.Engines.Billing;
using PlasticSurgery.Common.Configs;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Tests;

/// <summary>Rules that need no database: rate selection, destination country, the WhatsApp billing policy,
/// entitlement values and evaluation.</summary>
public class PureBillingRuleTests
{
    private static readonly Guid ClientCard = Guid.NewGuid();
    private static readonly Guid PlanCard = Guid.NewGuid();
    private static readonly Guid DefaultCard = Guid.NewGuid();

    private static BillingRate Rate(Guid card, decimal price, string? country = null, string? op = null, string? provider = null) => new()
    {
        Id = Guid.NewGuid(), RateCardId = card, EventType = "x", ClientRate = price, CountryCode = country, Operator = op, Provider = provider,
        EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1)
    };

    [Fact]
    public void RateSelector_PrefersTheClientCard_ThenThePlanCard_ThenTheDefault()
    {
        var order = new[] { ClientCard, PlanCard, DefaultCard };
        var defaultSpecific = Rate(DefaultCard, 1m, country: "LB", provider: "infobip");
        var planGeneric = Rate(PlanCard, 2m);
        Assert.Equal(2m, RateSelector.Select(new[] { defaultSpecific, planGeneric }, order)!.ClientRate);

        var clientGeneric = Rate(ClientCard, 3m);
        Assert.Equal(3m, RateSelector.Select(new[] { defaultSpecific, planGeneric, clientGeneric }, order)!.ClientRate);
    }

    [Fact]
    public void RateSelector_InsideOneCard_TheMostSpecificRateWins()
    {
        var order = new[] { DefaultCard };
        var any = Rate(DefaultCard, 1m);
        var provider = Rate(DefaultCard, 2m, provider: "infobip");
        var country = Rate(DefaultCard, 3m, country: "LB");
        var countryOperator = Rate(DefaultCard, 4m, country: "LB", op: "alfa");

        Assert.Equal(1m, RateSelector.Select(new[] { any }, order)!.ClientRate);
        Assert.Equal(2m, RateSelector.Select(new[] { any, provider }, order)!.ClientRate);
        Assert.Equal(3m, RateSelector.Select(new[] { any, provider, country }, order)!.ClientRate);
        Assert.Equal(4m, RateSelector.Select(new[] { any, provider, country, countryOperator }, order)!.ClientRate);
    }

    [Fact]
    public void RateSelector_ARateForTheAccountsArrangement_BeatsTheGenericRate()
    {
        var order = new[] { DefaultCard };
        var generic = Rate(DefaultCard, 0.07m);
        var feeForCustomerPaid = Rate(DefaultCard, 0.01m);
        feeForCustomerPaid.ProviderBilling = ProviderBillingResponsibility.CustomerDirect;
        Assert.Equal(0.01m, RateSelector.Select(new[] { generic, feeForCustomerPaid }, order)!.ClientRate);
        // ...but a country-specific rate still outranks the arrangement dimension.
        var lebanon = Rate(DefaultCard, 0.05m, country: "LB");
        Assert.Equal(0.05m, RateSelector.Select(new[] { generic, feeForCustomerPaid, lebanon }, order)!.ClientRate);
    }

    [Fact]
    public void BillableEvents_AreChargedByDefault_OnlyWhenSculptFlowPaysTheProvider()
    {
        var e = new BillableEvent { ClinicId = Guid.NewGuid(), IdempotencyKey = "k", EventType = "x", Channel = "c" };
        Assert.True(e.ChargesUsage);
        Assert.False((e with { ProviderBilling = ProviderBillingResponsibility.CustomerDirect }).ChargesUsage);
        Assert.False((e with { ProviderBilling = ProviderBillingResponsibility.ExternalProviderDirect }).ChargesUsage);
        Assert.False((e with { ProviderBilling = ProviderBillingResponsibility.NoProviderUsageFee }).ChargesUsage);
        Assert.True((e with { ProviderBilling = ProviderBillingResponsibility.CustomerDirect, ChargeUsage = true }).ChargesUsage);
        Assert.False((e with { ChargeUsage = false }).ChargesUsage);
    }

    [Fact]
    public void RateSelector_NoCandidates_MeansNoPrice()
    {
        Assert.Null(RateSelector.Select(Array.Empty<BillingRate>(), new[] { DefaultCard }));
        Assert.Null(RateSelector.Select(new[] { Rate(Guid.NewGuid(), 1m) }, new[] { DefaultCard })); // a card not in the clinic's hierarchy
    }

    [Theory]
    [InlineData("+961 3 123 456", "LB")]
    [InlineData("9613123456", "LB")]       // WhatsApp wa_id: digits only, no +
    [InlineData("00447860099299", "GB")]
    [InlineData("+14155550100", "US")]
    [InlineData("+77011234567", "KZ")]
    [InlineData("+79161234567", "RU")]
    [InlineData("+971501234567", "AE")]
    [InlineData("+201001234567", "EG")]
    [InlineData("03123456", null)]         // national format: can't tell
    [InlineData("", null)]
    [InlineData(null, null)]
    public void PhoneCountry_ResolvesTheDestinationCountry(string? phone, string? expected) =>
        Assert.Equal(expected, PhoneCountry.FromPhone(phone));

    private static WhatsAppBillingPolicy Policy(WhatsAppBillingOptions? options = null, string provider = "infobip")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WhatsApp:Provider"] = provider }).Build();
        return new WhatsAppBillingPolicy(config, Options.Create(options ?? new WhatsAppBillingOptions()));
    }

    private static OutboundMessageBillingContext Message(OutboundMessageKind kind, string? category = null, bool windowOpen = false) =>
        new(Guid.NewGuid(), ConversationChannel.WhatsApp, Guid.NewGuid(), Guid.NewGuid(), null, kind, category, windowOpen, "+9613123456");

    [Fact]
    public void WhatsAppPolicy_TemplatesAreBilledByCategory_ToTheRecipientsCountry_AndActiveProvider()
    {
        var decision = Policy().DescribeOutbound(Message(OutboundMessageKind.Template, "MARKETING"));
        Assert.NotNull(decision);
        Assert.Equal(BillableEventTypes.WhatsAppMarketingMessage, decision!.EventType);
        Assert.Equal(1, decision.Quantity);
        Assert.Equal("LB", decision.CountryCode);
        Assert.Equal("infobip", decision.Provider);
        Assert.Equal(BillingSettlementTrigger.OnDeliveryStatus, decision.SettleWhen);

        Assert.Equal(BillableEventTypes.WhatsAppAuthenticationMessage,
            Policy().DescribeOutbound(Message(OutboundMessageKind.Template, "authentication"))!.EventType);
    }

    [Fact]
    public void WhatsAppPolicy_FreeFormRepliesAndUtilityInsideTheWindow_AreFree()
    {
        Assert.Null(Policy().DescribeOutbound(Message(OutboundMessageKind.Text, windowOpen: true)));
        Assert.Null(Policy().DescribeOutbound(Message(OutboundMessageKind.Template, "utility", windowOpen: true)));
        Assert.Equal(BillableEventTypes.WhatsAppUtilityMessage,
            Policy().DescribeOutbound(Message(OutboundMessageKind.Template, "utility", windowOpen: false))!.EventType);
        // Marketing is billed even inside the window.
        Assert.NotNull(Policy().DescribeOutbound(Message(OutboundMessageKind.Template, "marketing", windowOpen: true)));
    }

    [Fact]
    public void WhatsAppPolicy_PricingRulesAreConfiguration_NotCode()
    {
        var policy = Policy(new WhatsAppBillingOptions
        {
            ServiceMessageEvent = "whatsapp_service_message",
            FreeInsideServiceWindow = new List<string> { "none" },
            TemplateCategoryEvents = new Dictionary<string, string> { ["marketing"] = "whatsapp_promo" },
            BillableStatuses = new List<string> { "sent" }
        });
        Assert.Equal("whatsapp_service_message", policy.DescribeOutbound(Message(OutboundMessageKind.Text, windowOpen: true))!.EventType);
        Assert.Equal("whatsapp_promo", policy.DescribeOutbound(Message(OutboundMessageKind.Template, "marketing"))!.EventType);
        Assert.NotNull(policy.DescribeOutbound(Message(OutboundMessageKind.Template, "utility", windowOpen: true)));
        Assert.Equal(DeliveryBillingAction.Settle, policy.OnDeliveryStatus("sent"));
        Assert.Equal(DeliveryBillingAction.None, policy.OnDeliveryStatus("delivered"));
    }

    [Fact]
    public void WhatsAppPolicy_UnknownCategory_GetsItsOwnEventType_SoItIsRefusedUntilPriced()
    {
        Assert.Equal("whatsapp_service_update_message", Policy().DescribeOutbound(Message(OutboundMessageKind.Template, "service_update"))!.EventType);
    }

    [Fact]
    public void WhatsAppPolicy_DeliveredSettles_FailedReleases_SentWaits()
    {
        var policy = Policy();
        Assert.Equal(DeliveryBillingAction.Settle, policy.OnDeliveryStatus("delivered"));
        Assert.Equal(DeliveryBillingAction.Settle, policy.OnDeliveryStatus("read"));
        Assert.Equal(DeliveryBillingAction.Release, policy.OnDeliveryStatus("failed"));
        Assert.Equal(DeliveryBillingAction.None, policy.OnDeliveryStatus("sent"));

        Assert.Equal(DeliveryBillingAction.Settle, policy.ResolveStale(new Message { DeliveredAt = DateTimeOffset.UtcNow }));
        Assert.Equal(DeliveryBillingAction.Release, policy.ResolveStale(new Message { SentAt = DateTimeOffset.UtcNow, DeliveryStatus = "sent" }));
        Assert.Equal(DeliveryBillingAction.Release, policy.ResolveStale(new Message { FailedAt = DateTimeOffset.UtcNow }));
        Assert.Equal(DeliveryBillingAction.Release, policy.ResolveStale(null));
    }

    [Theory]
    [InlineData(EntitlementKeys.Campaigns, "TRUE", "true")]
    [InlineData(EntitlementKeys.MaxAgents, " 5 ", "5")]
    [InlineData(EntitlementKeys.MaxAgents, "Unlimited", "unlimited")]
    public void EntitlementValues_AreNormalized(string key, string value, string expected) =>
        Assert.Equal(expected, EntitlementCatalog.NormalizeValue(key, value));

    [Theory]
    [InlineData(EntitlementKeys.Campaigns, "5")]
    [InlineData(EntitlementKeys.MaxAgents, "yes")]
    [InlineData(EntitlementKeys.MaxAgents, "-1")]
    [InlineData("not_a_key", "true")]
    public void EntitlementValues_OfTheWrongKind_AreRejected(string key, string value) =>
        Assert.Throws<ArgumentException>(() => EntitlementCatalog.NormalizeValue(key, value));

    [Fact]
    public void ClinicEntitlements_AnswerFromThePlan_NotFromThePlanName()
    {
        var values = new Dictionary<string, string>
        {
            [EntitlementKeys.Campaigns] = "true", [EntitlementKeys.AiAgent] = "false",
            [EntitlementKeys.MaxAgents] = "3", [EntitlementKeys.MaxChannelConnections] = "unlimited"
        };
        var active = new ClinicEntitlements(true, true, SubscriptionStatus.Active, "growth", "Growth", null, values);
        Assert.True(active.CanSendMessages);
        Assert.True(active.CanUseCampaigns);
        Assert.False(active.CanUseAiAgent);
        Assert.False(active.CanUseAdvancedReporting); // not listed = off
        Assert.Equal(3, active.MaximumAgents);
        Assert.Null(active.MaximumChannelConnections); // unlimited
        Assert.Equal(0, active.MaximumWhatsAppNumbers); // not listed = 0

        var expired = new ClinicEntitlements(true, false, SubscriptionStatus.Expired, "growth", "Growth", null, values);
        Assert.False(expired.CanSendMessages);
        Assert.False(expired.CanUseCampaigns);
        Assert.Equal(0, expired.MaximumAgents);

        Assert.True(ClinicEntitlements.Unrestricted.CanUseCampaigns);
        Assert.Null(ClinicEntitlements.Unrestricted.MaximumAgents);
    }
}
