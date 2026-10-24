using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Omni.App.Business.Contracts.Engines.Billing;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Providers.Channels;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Engines.Billing;
using Omni.App.Business.Managers;
using Omni.App.Business.Providers.Channels;
using Omni.App.Business.Services.Inbox;
using Omni.App.Common.Configs;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Billing;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Responses.Inbox;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Repositories.Channels;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Campaigns;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Events;
using Omni.App.Persistence.Repositories.Inbox;
using Omni.App.Persistence.Repositories.Contacts;
using Omni.App.Persistence.Repositories.WhatsApp;

namespace Omni.App.Tests;

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

        public Task<string> SendTextMessageAsync(Guid clientId, string toPhone, string text, CancellationToken ct = default) => Send();

        public Task<string> SendTemplateMessageAsync(Guid clientId, string toPhone, string templateName, string languageCode,
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
        public required Guid Client { get; init; }
        public required string Card { get; init; }
        public required Guid Contact { get; init; }
        public required Guid Conversation { get; init; }
        public required Guid MarketingTemplate { get; init; }
        public required Guid UtilityTemplate { get; init; }
        public required FakeWhatsApp WhatsApp { get; init; }
        public required MessageBillingService MessageBilling { get; init; }

        public Mock<IConversationService> Conversations { get; } = new();

        public MessageService NewMessageService(ApplicationDbContext db, IOptOutService? optOut = null)
        {
            return new MessageService(new ConversationRepository(db), new MessageRepository(db), new ContactRepository(db),
                new WhatsAppTemplateRepository(db), new CampaignRepository(db), new UnitOfWork(db), WhatsApp, new IChannelSender[] { new WhatsAppChannelSender(WhatsApp) },
                NullProxy<IInboxNotifier>.Create(), new EventLogger(new EventLogRepository(db)), NullProxy<INotificationService>.Create(),
                MessageBilling, H.Entitlements(db), new InboxSettingsService(new InboxSettingsRepository(db), new ClientRepository(db), H.Entitlements(db), new ChannelIntegrationRepository(db), new UnitOfWork(db)), Mock.Of<IMessageTracer>(),
                Conversations.Object, optOut ?? Mock.Of<IOptOutService>(), NoAutoReplies());
        }

        private static IAutoReplyService NoAutoReplies()
        {
            var autoReplies = new Mock<IAutoReplyService>();
            autoReplies.Setup(a => a.ChooseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AutoReplyChoice.Skip(AutoReplyRules.SkipOff));
            return autoReplies.Object;
        }

        public async Task<MessageResponse> SendTemplateAsync(Guid templateId)
        {
            await using var db = H.Db();
            return (await NewMessageService(db).SendTemplateAsync(Client, Conversation, new SendTemplateMessageRequest(templateId, null)))!;
        }

        public async Task StatusAsync(Guid messageId, string status)
        {
            await using var db = H.Db();
            var external = await db.Messages.Where(m => m.Id == messageId).Select(m => m.ExternalMessageId).SingleAsync();
            await NewMessageService(db).IngestAsync(new IngestMessageRequest(Client, Conversation, Contact, IngestEventType.StatusUpdate,
                ConversationChannel.WhatsApp, null, external, status, null, null));
        }

        public async Task<MessageResponse> SendAiReplyAsync(string text)
        {
            await using var db = H.Db();
            return (await NewMessageService(db).SendAiReplyAsync(Client, Conversation, text))!;
        }

        public async Task<BillingUsageRecord?> UsageForAsync(Guid messageId)
        {
            await using var db = H.Db();
            return await db.BillingUsageRecords.AsNoTracking().SingleOrDefaultAsync(u => u.MessageId == messageId);
        }
    }

    private async Task<Setup> SetupAsync(decimal wallet, bool subscribe = true, bool serviceWindowOpen = false,
        string? providerBilling = null, bool? omniUsageBilling = null, decimal planCredit = 0m, string mode = ConversationMode.Human)
    {
        var h = new BillingHarness(_db.ConnectionString);
        var client = await h.CreateClientAsync();
        var card = await h.CreateClientCardAsync(client);
        await h.AddRateAsync(card, BillableEventTypes.WhatsAppMarketingMessage, 0.0700m, 0.0550m, country: "LB");
        await h.AddRateAsync(card, BillableEventTypes.WhatsAppUtilityMessage, 0.0200m, 0.0150m);
        if (subscribe)
        {
            var plan = await h.CreatePlanAsync(0m, planCredit, new Dictionary<string, string> { [EntitlementKeys.Campaigns] = "true" });
            await h.Subscriptions.StartAsync(new StartSubscriptionRequest(client, plan, Guid.NewGuid().ToString("N"), true, BillingSource.Admin));
        }
        if (wallet > 0) await h.TopUpAsync(client, wallet);

        var now = DateTimeOffset.UtcNow;
        var contact = new Contact { Id = Guid.NewGuid(), ClientId = client, FullName = "Test contact", Phone = "9613123456", CreatedAt = now, UpdatedAt = now };
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), ClientId = client, ContactId = contact.Id, Channel = ConversationChannel.WhatsApp, Mode = mode,
            ServiceWindowExpiresAt = serviceWindowOpen ? now.AddHours(10) : null, LastCustomerMessageAt = serviceWindowOpen ? now : null,
            CreatedAt = now, UpdatedAt = now
        };
        WhatsAppTemplate Template(string category) => new()
        {
            Id = Guid.NewGuid(), ClientId = client, Name = "t_" + category + Guid.NewGuid().ToString("N")[..6], Category = category,
            Language = "en", Status = WhatsAppTemplateStatus.Approved, Body = "Hello",
            CreatedAt = now, UpdatedAt = now
        };
        var marketing = Template(WhatsAppTemplateCategory.Marketing);
        var utility = Template(WhatsAppTemplateCategory.Utility);
        await using (var db = h.Db())
        {
            db.Contacts.Add(contact);
            db.Conversations.Add(conversation);
            db.WhatsAppTemplates.AddRange(marketing, utility);
            await db.SaveChangesAsync();
        }

        // The client's WhatsApp account, optionally with an admin override of who pays.
        var accountId = await h.ConnectChannelAsync(client, ChannelType.WhatsApp);
        if (providerBilling is not null || omniUsageBilling is not null)
        {
            await h.ProviderBilling.SetAsync(accountId, new ChannelAccountBillingChange(providerBilling, omniUsageBilling, "test arrangement", "tests"));
        }

        var messageBilling = new MessageBillingService(h.Billing,
            new IChannelBillingPolicy[] { new WhatsAppBillingPolicy(Options.Create(new WhatsAppBillingOptions())), new TelegramBillingPolicy() },
            h.ProviderBilling, h.Factory, h.Time, NullLogger<MessageBillingService>.Instance, h.Config);

        return new Setup
        {
            H = h, Client = client, Card = card, Contact = contact.Id, Conversation = conversation.Id, MarketingTemplate = marketing.Id,
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
        Assert.Equal(ChannelProvider.Meta, usage.Provider);
        Assert.Equal(0.07m, usage.Amount);
        Assert.Equal(MessageBillingKeys.For(ConversationChannel.WhatsApp, message.Id), usage.IdempotencyKey);
        Assert.Equal(0.07m, (await s.H.AccountAsync(s.Client)).ReservedAmount);

        await s.StatusAsync(message.Id, "sent");      // not billable yet
        Assert.Equal(ChargeStatus.Reserved, (await s.UsageForAsync(message.Id))!.ChargeStatus);
        await s.StatusAsync(message.Id, "delivered"); // billable
        await s.StatusAsync(message.Id, "read");      // already settled: no second charge
        await s.StatusAsync(message.Id, "delivered"); // duplicate callback

        usage = await s.UsageForAsync(message.Id);
        Assert.Equal(ChargeStatus.Settled, usage!.ChargeStatus);
        var account = await s.H.AccountAsync(s.Client);
        Assert.Equal(0.93m, account.WalletBalance);
        Assert.Equal(0m, account.ReservedAmount);
        Assert.Single((await s.H.LedgerAsync(s.Client)), l => l.EntryType == LedgerEntryType.UsageDebit);
        await s.H.AssertReconciledAsync(s.Client);
    }

    [PostgresFact]
    public async Task AiReplies_ArePaidFromIncludedCreditThenWallet_AndRefusedWhenNeitherCovers()
    {
        // AI plan: $0.05 of included credit, an ai_reply rate of $0.02 (the client's card here; normally the default card). Free-form WhatsApp replies
        // inside the service window aren't billed by the channel, so only ai_reply is charged.
        var s = await SetupAsync(wallet: 0m, serviceWindowOpen: true, planCredit: 0.05m, mode: ConversationMode.Ai);
        await s.H.AddRateAsync(s.Card, BillableEventTypes.AiReply, 0.02m, 0.004m);

        var first = await s.SendAiReplyAsync("Hello!");
        var second = await s.SendAiReplyAsync("Anything else?");

        foreach (var message in new[] { first, second })
        {
            await using var db = s.H.Db();
            var usage = await db.BillingUsageRecords.AsNoTracking().SingleAsync(u => u.IdempotencyKey == MessageBillingKeys.AiReply(message.Id));
            Assert.Equal((ChargeStatus.Settled, BillableEventTypes.AiReply, 0.02m, 0.02m, 0m), (usage.ChargeStatus, usage.EventType, usage.Amount, usage.CreditAmount, usage.WalletAmount));
            Assert.Equal(message.Id, usage.MessageId);
        }
        var account = await s.H.AccountAsync(s.Client);
        Assert.Equal((0.01m, 0m, 0m), (account.IncludedCreditBalance, account.WalletBalance, account.ReservedAmount));

        // $0.01 of credit left, empty wallet: the third reply is refused, not sent, and the team gets the conversation.
        var sends = s.WhatsApp.Sends;
        var ex = await Assert.ThrowsAsync<AiReplyRefusedException>(() => s.SendAiReplyAsync("Third"));
        Assert.Equal(UsageFailureReason.InsufficientFunds, ex.Reason);
        Assert.Equal(sends, s.WhatsApp.Sends);
        s.Conversations.Verify(c => c.HandOverUnpaidAiReplyAsync(s.Client, s.Conversation, UsageFailureReason.InsufficientFunds, It.IsAny<CancellationToken>()), Times.Once);
        await using (var db = s.H.Db())
        {
            var refused = await db.BillingUsageRecords.AsNoTracking()
                .Where(u => u.ClientId == s.Client && u.EventType == BillableEventTypes.AiReply && u.ChargeStatus == ChargeStatus.Failed).ToListAsync();
            Assert.Equal(UsageFailureReason.InsufficientFunds, Assert.Single(refused).FailureReason);
            Assert.Equal(2, await db.Messages.CountAsync(m => m.ConversationId == s.Conversation)); // no row for the refused reply
        }

        // A top-up pays the next one: credit first, the rest from the wallet.
        await s.H.TopUpAsync(s.Client, 1m);
        var fourth = await s.SendAiReplyAsync("Back again");
        await using (var db = s.H.Db())
        {
            var aiUsage = await db.BillingUsageRecords.AsNoTracking().SingleAsync(u => u.IdempotencyKey == MessageBillingKeys.AiReply(fourth.Id));
            Assert.Equal((0.01m, 0.01m), (aiUsage.CreditAmount, aiUsage.WalletAmount));
        }
        account = await s.H.AccountAsync(s.Client);
        Assert.Equal((0m, 0.99m), (account.IncludedCreditBalance, account.WalletBalance));
        await s.H.AssertReconciledAsync(s.Client);
    }

    [PostgresFact]
    public async Task AnAiReply_WithNoAiReplyRate_IsRefused_AndAFailedSendReturnsTheHold()
    {
        var s = await SetupAsync(wallet: 1m, serviceWindowOpen: true, mode: ConversationMode.Ai);

        var ex = await Assert.ThrowsAsync<AiReplyRefusedException>(() => s.SendAiReplyAsync("Hi"));
        Assert.Equal(UsageFailureReason.RateNotFound, ex.Reason);
        Assert.Equal(0, s.WhatsApp.Sends);
        s.Conversations.Verify(c => c.HandOverUnpaidAiReplyAsync(s.Client, s.Conversation, UsageFailureReason.RateNotFound, It.IsAny<CancellationToken>()), Times.Once);

        await s.H.AddRateAsync(s.Card, BillableEventTypes.AiReply, 0.02m);
        s.WhatsApp.Fail = true;
        await Assert.ThrowsAnyAsync<Exception>(() => s.SendAiReplyAsync("Hi again"));
        await using (var db = s.H.Db())
        {
            var released = await db.BillingUsageRecords.AsNoTracking()
                .SingleAsync(u => u.ClientId == s.Client && u.EventType == BillableEventTypes.AiReply && u.ChargeStatus == ChargeStatus.Released);
            Assert.Equal((0m, 0m), (released.CreditAmount, released.WalletAmount)); // nothing paid
        }
        var account = await s.H.AccountAsync(s.Client);
        Assert.Equal((1m, 0m), (account.WalletBalance, account.ReservedAmount));
        await s.H.AssertReconciledAsync(s.Client);
    }

    [PostgresFact]
    public async Task AiReplies_AreFreeWhileChargeAiRepliesIsOff()
    {
        var s = await SetupAsync(wallet: 0m, serviceWindowOpen: true, mode: ConversationMode.Ai);
        s.H.SetSetting("Billing", "ChargeAiReplies", "false");

        var message = await s.SendAiReplyAsync("Hello");

        Assert.Equal(1, s.WhatsApp.Sends);
        await using var db = s.H.Db();
        Assert.False(await db.BillingUsageRecords.AnyAsync(u => u.IdempotencyKey == MessageBillingKeys.AiReply(message.Id)));
    }

    [PostgresFact]
    public async Task AFailedDelivery_ReleasesTheReservation()
    {
        var s = await SetupAsync(wallet: 1m);
        var message = await s.SendTemplateAsync(s.MarketingTemplate);

        await s.StatusAsync(message.Id, "failed");
        await s.StatusAsync(message.Id, "delivered"); // a late "delivered" after the release must not charge

        Assert.Equal(ChargeStatus.Released, (await s.UsageForAsync(message.Id))!.ChargeStatus);
        var account = await s.H.AccountAsync(s.Client);
        Assert.Equal(1m, account.WalletBalance);
        Assert.Equal(0m, account.ReservedAmount);
        await s.H.AssertReconciledAsync(s.Client);
    }

    [PostgresFact]
    public async Task AProviderError_ReleasesTheReservation_AndNoMessageIsSaved()
    {
        var s = await SetupAsync(wallet: 1m);
        s.WhatsApp.Fail = true;

        await Assert.ThrowsAsync<WhatsAppSendException>(() => s.SendTemplateAsync(s.MarketingTemplate));

        await using var db = s.H.Db();
        var usage = await db.BillingUsageRecords.AsNoTracking().SingleAsync(u => u.ClientId == s.Client && u.ChargeStatus != ChargeStatus.Failed);
        Assert.Equal(ChargeStatus.Released, usage.ChargeStatus);
        Assert.Equal("send_failed", usage.ReleaseReason);
        Assert.False(await db.Messages.AnyAsync(m => m.ClientId == s.Client));
        Assert.Equal(0m, (await s.H.AccountAsync(s.Client)).ReservedAmount);
        await s.H.AssertReconciledAsync(s.Client);
    }

    [PostgresFact]
    public async Task NotEnoughBalance_BlocksTheSendBeforeTheProviderIsCalled()
    {
        var s = await SetupAsync(wallet: 0.05m);

        var denied = await Assert.ThrowsAsync<BillingDeniedException>(() => s.SendTemplateAsync(s.MarketingTemplate));

        Assert.Equal(UsageFailureReason.InsufficientFunds, denied.Reason);
        Assert.Equal(0, s.WhatsApp.Sends);
        await s.H.AssertReconciledAsync(s.Client);
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
            text = (await s.NewMessageService(db).SendAsync(s.Client, s.Conversation, new SendMessageRequest("Thanks, see you Monday")))!;
        }

        Assert.Null(await s.UsageForAsync(utilityInWindow.Id));
        Assert.Null(await s.UsageForAsync(text.Id));
        Assert.Equal(2, s.WhatsApp.Sends);
        Assert.Equal(1m, (await s.H.AccountAsync(s.Client)).Spendable);
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
        var account = await s.H.AccountAsync(s.Client);
        Assert.Equal(0.93m, account.WalletBalance);
        Assert.Equal(0m, account.ReservedAmount);
        await s.H.AssertReconciledAsync(s.Client);
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
        await s.H.Billing.AdjustAsync(new WalletAdjustment(s.Client, 25m, LedgerBalanceType.IncludedCredit, "plan credit", Guid.NewGuid().ToString("N"), BillingSource.Admin, "tests"));

        var message = await s.SendTemplateAsync(s.MarketingTemplate);
        var usage = (await s.UsageForAsync(message.Id))!;
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(ProviderBillingResponsibility.CustomerDirect, usage.ProviderBilling);
        Assert.Equal(ProviderOutcome.Pending, usage.ProviderOutcome);
        Assert.Equal(0m, usage.Amount);
        Assert.Equal(0.055m, usage.ProviderCost); // the provider cost is still known, for reporting — the client pays it to Meta
        Assert.NotNull(usage.ChannelIntegrationId);
        Assert.Equal(0m, (await s.H.AccountAsync(s.Client)).ReservedAmount);

        await s.StatusAsync(message.Id, "delivered");
        await s.StatusAsync(message.Id, "delivered"); // duplicate callback

        usage = (await s.UsageForAsync(message.Id))!;
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(ProviderOutcome.Billable, usage.ProviderOutcome);
        var account = await s.H.AccountAsync(s.Client);
        Assert.Equal(1m, account.WalletBalance);
        Assert.Equal(25m, account.IncludedCreditBalance); // included credit untouched
        Assert.Equal(0m, account.ReservedAmount);
        Assert.DoesNotContain(await s.H.LedgerAsync(s.Client),
            l => l.EntryType is LedgerEntryType.UsageDebit or LedgerEntryType.IncludedCreditConsumption);
        await s.H.AssertReconciledAsync(s.Client);
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
    public async Task TwoWhatsAppAccounts_OneCustomerDirect_OnePlatformFunded_BehaveDifferently()
    {
        var direct = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.CustomerDirect);
        var funded = await SetupAsync(wallet: 1m); // default arrangement: Omni pays the provider

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
        Assert.Equal(1m, (await direct.H.AccountAsync(direct.Client)).WalletBalance);
        Assert.Equal(0.93m, (await funded.H.AccountAsync(funded.Client)).WalletBalance);
        await direct.H.AssertReconciledAsync(direct.Client);
        await funded.H.AssertReconciledAsync(funded.Client);
    }

    [PostgresFact]
    public async Task AnExternalProviderAccount_IsRecorded_WithNoPlatformDeduction()
    {
        var s = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.ExternalProviderDirect);

        var message = await s.SendTemplateAsync(s.MarketingTemplate);
        await s.StatusAsync(message.Id, "failed");

        var usage = (await s.UsageForAsync(message.Id))!;
        Assert.Equal(ChargeStatus.NotCharged, usage.ChargeStatus);
        Assert.Equal(ProviderOutcome.NotBillable, usage.ProviderOutcome);
        Assert.Equal(1m, (await s.H.AccountAsync(s.Client)).Spendable);
        await s.H.AssertReconciledAsync(s.Client);
    }

    [PostgresFact]
    public async Task APlatformUsageFee_OnACustomerDirectAccount_UsesOnlyAFeeRate_NeverThePlatformRate()
    {
        // Usage billing turned on for a customer-paid WABA, but no fee rate exists yet: refused, never the $0.07 platform price.
        var noFee = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.CustomerDirect, omniUsageBilling: true);
        var refused = await Assert.ThrowsAsync<BillingDeniedException>(() => noFee.SendTemplateAsync(noFee.MarketingTemplate));
        Assert.Equal(UsageFailureReason.RateNotFound, refused.Reason);
        Assert.Equal(0, noFee.WhatsApp.Sends);

        // With a fee rate for customer-direct accounts: Omni charges its fee, independent of Meta's cost.
        var withFee = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.CustomerDirect, omniUsageBilling: true);
        var card = (await withFee.H.ProviderBilling.ListAccountsAsync(withFee.Client)).Single();
        Assert.True(card.OmniUsageBilling);
        var feeCard = (await withFee.H.RateCards.ListAsync()).Single(c => c.ClientId == withFee.Client).Code;
        await withFee.H.AddRateAsync(feeCard, BillableEventTypes.WhatsAppMarketingMessage, clientRate: 0.01m, providerCost: 0.055m,
            providerBilling: ProviderBillingResponsibility.CustomerDirect);

        var message = await withFee.SendTemplateAsync(withFee.MarketingTemplate);
        await withFee.StatusAsync(message.Id, "delivered");

        var usage = (await withFee.UsageForAsync(message.Id))!;
        Assert.Equal(ChargeStatus.Settled, usage.ChargeStatus);
        Assert.Equal(ProviderBillingResponsibility.CustomerDirect, usage.ProviderBilling);
        Assert.Equal(0.01m, usage.Amount);
        Assert.Equal(0.99m, (await withFee.H.AccountAsync(withFee.Client)).WalletBalance);
        await withFee.H.AssertReconciledAsync(withFee.Client);
    }

    [PostgresFact]
    public async Task ChangingAnAccountsArrangement_NeverChangesRecordedUsage()
    {
        var s = await SetupAsync(wallet: 1m);
        var before = await s.SendTemplateAsync(s.MarketingTemplate);
        await s.StatusAsync(before.Id, "delivered");

        var accountId = (await s.H.ProviderBilling.ListAccountsAsync(s.Client)).Single().ChannelIntegrationId;
        await s.H.ProviderBilling.SetAsync(accountId, new ChannelAccountBillingChange(ProviderBillingResponsibility.CustomerDirect, null,
            "client moved to its own WABA payment method", "tests"));
        var after = await s.SendTemplateAsync(s.MarketingTemplate);

        var old = (await s.UsageForAsync(before.Id))!;
        Assert.Equal(ProviderBillingResponsibility.PlatformFunded, old.ProviderBilling);
        Assert.Equal(ChargeStatus.Settled, old.ChargeStatus);
        Assert.Equal(0.07m, old.Amount);
        Assert.Equal(ChargeStatus.NotCharged, (await s.UsageForAsync(after.Id))!.ChargeStatus);
        await using (var db = s.H.Db())
        {
            Assert.True(await db.Events.AnyAsync(e => e.ClientId == s.Client && e.EventType == BillingEventTypes.ProviderBillingChanged));
        }
        await s.H.AssertReconciledAsync(s.Client);
    }

    [PostgresFact]
    public async Task TheClientBillingView_ShowsProviderDirectUsageApartFromPlatformCharges()
    {
        var s = await SetupAsync(wallet: 1m, providerBilling: ProviderBillingResponsibility.CustomerDirect);
        var message = await s.SendTemplateAsync(s.MarketingTemplate);
        await s.StatusAsync(message.Id, "delivered");

        var summary = await s.H.Queries.GetSummaryAsync(s.Client);

        Assert.Empty(summary.Usage); // nothing charged by Omni
        var direct = Assert.Single(summary.ProviderDirectUsage);
        Assert.Equal(1, direct.Count);
        Assert.Equal("Paid by you directly to Meta", direct.PaidBy);
        var account = Assert.Single(summary.ChannelAccounts);
        Assert.False(account.ChargedByPlatform);
    }

    [PostgresFact]
    public async Task A_stop_word_opts_the_contact_out_and_the_confirmation_is_a_billed_system_message()
    {
        var s = await SetupAsync(wallet: 1m, serviceWindowOpen: true);
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.OptOutEnabled).Returns(true);
        config.SetupGet(c => c.OptOutSendConfirmation).Returns(true);
        config.SetupGet(c => c.OptOutStopKeywords).Returns("stop");
        config.SetupGet(c => c.OptOutStartKeywords).Returns("start");
        config.SetupGet(c => c.LocalizationLanguages).Returns("en");
        var terminology = new Mock<ITerminology>();
        terminology.Setup(t => t.Defaults(It.IsAny<string>())).Returns(TextKit.DefaultTerms());
        terminology.Setup(t => t.ForBusinessTypeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TextKit.DefaultTerms());

        IngestMessageResult result;
        await using (var db = s.H.Db())
        {
            var optOut = new Omni.App.Business.Services.Contacts.OptOutService(config.Object, new ContactRepository(db), new ClientRepository(db),
                new Omni.App.Business.Managers.ReaderTextFactory(TextKit.Catalog, terminology.Object, config.Object), new EventLogger(new EventLogRepository(db)),
                new UnitOfWork(db), TimeProvider.System);
            result = await s.NewMessageService(db, optOut).IngestAsync(new IngestMessageRequest(s.Client, s.Conversation, s.Contact, IngestEventType.CustomerMessage,
                ConversationChannel.WhatsApp, "Stop!", "wamid.in-" + Guid.NewGuid().ToString("N"), null, null, null, "text", null, null, null));
        }

        Assert.False(result.AiEligible);
        await using var check = s.H.Db();
        var contact = await check.Contacts.SingleAsync(c => c.Id == s.Contact);
        Assert.False(contact.MarketingOptIn);
        Assert.NotNull(contact.OptedOutAt);
        var reply = await check.Messages.SingleAsync(m => m.ConversationId == s.Conversation && m.Direction == MessageDirection.Outbound);
        Assert.Equal((MessageSenderType.System, MessageOrigin.System), (reply.SenderType, reply.Origin));
        Assert.StartsWith("You're unsubscribed", reply.Content);
        Assert.Matches(@"""automatic""\s*:\s*true", reply.MetadataJson); // jsonb may add a space; inbox.js allows it
        Assert.Equal(1, s.WhatsApp.Sends);
        Assert.True(await check.Events.AnyAsync(e => e.ContactId == s.Contact && e.EventType == EventTypes.ContactOptedOut));
    }
}
