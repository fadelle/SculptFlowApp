using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlasticSurgery.Business.Contracts.Engines.Billing;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Providers.Channels;
using PlasticSurgery.Business.Contracts.Services.Inbox;
using PlasticSurgery.Business.Contracts.Services.Notifications;
using PlasticSurgery.Business.Engines.Billing;
using PlasticSurgery.Business.Managers;
using PlasticSurgery.Business.Providers.Channels;
using PlasticSurgery.Business.Services.Inbox;
using PlasticSurgery.Common.Configs;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Billing;
using PlasticSurgery.Entities.Requests.Inbox;
using PlasticSurgery.Entities.Responses.Inbox;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Repositories;
using PlasticSurgery.Persistence.Repositories.Campaigns;
using PlasticSurgery.Persistence.Repositories.Events;
using PlasticSurgery.Persistence.Repositories.Inbox;
using PlasticSurgery.Persistence.Repositories.Leads;
using PlasticSurgery.Persistence.Repositories.WhatsApp;

namespace PlasticSurgery.Tests;

/// <summary>The current channel end to end: MessageService sends a WhatsApp template (provider faked), and the
/// delivery callbacks settle or release it. Everything else — MessageService, the WhatsApp billing policy, the
/// billing core, the database — is the real code.</summary>
[Collection("Postgres")]
public class MessageBillingTests
{
    private readonly PostgresFixture _db;

    public MessageBillingTests(PostgresFixture db) => _db = db;

    private sealed class FakeWhatsApp : IWhatsAppService
    {
        public bool Fail { get; set; }
        public int Sends { get; private set; }

        public Task<string> SendTextMessageAsync(Guid clinicId, string toPhone, string text, CancellationToken ct = default) => Send();

        public Task<string> SendTemplateMessageAsync(Guid clinicId, string toPhone, string templateName, string languageCode,
            IReadOnlyList<string> bodyParameters, CancellationToken ct = default) => Send();

        private Task<string> Send()
        {
            if (Fail) throw new WhatsAppSendException("WhatsApp didn't accept the message.");
            Sends++;
            return Task.FromResult("wamid." + Guid.NewGuid().ToString("N"));
        }
    }

    private sealed class Setup
    {
        public required BillingHarness H { get; init; }
        public required Guid Clinic { get; init; }
        public required Guid Lead { get; init; }
        public required Guid Conversation { get; init; }
        public required Guid MarketingTemplate { get; init; }
        public required Guid UtilityTemplate { get; init; }
        public required FakeWhatsApp WhatsApp { get; init; }
        public required MessageBillingService MessageBilling { get; init; }

        public MessageService NewMessageService(ApplicationDbContext db)
        {
            var config = Config();
            return new MessageService(new ConversationRepository(db), new MessageRepository(db), new LeadRepository(db),
                new WhatsAppTemplateRepository(db), new CampaignRepository(db), new UnitOfWork(db), WhatsApp, new IChannelSender[] { new WhatsAppChannelSender(WhatsApp) },
                NullProxy<IInboxNotifier>.Create(), new EventLogger(new EventLogRepository(db)), NullProxy<INotificationService>.Create(), config,
                MessageBilling, H.Entitlements(db));
        }

        public async Task<MessageResponse> SendTemplateAsync(Guid templateId)
        {
            await using var db = H.Db();
            return (await NewMessageService(db).SendTemplateAsync(Clinic, Conversation, new SendTemplateMessageRequest(templateId, null)))!;
        }

        public async Task StatusAsync(Guid messageId, string status)
        {
            await using var db = H.Db();
            var external = await db.Messages.Where(m => m.Id == messageId).Select(m => m.ExternalMessageId).SingleAsync();
            await NewMessageService(db).IngestAsync(new IngestMessageRequest(Clinic, Conversation, Lead, IngestEventType.StatusUpdate,
                ConversationChannel.WhatsApp, null, external, status, null, null));
        }

