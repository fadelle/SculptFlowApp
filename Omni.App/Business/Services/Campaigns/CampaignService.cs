using Omni.App.Common.Helpers;
using System.Text.Json;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Campaigns;
using Omni.App.Entities.Responses.Campaigns;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Campaigns;
using Omni.App.Persistence.Contracts.Inbox;
using Omni.App.Persistence.Contracts.Contacts;
using Omni.App.Persistence.Contracts.WhatsApp;
using Omni.App.Entities.Dtos.Ui;
using Omni.App.Entities.Dtos.Notifications;
using Microsoft.Extensions.Localization;

namespace Omni.App.Business.Services.Campaigns;

public class CampaignService : ICampaignService
{
    private readonly ICampaignRepository _campaigns;
    private readonly IWhatsAppTemplateRepository _templates;
    private readonly IContactRepository _contacts;
    private readonly IMessageRepository _messageRows;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly ICampaignAudienceService _audience;
    private readonly IEntitlementService _entitlements;
    private readonly IWhatsAppService _whatsApp;
    private readonly INotificationService _notifications;

    private readonly IStringLocalizer _text;

    public const string PausedTitleKey = "notifications.campaignPaused.title";
    public const string PausedMessageKey = "notifications.campaignPaused.message";
    public const string FinishedWithFailuresTitleKey = "notifications.campaignFailures.title";
    public const string FinishedWithFailuresMessageKey = "notifications.campaignFailures.message";
    public const string NothingToRetryKey = "campaigns.errors.nothingToRetry";
    public const string CantRetryKey = "campaigns.errors.cantRetry";

    /// <summary>The longest pause reason stored (campaigns.paused_reason).</summary>
    public const int PausedReasonMaxLength = 500;

    public CampaignService(ICampaignRepository campaigns, IWhatsAppTemplateRepository templates, IContactRepository contacts,
        IMessageRepository messageRows, IUnitOfWork unitOfWork, IConversationService conversations, IMessageService messages, ICampaignAudienceService audience,
        IEntitlementService entitlements, IWhatsAppService whatsApp, INotificationService notifications, IStringLocalizer text)
    {
        _whatsApp = whatsApp;
        _notifications = notifications;
        _text = text;
        _campaigns = campaigns;
        _templates = templates;
        _contacts = contacts;
        _messageRows = messageRows;
        _unitOfWork = unitOfWork;
        _conversations = conversations;
        _messages = messages;
        _audience = audience;
        _entitlements = entitlements;
    }

    /// <summary>Recipient statuses that mean the message went out.</summary>
    private static readonly string[] SentStatuses =
    {
        CampaignRecipientStatus.Sent, CampaignRecipientStatus.Delivered, CampaignRecipientStatus.Read,
        CampaignRecipientStatus.Replied, CampaignRecipientStatus.Booked
    };

    public async Task<IReadOnlyList<CampaignListRow>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        var campaigns = await _campaigns.ListWithTemplateAsync(clientId, ct);
        if (campaigns.Count == 0) return Array.Empty<CampaignListRow>();

        // Batched across every campaign (one CampaignRecipients query, one inbound-reply lookup) instead
        // of once per campaign, so this page's cost doesn't grow linearly with how many campaigns exist.
        var campaignIds = campaigns.Select(c => c.Id).ToList();
        var allRecipients = await _campaigns.ListRecipientsAsync(campaignIds, ct);
        var recipientsByCampaign = allRecipients.GroupBy(r => r.CampaignId).ToDictionary(g => g.Key, g => g.ToList());
        var inboundByConversation = await FetchInboundReplyLookupAsync(clientId, allRecipients, ct);