        public async Task<BillingUsageRecord?> UsageForAsync(Guid messageId)
        {
            await using var db = H.Db();
            return await db.BillingUsageRecords.AsNoTracking().SingleOrDefaultAsync(u => u.MessageId == messageId);
        }
    }

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WhatsApp:Provider"] = "infobip" }).Build();

    private async Task<Setup> SetupAsync(decimal wallet, bool subscribe = true, bool serviceWindowOpen = false,
        string? providerBilling = null, bool? omniUsageBilling = null)
    {
        var h = new BillingHarness(_db.ConnectionString);
        var clinic = await h.CreateClinicAsync();
        var card = await h.CreateClientCardAsync(clinic);
        await h.AddRateAsync(card, BillableEventTypes.WhatsAppMarketingMessage, 0.0700m, 0.0550m, country: "LB");
        await h.AddRateAsync(card, BillableEventTypes.WhatsAppUtilityMessage, 0.0200m, 0.0150m);
        if (subscribe)
        {
            var plan = await h.CreatePlanAsync(0m, 0m, new Dictionary<string, string> { [EntitlementKeys.Campaigns] = "true" });
            await h.Subscriptions.StartAsync(new StartSubscriptionRequest(clinic, plan, Guid.NewGuid().ToString("N"), true, BillingSource.Admin));
        }
        if (wallet > 0) await h.TopUpAsync(clinic, wallet);

        var now = DateTimeOffset.UtcNow;
        var lead = new Lead { Id = Guid.NewGuid(), ClinicId = clinic, FullName = "Test patient", Phone = "9613123456", CreatedAt = now, UpdatedAt = now };
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), ClinicId = clinic, LeadId = lead.Id, Channel = ConversationChannel.WhatsApp, Mode = ConversationMode.Human,
            ServiceWindowExpiresAt = serviceWindowOpen ? now.AddHours(10) : null, LastCustomerMessageAt = serviceWindowOpen ? now : null,
            CreatedAt = now, UpdatedAt = now
        };
        WhatsAppTemplate Template(string category) => new()
        {
            Id = Guid.NewGuid(), ClinicId = clinic, Name = "t_" + category + Guid.NewGuid().ToString("N")[..6], Category = category,
            Language = "en", Status = WhatsAppTemplateStatus.Approved, Body = "Hello", Provider = ChannelProvider.Infobip,
            CreatedAt = now, UpdatedAt = now
        };
        var marketing = Template(WhatsAppTemplateCategory.Marketing);
        var utility = Template(WhatsAppTemplateCategory.Utility);
        await using (var db = h.Db())
        {
            db.Leads.Add(lead);
            db.Conversations.Add(conversation);
            db.WhatsAppTemplates.AddRange(marketing, utility);
            await db.SaveChangesAsync();
        }

        // The clinic's WhatsApp account (SculptFlow's Infobip sender), optionally with an admin override of who pays.
        var accountId = await h.ConnectChannelAsync(clinic, ChannelType.WhatsApp, ChannelProvider.Infobip);
        if (providerBilling is not null || omniUsageBilling is not null)
        {
            await h.ProviderBilling.SetAsync(accountId, new ChannelAccountBillingChange(providerBilling, omniUsageBilling, "test arrangement", "tests"));
        }

        var messageBilling = new MessageBillingService(h.Billing,
            new IChannelBillingPolicy[] { new WhatsAppBillingPolicy(Config(), Options.Create(new WhatsAppBillingOptions())), new TelegramBillingPolicy() },
            h.ProviderBilling, h.Factory, h.Time, NullLogger<MessageBillingService>.Instance, h.Config);

        return new Setup
        {
            H = h, Clinic = clinic, Lead = lead.Id, Conversation = conversation.Id, MarketingTemplate = marketing.Id,
            UtilityTemplate = utility.Id, WhatsApp = new FakeWhatsApp(), MessageBilling = messageBilling
        };
    }

    [PostgresFact]
    public async Task ATemplateSend_IsReservedBeforeSending_AndSettledWhenDelivered()
    {
        var s = await SetupAsync(wallet: 1m);

        var message = await s.SendTemplateAsync(s.MarketingTemplate);

        var usage = await s.UsageForAsync(message.Id);
        Assert.NotNull(usage);
        Assert.Equal(ChargeStatus.Reserved, usage!.ChargeStatus);
        Assert.Equal(BillableEventTypes.WhatsAppMarketingMessage, usage.EventType);
        Assert.Equal("LB", usage.CountryCode);
        Assert.Equal("infobip", usage.Provider);
        Assert.Equal(0.07m, usage.Amount);
        Assert.Equal(MessageBillingKeys.For(ConversationChannel.WhatsApp, message.Id), usage.IdempotencyKey);
        Assert.Equal(0.07m, (await s.H.AccountAsync(s.Clinic)).ReservedAmount);

        await s.StatusAsync(message.Id, "sent");      // not billable yet
        Assert.Equal(ChargeStatus.Reserved, (await s.UsageForAsync(message.Id))!.ChargeStatus);
        await s.StatusAsync(message.Id, "delivered"); // billable
        await s.StatusAsync(message.Id, "read");      // already settled: no second charge
        await s.StatusAsync(message.Id, "delivered"); // duplicate callback

        usage = await s.UsageForAsync(message.Id);
        Assert.Equal(ChargeStatus.Settled, usage!.ChargeStatus);
        var account = await s.H.AccountAsync(s.Clinic);
        Assert.Equal(0.93m, account.WalletBalance);
        Assert.Equal(0m, account.ReservedAmount);
        Assert.Single((await s.H.LedgerAsync(s.Clinic)), l => l.EntryType == LedgerEntryType.UsageDebit);
        await s.H.AssertReconciledAsync(s.Clinic);
    }

    [PostgresFact]
    public async Task AFailedDelivery_ReleasesTheReservation()
    {
        var s = await SetupAsync(wallet: 1m);
        var message = await s.SendTemplateAsync(s.MarketingTemplate);

        await s.StatusAsync(message.Id, "failed");
        await s.StatusAsync(message.Id, "delivered"); // a late "delivered" after the release must not charge

        Assert.Equal(ChargeStatus.Released, (await s.UsageForAsync(message.Id))!.ChargeStatus);
        var account = await s.H.AccountAsync(s.Clinic);
        Assert.Equal(1m, account.WalletBalance);
        Assert.Equal(0m, account.ReservedAmount);
        await s.H.AssertReconciledAsync(s.Clinic);
    }

    [PostgresFact]
    public async Task AProviderError_ReleasesTheReservation_AndNoMessageIsSaved()
    {
        var s = await SetupAsync(wallet: 1m);
        s.WhatsApp.Fail = true;

        await Assert.ThrowsAsync<WhatsAppSendException>(() => s.SendTemplateAsync(s.MarketingTemplate));

        await using var db = s.H.Db();
        var usage = await db.BillingUsageRecords.AsNoTracking().SingleAsync(u => u.ClinicId == s.Clinic && u.ChargeStatus != ChargeStatus.Failed);
        Assert.Equal(ChargeStatus.Released, usage.ChargeStatus);
        Assert.Equal("send_failed", usage.ReleaseReason);
        Assert.False(await db.Messages.AnyAsync(m => m.ClinicId == s.Clinic));
        Assert.Equal(0m, (await s.H.AccountAsync(s.Clinic)).ReservedAmount);
        await s.H.AssertReconciledAsync(s.Clinic);
    }

    [PostgresFact]
    public async Task NotEnoughBalance_BlocksTheSendBeforeTheProviderIsCalled()
    {
        var s = await SetupAsync(wallet: 0.05m);

        var denied = await Assert.ThrowsAsync<BillingDeniedException>(() => s.SendTemplateAsync(s.MarketingTemplate));

        Assert.Equal(UsageFailureReason.InsufficientFunds, denied.Reason);
        Assert.DoesNotContain("infobip", denied.Message, StringComparison.OrdinalIgnoreCase); // white-label
        Assert.Equal(0, s.WhatsApp.Sends);
        await s.H.AssertReconciledAsync(s.Clinic);
    }

    [PostgresFact]
    public async Task WithoutAnActivePlan_NothingIsSent()
    {
        var s = await SetupAsync(wallet: 5m, subscribe: false);

        await Assert.ThrowsAsync<EntitlementDeniedException>(() => s.SendTemplateAsync(s.MarketingTemplate));
        Assert.Equal(0, s.WhatsApp.Sends);
    }

    [PostgresFact]
    public async Task FreeMessages_CreateNoUsage()
    {
        var s = await SetupAsync(wallet: 1m, serviceWindowOpen: true);

        var utilityInWindow = await s.SendTemplateAsync(s.UtilityTemplate);
        MessageResponse text;
        await using (var db = s.H.Db())
        {
            text = (await s.NewMessageService(db).SendAsync(s.Clinic, s.Conversation, new SendMessageRequest("Thanks, see you Monday")))!;
        }

        Assert.Null(await s.UsageForAsync(utilityInWindow.Id));
        Assert.Null(await s.UsageForAsync(text.Id));
        Assert.Equal(2, s.WhatsApp.Sends);
        Assert.Equal(1m, (await s.H.AccountAsync(s.Clinic)).Spendable);
    }

    [PostgresFact]
    public async Task ReservationsWithoutAnOutcome_AreResolvedFromTheMessageAfterTheTimeout()
    {
        var s = await SetupAsync(wallet: 1m);
        var deliveredButCallbackLost = await s.SendTemplateAsync(s.MarketingTemplate);
        var neverDelivered = await s.SendTemplateAsync(s.MarketingTemplate);
        await using (var db = s.H.Db())
        {
            // The delivery happened, but its callback never reached billing (e.g. a crash while processing it).
            var row = await db.Messages.SingleAsync(m => m.Id == deliveredButCallbackLost.Id);
            row.DeliveredAt = DateTimeOffset.UtcNow;
            row.DeliveryStatus = "delivered";
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, await s.MessageBilling.ResolveStaleReservationsAsync()); // not stale yet
        s.H.Time.Advance(TimeSpan.FromHours(73));
        await s.MessageBilling.ResolveStaleReservationsAsync();

        Assert.Equal(ChargeStatus.Settled, (await s.UsageForAsync(deliveredButCallbackLost.Id))!.ChargeStatus);
        var released = (await s.UsageForAsync(neverDelivered.Id))!;
        Assert.Equal(ChargeStatus.Released, released.ChargeStatus);
        Assert.Equal("reservation_timeout", released.ReleaseReason);
        var account = await s.H.AccountAsync(s.Clinic);
        Assert.Equal(0.93m, account.WalletBalance);
        Assert.Equal(0m, account.ReservedAmount);
        await s.H.AssertReconciledAsync(s.Clinic);
    }

    [PostgresFact]
    public async Task WithBillingOff_SendsWorkExactlyAsBefore()
    {
        var s = await SetupAsync(wallet: 0m, subscribe: false);
        s.H.SetSetting("Billing", "Enabled", "false");

        var message = await s.SendTemplateAsync(s.MarketingTemplate);
        await s.StatusAsync(message.Id, "delivered");

        Assert.Equal(1, s.WhatsApp.Sends);
        Assert.Null(await s.UsageForAsync(message.Id));
    }

    // ---- provider billing responsibility, per connected account --------------------------------------------------

    [PostgresFact]
    public async Task ACustomerDirectWaba_RecordsUsage_ButNeverTouchesTheWalletCreditOrLedger()
    {
        var s = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.CustomerDirect);
        await s.H.Billing.AdjustAsync(new WalletAdjustment(s.Clinic, 25m, LedgerBalanceType.IncludedCredit, "plan credit", Guid.NewGuid().ToString("N"), BillingSource.Admin, "tests"));

        var message = await s.SendTemplateAsync(s.MarketingTemplate);
        var usage = (await s.UsageForAsync(message.Id))!;
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(ProviderBillingResponsibility.CustomerDirect, usage.ProviderBilling);
        Assert.Equal(ProviderOutcome.Pending, usage.ProviderOutcome);
        Assert.Equal(0m, usage.Amount);
        Assert.Equal(0.055m, usage.ProviderCost); // the provider cost is still known, for reporting — the clinic pays it to Meta
        Assert.NotNull(usage.ChannelIntegrationId);
        Assert.Equal(0m, (await s.H.AccountAsync(s.Clinic)).ReservedAmount);

        await s.StatusAsync(message.Id, "delivered");
        await s.StatusAsync(message.Id, "delivered"); // duplicate callback

        usage = (await s.UsageForAsync(message.Id))!;
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(ProviderOutcome.Billable, usage.ProviderOutcome);
        var account = await s.H.AccountAsync(s.Clinic);
        Assert.Equal(1m, account.WalletBalance);
        Assert.Equal(25m, account.IncludedCreditBalance); // included credit untouched
        Assert.Equal(0m, account.ReservedAmount);
        Assert.DoesNotContain(await s.H.LedgerAsync(s.Clinic),
            l => l.EntryType is LedgerEntryType.UsageDebit or LedgerEntryType.IncludedCreditConsumption);
        await s.H.AssertReconciledAsync(s.Clinic);
    }

    [PostgresFact]
    public async Task ACustomerDirectWaba_SendsEvenWithAnEmptyWallet()
    {
        var s = await SetupAsync(wallet: 0m, providerBilling: ProviderBillingResponsibility.CustomerDirect);

        var message = await s.SendTemplateAsync(s.MarketingTemplate);

        Assert.Equal(1, s.WhatsApp.Sends);
        Assert.Equal(ChargeStatus.NotCharged, (await s.UsageForAsync(message.Id))!.ChargeStatus);
    }

    [PostgresFact]
    public async Task TwoWhatsAppAccounts_OneCustomerDirect_OneSculptFlowFunded_BehaveDifferently()
    {
        var direct = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.CustomerDirect);
        var funded = await SetupAsync(wallet: 1m); // default arrangement: SculptFlow pays the provider

        var directMessage = await direct.SendTemplateAsync(direct.MarketingTemplate);
        var fundedMessage = await funded.SendTemplateAsync(funded.MarketingTemplate);
        await direct.StatusAsync(directMessage.Id, "delivered");
        await funded.StatusAsync(fundedMessage.Id, "delivered");

        var directUsage = (await direct.UsageForAsync(directMessage.Id))!;
        var fundedUsage = (await funded.UsageForAsync(fundedMessage.Id))!;
        Assert.Equal(ChargeStatus.NotCharged, directUsage.ChargeStatus);
        Assert.Equal(ChargeStatus.Settled, fundedUsage.ChargeStatus);
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, fundedUsage.ProviderBilling);
        Assert.Equal(0.07m, fundedUsage.Amount);
        Assert.Equal(1m, (await direct.H.AccountAsync(direct.Clinic)).WalletBalance);
        Assert.Equal(0.93m, (await funded.H.AccountAsync(funded.Clinic)).WalletBalance);
        await direct.H.AssertReconciledAsync(direct.Clinic);
        await funded.H.AssertReconciledAsync(funded.Clinic);
    }

    [PostgresFact]
    public async Task AnExternalProviderAccount_IsRecorded_WithNoSculptFlowDeduction()
    {
        var s = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.ExternalProviderDirect);

        var message = await s.SendTemplateAsync(s.MarketingTemplate);
        await s.StatusAsync(message.Id, "failed");

        var usage = (await s.UsageForAsync(message.Id))!;
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(ProviderOutcome.NotBillable, usage.ProviderOutcome);
        Assert.Equal(1m, (await s.H.AccountAsync(s.Clinic)).Spendable);
        await s.H.AssertReconciledAsync(s.Clinic);
    }

    [PostgresFact]
    public async Task ASculptFlowUsageFee_OnACustomerDirectAccount_UsesOnlyAFeeRate_NeverThePlatformRate()
    {
        // Usage billing turned on for a customer-paid WABA, but no fee rate exists yet: refused, never the $0.07 platform price.
        var noFee = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.CustomerDirect, omniUsageBilling: true);
        var refused = await Assert.ThrowsAsync<BillingDeniedException>(() => noFee.SendTemplateAsync(noFee.MarketingTemplate));
        Assert.Equal(UsageFailureReason.RateNotFound, refused.Reason);
        Assert.Equal(0, noFee.WhatsApp.Sends);

        // With a fee rate for customer-direct accounts: SculptFlow charges its fee, independent of Meta's cost.
        var withFee = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.CustomerDirect, omniUsageBilling: true);
        var card = (await withFee.H.ProviderBilling.ListAccountsAsync(withFee.Clinic)).Single();
        Assert.True(card.OmniUsageBilling);
        var feeCard = (await withFee.H.RateCards.ListAsync()).Single(c => c.ClinicId == withFee.Clinic).Code;
        await withFee.H.AddRateAsync(feeCard, BillableEventTypes.WhatsAppMarketingMessage, clientRate: 0.01m, providerCost: 0.055m,
            providerBilling: ProviderBillingResponsibility.CustomerDirect);

        var message = await withFee.SendTemplateAsync(withFee.MarketingTemplate);
        await withFee.StatusAsync(message.Id, "delivered");

        var usage = (await withFee.UsageForAsync(message.Id))!;
        Assert.Equal(ChargeStatus.Settled, usage.ChargeStatus);
        Assert.Equal(ProviderBillingResponsibility.CustomerDirect, usage.ProviderBilling);
        Assert.Equal(0.01m, usage.Amount);
        Assert.Equal(0.99m, (await withFee.H.AccountAsync(withFee.Clinic)).WalletBalance);
        await withFee.H.AssertReconciledAsync(withFee.Clinic);
    }

    [PostgresFact]
    public async Task ChangingAnAccountsArrangement_NeverChangesRecordedUsage()
    {
        var s = await SetupAsync(wallet: 1m);
        var before = await s.SendTemplateAsync(s.MarketingTemplate);
        await s.StatusAsync(before.Id, "delivered");

        var accountId = (await s.H.ProviderBilling.ListAccountsAsync(s.Clinic)).Single().ChannelIntegrationId;
        await s.H.ProviderBilling.SetAsync(accountId, new ChannelAccountBillingChange(ProviderBillingResponsibility.CustomerDirect, null,
            "clinic moved to its own WABA payment method", "tests"));
        var after = await s.SendTemplateAsync(s.MarketingTemplate);

        var old = (await s.UsageForAsync(before.Id))!;
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, old.ProviderBilling);
        Assert.Equal(ChargeStatus.Settled, old.ChargeStatus);
        Assert.Equal(0.07m, old.Amount);
        Assert.Equal(ChargeStatus.NotCharged, (await s.UsageForAsync(after.Id))!.ChargeStatus);
        await using (var db = s.H.Db())
        {
            Assert.True(await db.Events.AnyAsync(e => e.ClinicId == s.Clinic && e.EventType == BillingEventTypes.ProviderBillingChanged));
        }
        await s.H.AssertReconciledAsync(s.Clinic);
    }

    [PostgresFact]
    public async Task TheClinicBillingView_ShowsProviderDirectUsageApartFromSculptFlowCharges()
    {
        var s = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.CustomerDirect);
        var message = await s.SendTemplateAsync(s.MarketingTemplate);
        await s.StatusAsync(message.Id, "delivered");

        var summary = await s.H.Queries.GetSummaryAsync(s.Clinic);

        Assert.Empty(summary.Usage); // nothing charged by SculptFlow
        var direct = Assert.Single(summary.ProviderDirectUsage);
        Assert.Equal(1, direct.Count);
        Assert.Equal("Paid by you directly to Meta", direct.PaidBy);
        var account = Assert.Single(summary.ChannelAccounts);
        Assert.False(account.ChargedBySculptFlow);
        Assert.DoesNotContain("infobip", account.PaidBy, StringComparison.OrdinalIgnoreCase);
    }
}