        var rows = new List<CampaignListRow>(campaigns.Count);
        foreach (var c in campaigns)
        {
            var recipients = recipientsByCampaign.TryGetValue(c.Id, out var list) ? list : new List<CampaignRecipient>();
            // Same numbers as the details page (including the best-effort "replied" signal).
            var stats = ComputeStatsCore(recipients, inboundByConversation);
            rows.Add(new CampaignListRow(
                c.Id, c.Name, c.WhatsAppTemplate?.Name, c.Status,
                stats.TotalRecipients, stats.Sent, stats.Delivered, stats.Failed,
                c.ScheduledAt, c.CreatedAt,
                stats.Read, stats.Replied,
                c.AudienceType, IsManualSelection(c)));
        }
        return rows;
    }

    public async Task<CampaignDetailsResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetWithTemplateAsync(clientId, id, ct);
        if (campaign is null) return null;

        var recipients = await _campaigns.ListRecipientsWithContactAsync(id, ct);

        var stats = await ComputeStats(clientId, recipients, ct);
        var recipientRows = recipients.Select(r => new CampaignRecipientRow(
            r.Id, r.ContactId, r.Contact?.FullName, r.PhoneNumber, r.Status,
            r.SkipReason, r.FailureCode, StoredText.Show(r.FailureReason, _text), // in the reader's language (QA t5-ops-12)
            r.QueuedAt, r.SentAt, r.DeliveredAt, r.ReadAt, r.RepliedAt, r.BookedAt, r.FailedAt)).ToList();

        return new CampaignDetailsResponse(ToResponse(campaign), stats, recipientRows, campaign.WhatsAppTemplate?.Body);
    }

    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("campaigns.errors.nameRequired");
        }
        // marketing.campaigns.name is varchar(200) and campaign_type varchar(30): too long is a 400, never a 500 (QA t5-ops-08).
        var name = request.Name.Trim();
        if (name.Length > NameMax) throw new ArgumentException("campaigns.errors.nameTooLong").WithArgs(NameMax);
        var campaignType = string.IsNullOrWhiteSpace(request.CampaignType) ? CampaignType.Custom : request.CampaignType.Trim();
        if (campaignType.Length > CampaignTypeMax) throw new ArgumentException("campaigns.errors.typeTooLong").WithArgs(CampaignTypeMax);
        // A draft may carry the time it is meant for, but never a past one (QA t5-ops-10: a past date was stored).
        if (request.ScheduledAt is { } planned) EnsureFutureTime(planned);
        await _entitlements.EnsureFeatureAsync(request.ClientId, EntitlementKeys.Campaigns, ct);

        var template = await _templates.GetReadOnlyAsync(request.ClientId, request.WhatsAppTemplateId, ct);
        if (template is null)
        {
            throw new ArgumentException("templates.errors.notFound");
        }
        // Only an approved template can be sent (QA t8-11: campaigns were created with a rejected or draft one, then failed everyone).
        if (template.Status != WhatsAppTemplateStatus.Approved)
        {
            throw new ArgumentException("templates.errors.notApproved").WithArgs(template.Name, TextArg.Value(template.Status));
        }

        var audienceType = string.IsNullOrWhiteSpace(request.AudienceType) ? CampaignAudienceType.Custom : request.AudienceType;
        if (!CampaignAudienceType.All.Contains(audienceType))
        {
            throw new ArgumentException("campaigns.errors.unknownAudience").WithArgs(audienceType);
        }

        CampaignAudienceFilters.Validate(request.AudienceFilters);

        var explicitContactIds = request.ContactIds.Distinct().ToList();
        List<Contact> contacts;
        if (explicitContactIds.Count > 0)
        {
            // Manual selection — the caller already picked exactly who to include (the Campaign
            // page's "pick contacts yourself" escape hatch, still audience_type = custom).
            contacts = await _contacts.ListByIdsAsync(request.ClientId, explicitContactIds, ct);
        }
        else
        {
            // No explicit list — resolve the audience server-side (all_eligible / reactivation /
            // a filter-built custom audience, all via the same ICampaignAudienceService) and
            // snapshot it now. For "custom" with no filters either, this correctly resolves to
            // "every reachable contact" (the mandatory-exclusion base with no further narrowing) —
            // the UI always requires picking a filter or a contact first, so that's not user-reachable.
            contacts = await _audience.GetEligibleContactsAsync(request.ClientId, audienceType, request.AudienceFilters, ct);
        }

        if (contacts.Count == 0)
        {
            throw new ArgumentException("campaigns.errors.recipientRequired");
        }

        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            Name = name,
            CampaignType = campaignType,
            Channel = CampaignChannel.WhatsApp,
            WhatsAppTemplateId = request.WhatsAppTemplateId,
            AudienceType = audienceType,
            AudienceFilters = request.AudienceFilters,
            Status = CampaignStatus.Draft,
            ScheduledAt = request.ScheduledAt?.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now
        };
        _campaigns.Add(campaign);

        foreach (var contact in contacts)
        {
            // Mandatory exclusions apply even to a manually hand-picked contact list — see
            // ICampaignAudienceService, the single place this rule is defined. The contact still gets a
            // recipient row (Skipped, with why) so the audience snapshot accounts for everyone matched,
            // per the "CampaignRecipients records who was actually selected" contract.
            var skipReason = _audience.GetSkipReasonIfNotContactable(contact);

            var variables = request.VariablesByContactId is not null && request.VariablesByContactId.TryGetValue(contact.Id, out var vars)
                ? JsonSerializer.Serialize(vars)
                : null;

            _campaigns.AddRecipient(new CampaignRecipient
            {
                Id = Guid.NewGuid(),
                ClientId = request.ClientId,
                CampaignId = campaign.Id,
                ContactId = contact.Id,
                PhoneNumber = contact.Phone ?? string.Empty,
                VariablesJson = variables,
                Status = skipReason is null ? CampaignRecipientStatus.Pending : CampaignRecipientStatus.Skipped,
                SkipReason = skipReason,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return ToResponse(campaign, template.Name);
    }

    public async Task<CampaignResponse?> ScheduleAsync(Guid clientId, Guid id, DateTimeOffset scheduledAt, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clientId, id, ct);
        if (campaign is null) return null;
        if (campaign.Status != CampaignStatus.Draft)
        {
            throw new InvalidOperationException("campaigns.errors.scheduleNotDraft").WithArgs(TextArg.Value(campaign.Status));
        }
        // A time in the past would make the worker start it on its next tick (QA t5-ops-10); "Send now" is the way for that.
        EnsureFutureTime(scheduledAt);
        await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.Campaigns, ct);
        if (await StartProblemAsync(clientId, campaign, ct) is { } problem) throw problem;

        campaign.Status = CampaignStatus.Scheduled;
        campaign.ScheduledAt = scheduledAt.ToUniversalTime();
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToResponse(campaign);
    }

    /// <summary>How far ahead a scheduled time must be: at least a minute, so it can't already have passed when the worker looks.</summary>
    public static readonly TimeSpan MinScheduleNotice = TimeSpan.FromMinutes(1);

    /// <summary>400 for a time less than <see cref="MinScheduleNotice"/> from now.</summary>
    public static void EnsureFutureTime(DateTimeOffset scheduledAt)
    {
        if (scheduledAt < DateTimeOffset.UtcNow + MinScheduleNotice) throw new ArgumentException("campaigns.create.errors.schedulePast");
    }

    public async Task<ProcessCampaignBatchResult?> SendAsync(Guid clientId, Guid id, int batchSize = 20, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clientId, id, ct);
        if (campaign is null) return null;
        await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.Campaigns, ct);

        if (campaign.Status is not (CampaignStatus.Draft or CampaignStatus.Scheduled or CampaignStatus.Paused or CampaignStatus.Running))
        {
            throw new InvalidOperationException("campaigns.errors.cantSend").WithArgs(TextArg.Value(campaign.Status));
        }
        if (await StartProblemAsync(clientId, campaign, ct) is { } problem) throw problem;
        if (campaign.Status != CampaignStatus.Running)
        {
            await StartAsync(campaign, ct);
        }

        return await ProcessBatchAsync(clientId, id, batchSize, ct);
    }

    /// <summary>Why the campaign can't send right now, or null: WhatsApp isn't connected (every recipient would fail), or
    /// its template is gone or not approved. Checked when it is scheduled, started, resumed and before every batch.</summary>
    private async Task<InvalidOperationException?> StartProblemAsync(Guid clientId, Campaign campaign, CancellationToken ct)
    {
        if (!await _whatsApp.IsReadyAsync(clientId, ct))
        {
            return new InvalidOperationException("campaigns.errors.whatsAppNotConnected");
        }
        var template = campaign.WhatsAppTemplateId is { } templateId ? await _templates.GetReadOnlyAsync(clientId, templateId, ct) : null;
        if (template is null)
        {
            return new InvalidOperationException("campaigns.errors.noTemplate");
        }
        if (template.Status != WhatsAppTemplateStatus.Approved)
        {
            return new InvalidOperationException("templates.errors.notApproved").WithArgs(template.Name, TextArg.Value(template.Status));
        }
        return null;
    }

    /// <summary>Draft/Scheduled/Paused -> Running: queues every recipient still Pending (a stopped campaign that never
    /// started still has them) and keeps the first StartedAt when a stopped campaign is resumed.</summary>
    private async Task StartAsync(Campaign campaign, CancellationToken ct)
    {
        var pending = await _campaigns.ListRecipientsInStatusAsync(campaign.Id, new[] { CampaignRecipientStatus.Pending }, ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var r in pending)
        {
            r.Status = CampaignRecipientStatus.Queued;
            r.QueuedAt = now;
            r.UpdatedAt = now;
        }

        campaign.Status = CampaignStatus.Running;
        campaign.StartedAt ??= now;
        campaign.PausedReason = null;
        campaign.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Stops a campaign that can't go on by itself (Running or Scheduled -> Paused), records why for the campaign page and
    /// tells the members once. Nothing happens when it was already stopped, cancelled or finished meanwhile.</summary>
    private async Task PauseByItselfAsync(Campaign campaign, string fromStatus, Exception problem, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await _campaigns.TryChangeStatusAsync(campaign.ClientId, campaign.Id, new[] { fromStatus }, CampaignStatus.Paused, now, ct)) return;

        // Stored as worded now, like a recipient's failure reason (outside a request: the default language).
        var reason = _text.Localize(problem);
        if (reason.Length > PausedReasonMaxLength) reason = reason[..PausedReasonMaxLength];
        campaign.PausedReason = reason;
        campaign.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
        campaign.Status = CampaignStatus.Paused;

        await _notifications.CreateAsync(campaign.ClientId, NotificationType.OutboundMessageFailed,
            NotificationText.Of(PausedTitleKey, PausedMessageKey, campaign.Name, reason),
            link: $"/campaigns/{campaign.Id}", ct: ct);
    }

    /// <summary>Sends the failed recipients again (QA t8-11: a failed recipient could never be resent): they are queued again, the
    /// campaign runs, and the first batch goes out now. For a campaign that finished, failed, is stopped or is still sending; 422 when
    /// none failed or the campaign was never started or was cancelled.</summary>
    public async Task<ProcessCampaignBatchResult?> RetryFailedAsync(Guid clientId, Guid id, int batchSize = 20, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clientId, id, ct);
        if (campaign is null) return null;
        if (campaign.Status is not (CampaignStatus.Completed or CampaignStatus.Failed or CampaignStatus.Paused or CampaignStatus.Running))
        {
            throw new InvalidOperationException(CantRetryKey).WithArgs(TextArg.Value(campaign.Status));
        }
        await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.Campaigns, ct);

        var failed = await _campaigns.ListRecipientsInStatusAsync(id, new[] { CampaignRecipientStatus.Failed }, ct);
        if (failed.Count == 0)
        {
            throw new InvalidOperationException(NothingToRetryKey);
        }
        if (await StartProblemAsync(clientId, campaign, ct) is { } problem) throw problem;

        var now = DateTimeOffset.UtcNow;
        foreach (var r in failed)
        {
            r.Status = CampaignRecipientStatus.Queued;
            r.QueuedAt = now;
            r.FailedAt = null;
            r.FailureCode = null;
            r.FailureReason = null;
            r.MessageId = null;
            r.ExternalMessageId = null;
            r.SentAt = null;
            r.DeliveredAt = null;
            r.ReadAt = null;
            r.UpdatedAt = now;
        }
        campaign.CompletedAt = null;
        await StartAsync(campaign, ct);

        return await ProcessBatchAsync(clientId, id, batchSize, ct);
    }

    public async Task<ProcessCampaignBatchResult?> ProcessBatchAsync(Guid clientId, Guid id, int batchSize = 20, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clientId, id, ct);
        if (campaign is null) return null;

        if (campaign.Status != CampaignStatus.Running)
        {
            var remaining = await _campaigns.CountRecipientsInStatusAsync(id, CampaignRecipientStatus.Queued, ct);
            return new ProcessCampaignBatchResult(0, 0, 0, remaining, campaign.Status == CampaignStatus.Completed);
        }
        await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.Campaigns, ct);

        // WhatsApp disconnected (or the template withdrawn) while it ran: stop it, so Send resumes it once that's fixed,
        // instead of failing everyone still queued.
        if (await StartProblemAsync(clientId, campaign, ct) is { } startProblem)
        {
            await PauseByItselfAsync(campaign, CampaignStatus.Running, startProblem, ct);
            var remaining = await _campaigns.CountRecipientsInStatusAsync(id, CampaignRecipientStatus.Queued, ct);
            return new ProcessCampaignBatchResult(0, 0, 0, remaining, false);
        }

        // One sender per campaign at a time (the Send button and the background sender, or two app instances), so
        // nobody is messaged twice. If another sender has it, it is already sending: report progress and step aside.
        await using var sendingLock = await _campaigns.TryLockForSendingAsync(id, ct);
        if (sendingLock is null)
        {
            var remaining = await _campaigns.CountRecipientsInStatusAsync(id, CampaignRecipientStatus.Queued, ct);
            return new ProcessCampaignBatchResult(0, 0, 0, remaining, false);
        }

        var batch = await _campaigns.ListQueuedBatchAsync(id, batchSize, ct);

        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var stillRunning = true;
        foreach (var recipient in batch)
        {
            // Read fresh before every message, so Stop or Cancel pressed mid-batch halts sending at once.
            if (await _campaigns.GetStatusReadOnlyAsync(id, ct) != CampaignStatus.Running)
            {
                stillRunning = false;
                break;
            }

            bool ok;
            try
            {
                ok = await ProcessRecipientAsync(clientId, recipient, ct);
            }
            catch (CampaignHalt halt)
            {
                // Not about this recipient: it stays queued, and so does everyone after it.
                if (halt.Pause) await PauseByItselfAsync(campaign, CampaignStatus.Running, halt.Problem, ct);
                stillRunning = false;
                break;
            }
            processed++;
            if (ok) succeeded++; else failed++;
        }

        var remainingQueued = await _campaigns.CountRecipientsInStatusAsync(id, CampaignRecipientStatus.Queued, ct);

        // Only a campaign that is still running completes: a stopped or cancelled one keeps the status members gave it.
        var completed = remainingQueued == 0 && stillRunning
                        && await _campaigns.GetStatusReadOnlyAsync(id, ct) == CampaignStatus.Running;
        if (completed)
        {
            // Sends were tried and none went out: the campaign failed, it didn't complete.
            var nothingSent = await _campaigns.CountRecipientsInStatusAsync(id, CampaignRecipientStatus.Failed, ct) > 0
                              && await _campaigns.CountRecipientsInStatusesAsync(id, SentStatuses, ct) == 0;
            campaign.Status = nothingSent ? CampaignStatus.Failed : CampaignStatus.Completed;
            campaign.CompletedAt = DateTimeOffset.UtcNow;
            campaign.UpdatedAt = campaign.CompletedAt.Value;
            await _unitOfWork.SaveChangesAsync(ct);

            // One notification for the campaign's failures, not one per recipient (QA t8-11).
            var failedCount = await _campaigns.CountRecipientsInStatusAsync(id, CampaignRecipientStatus.Failed, ct);
            if (failedCount > 0)
            {
                await _notifications.CreateAsync(clientId, NotificationType.OutboundMessageFailed,
                    NotificationText.Of(FinishedWithFailuresTitleKey, FinishedWithFailuresMessageKey, campaign.Name, failedCount),
                    link: $"/campaigns/{id}", ct: ct);
            }
        }

        return new ProcessCampaignBatchResult(processed, succeeded, failed, remainingQueued, completed);
    }

    public Task<IReadOnlyList<DueCampaign>> ListDueAsync(CancellationToken ct = default) =>
        _campaigns.ListDueForSendingReadOnlyAsync(DateTimeOffset.UtcNow, ct);

    public async Task<ProcessCampaignBatchResult?> SendDueAsync(Guid clientId, Guid id, int batchSize, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clientId, id, ct);
        if (campaign is null) return null;

        if (campaign.Status == CampaignStatus.Scheduled)
        {
            // Re-checked here (the list can be a minute old): never start one before its time, or one members stopped.
            if (campaign.ScheduledAt is null || campaign.ScheduledAt > DateTimeOffset.UtcNow) return null;
            await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.Campaigns, ct);
            // Due but WhatsApp isn't connected (or the template isn't approved): stop it rather than fail every recipient;
            // the campaign page says why, and Send resumes it once that's fixed.
            if (await StartProblemAsync(clientId, campaign, ct) is { } problem)
            {
                await PauseByItselfAsync(campaign, CampaignStatus.Scheduled, problem, ct);
                return null;
            }
            await StartAsync(campaign, ct);
        }
        else if (campaign.Status != CampaignStatus.Running)
        {
            return null; // stopped, cancelled or finished since it was listed
        }

        return await ProcessBatchAsync(clientId, id, batchSize, ct);
    }

    public async Task<CampaignResponse?> StopAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clientId, id, ct);
        if (campaign is null) return null;
        if (campaign.Status is not (CampaignStatus.Scheduled or CampaignStatus.Running))
        {
            throw new InvalidOperationException("campaigns.errors.cantStop").WithArgs(TextArg.Value(campaign.Status));
        }

        // Set-based and conditional: the sender may be finishing it right now, and a finished campaign must stay finished.
        var now = DateTimeOffset.UtcNow;
        var stopped = await _campaigns.TryChangeStatusAsync(clientId, id,
            new[] { CampaignStatus.Scheduled, CampaignStatus.Running }, CampaignStatus.Paused, now, ct);
        if (!stopped)
        {
            throw new InvalidOperationException("campaigns.errors.changedBeforeStop");
        }

        campaign.Status = CampaignStatus.Paused;
        campaign.UpdatedAt = now;
        if (campaign.PausedReason is not null)
        {
            campaign.PausedReason = null;
            await _unitOfWork.SaveChangesAsync(ct);
        }
        return ToResponse(campaign);
    }

    /// <summary>Sends one recipient's template and records the outcome. A single recipient's failure (bad phone
    /// number, WhatsApp API error, etc.) must not abort the batch — EXCEPT running out of prepaid balance or losing
    /// the subscription: then the recipient stays queued and the exception stops the batch, so the campaign
    /// continues where it left off after a top-up instead of burning the rest of the audience as failed. Likewise a
    /// problem that would fail everyone (QA t8-11: WhatsApp's token, account or template, Meta down) leaves the recipient
    /// queued and pauses the campaign with the reason (<see cref="CampaignHalt"/>), and Meta's rate limit just ends the
    /// batch so the next run goes on.</summary>
    private async Task<bool> ProcessRecipientAsync(Guid clientId, CampaignRecipient recipient, CancellationToken ct)
    {
        // Each recipient's send is its own story in the message trace (not one trace for the whole batch).
        using var trace = TraceContext.StartNew("campaign.send");
        try
        {
            var campaign = await _campaigns.GetByIdAsync(recipient.CampaignId, ct)
                ?? throw new InvalidOperationException("campaigns.errors.notFound");
            if (campaign.WhatsAppTemplateId is null)
            {
                throw new InvalidOperationException("campaigns.errors.noTemplate");
            }
            var conversation = await _conversations.GetOrCreateForContactAsync(clientId, recipient.ContactId, ConversationChannel.WhatsApp, ct);

            var variables = string.IsNullOrEmpty(recipient.VariablesJson)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(recipient.VariablesJson) ?? Array.Empty<string>();

            var message = await _messages.SendCampaignTemplateAsync(
                clientId, conversation.Id, campaign.WhatsAppTemplateId.Value, variables,
                campaign.Id, recipient.Id, ct);

            var now = DateTimeOffset.UtcNow;
            recipient.ConversationId = conversation.Id;
            recipient.MessageId = message?.Id;
            recipient.ExternalMessageId = message is null ? null
                : await _messageRows.GetExternalIdAsync(message.Id, ct);
            recipient.Status = CampaignRecipientStatus.Sent;
            recipient.SentAt = now;
            recipient.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is BillingDeniedException { Reason: UsageFailureReason.InsufficientFunds } or EntitlementDeniedException)
        {
            throw;
        }
        catch (WhatsAppSendException ex) when (ex.StopsEveryone || ex.IsRateLimited)
        {
            throw new CampaignHalt(ex, pause: ex.StopsEveryone);
        }
        catch (InvalidOperationException ex) when (ex.Message == NotApprovedKey)
        {
            // The template was paused or withdrawn while it ran.
            throw new CampaignHalt(ex, pause: true);
        }
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            recipient.Status = CampaignRecipientStatus.Failed;
            // Stored text (the campaign page shows it as written): a keyed refusal is worded now, outside a request in English.
            // Stored as a key where it is one of ours, read in each viewer's language (QA t5-ops-12).
            recipient.FailureReason = StoredText.Of(ex, _text);
            recipient.FailedAt = now;
            recipient.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            return false;
        }
    }

    public async Task<CampaignResponse?> CancelAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clientId, id, ct);
        if (campaign is null) return null;
        if (campaign.Status is CampaignStatus.Completed or CampaignStatus.Cancelled)
        {
            throw new InvalidOperationException("campaigns.errors.alreadyInStatus").WithArgs(TextArg.Value(campaign.Status));
        }

        var now = DateTimeOffset.UtcNow;
        var outstanding = await _campaigns.ListRecipientsInStatusAsync(id,
            new[] { CampaignRecipientStatus.Pending, CampaignRecipientStatus.Queued }, ct);
        // Never sent, so not failed: skipped, with a reason the page translates (QA t5-ops-11: a cancelled draft showed
        // "Failed 5" with the English "Campaign cancelled.").
        foreach (var r in outstanding)
        {
            r.Status = CampaignRecipientStatus.Skipped;
            r.SkipReason = CampaignSkipReason.CampaignCancelled;
            r.UpdatedAt = now;
        }

        campaign.Status = CampaignStatus.Cancelled;
        campaign.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToResponse(campaign);
    }

    private const string NotApprovedKey = "templates.errors.notApproved";

    /// <summary>The longest campaign name and type the table holds.</summary>
    public const int NameMax = 200, CampaignTypeMax = 30;

    /// <summary>A send problem that isn't the recipient's: the batch stops with the recipient still queued, and the campaign is
    /// paused when Pause (otherwise it is only Meta's rate limit, and the next run goes on).</summary>
    private sealed class CampaignHalt : Exception
    {
        public CampaignHalt(Exception problem, bool pause) : base(problem.Message, problem)
        {
            Problem = problem;
            Pause = pause;
        }

        public Exception Problem { get; }
        public bool Pause { get; }
    }

    /// <summary>A hand-picked contact list is stored as audience_type = custom with no filters (see Pages/Campaigns/Create).</summary>
    private static bool IsManualSelection(Campaign c) =>
        c.AudienceType == CampaignAudienceType.Custom && string.IsNullOrWhiteSpace(c.AudienceFilters);

    private async Task<CampaignStatsResponse> ComputeStats(Guid clientId, List<CampaignRecipient> recipients, CancellationToken ct)
    {
        var inboundByConversation = await FetchInboundReplyLookupAsync(clientId, recipients, ct);
        return ComputeStatsCore(recipients, inboundByConversation);
    }

    /// <summary>Every inbound WhatsApp-customer message's timestamp, grouped by conversation, for every
    /// conversation any of these recipients belong to — one query regardless of how many recipients/campaigns
    /// are passed in. Raw timestamps (not aggregated to a single "earliest") because each recipient's own
    /// SentAt is the threshold a reply has to beat, and different recipients in the same batch (e.g. the same
    /// contact messaged by two different campaigns) can have different SentAt values for the same conversation.</summary>
    private async Task<Dictionary<Guid, List<DateTimeOffset>>> FetchInboundReplyLookupAsync(
        Guid clientId, List<CampaignRecipient> recipients, CancellationToken ct)
    {
        var conversationIds = recipients.Where(r => r.ConversationId.HasValue && r.SentAt.HasValue)
            .Select(r => r.ConversationId!.Value).Distinct().ToList();
        if (conversationIds.Count == 0) return new Dictionary<Guid, List<DateTimeOffset>>();

        var inboundMessages = await _messageRows.ListInboundCustomerMessageTimesAsync(clientId, conversationIds, ct);

        return inboundMessages.GroupBy(m => m.ConversationId).ToDictionary(g => g.Key, g => g.Select(x => x.CreatedAt).ToList());
    }

    private static CampaignStatsResponse ComputeStatsCore(List<CampaignRecipient> recipients, IReadOnlyDictionary<Guid, List<DateTimeOffset>> inboundByConversation)
    {
        var total = recipients.Count;
        var pending = recipients.Count(r => r.Status == CampaignRecipientStatus.Pending);
        var queued = recipients.Count(r => r.Status == CampaignRecipientStatus.Queued);
        var sent = recipients.Count(r => r.Status is CampaignRecipientStatus.Sent or CampaignRecipientStatus.Delivered or CampaignRecipientStatus.Read);
        var delivered = recipients.Count(r => r.Status is CampaignRecipientStatus.Delivered or CampaignRecipientStatus.Read);
        var read = recipients.Count(r => r.Status == CampaignRecipientStatus.Read);
        var failed = recipients.Count(r => r.Status == CampaignRecipientStatus.Failed);
        var skipped = recipients.Count(r => r.Status == CampaignRecipientStatus.Skipped);
        var booked = recipients.Count(r => r.Status == CampaignRecipientStatus.Booked || r.BookedAt.HasValue);

        // Best-effort "replied" signal: a customer inbound message landed in the recipient's conversation
        // any time after THIS recipient's own campaign message was sent — not just the conversation's
        // earliest-ever inbound message, which could predate this campaign entirely (a contact who'd messaged
        // the client before would otherwise never count as "replied" to a later campaign).
        var replied = 0;
        foreach (var r in recipients.Where(r => r.ConversationId.HasValue && r.SentAt.HasValue))
        {
            if (inboundByConversation.TryGetValue(r.ConversationId!.Value, out var inboundTimes)
                && inboundTimes.Any(t => t > r.SentAt!.Value))
            {
                replied++;
            }
        }

        return new CampaignStatsResponse(total, pending, queued, sent, delivered, read, failed, replied, skipped, booked);
    }

    private static CampaignResponse ToResponse(Campaign c, string? templateName = null) => new(
        c.Id, c.ClientId, c.Name, c.CampaignType, c.Channel, c.WhatsAppTemplateId, templateName ?? c.WhatsAppTemplate?.Name,
        c.AudienceType, c.AudienceFilters, c.Status,
        c.ScheduledAt, c.StartedAt, c.CompletedAt, c.CreatedAt, c.UpdatedAt, c.PausedReason
    );
}
