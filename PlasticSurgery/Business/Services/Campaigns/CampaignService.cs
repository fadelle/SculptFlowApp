using System.Text.Json;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Business.Contracts.Services.Campaigns;
using PlasticSurgery.Business.Contracts.Services.Inbox;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Campaigns;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Campaigns;
using PlasticSurgery.Entities.Responses.Campaigns;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Campaigns;
using PlasticSurgery.Persistence.Contracts.Inbox;
using PlasticSurgery.Persistence.Contracts.Leads;
using PlasticSurgery.Persistence.Contracts.WhatsApp;

namespace PlasticSurgery.Business.Services.Campaigns;

public class CampaignService : ICampaignService
{
    private readonly ICampaignRepository _campaigns;
    private readonly IWhatsAppTemplateRepository _templates;
    private readonly ILeadRepository _leads;
    private readonly IMessageRepository _messageRows;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly ICampaignAudienceService _audience;
    private readonly IEntitlementService _entitlements;

    public CampaignService(ICampaignRepository campaigns, IWhatsAppTemplateRepository templates, ILeadRepository leads,
        IMessageRepository messageRows, IUnitOfWork unitOfWork, IConversationService conversations, IMessageService messages, ICampaignAudienceService audience,
        IEntitlementService entitlements)
    {
        _campaigns = campaigns;
        _templates = templates;
        _leads = leads;
        _messageRows = messageRows;
        _unitOfWork = unitOfWork;
        _conversations = conversations;
        _messages = messages;
        _audience = audience;
        _entitlements = entitlements;
    }

    public async Task<IReadOnlyList<CampaignListRow>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var campaigns = await _campaigns.ListWithTemplateAsync(clinicId, ct);
        if (campaigns.Count == 0) return Array.Empty<CampaignListRow>();

        // Batched across every campaign (one CampaignRecipients query, one inbound-reply lookup) instead
        // of once per campaign, so this page's cost doesn't grow linearly with how many campaigns exist.
        var campaignIds = campaigns.Select(c => c.Id).ToList();
        var allRecipients = await _campaigns.ListRecipientsAsync(campaignIds, ct);
        var recipientsByCampaign = allRecipients.GroupBy(r => r.CampaignId).ToDictionary(g => g.Key, g => g.ToList());
        var inboundByConversation = await FetchInboundReplyLookupAsync(clinicId, allRecipients, ct);

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

    public async Task<CampaignDetailsResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetWithTemplateAsync(clinicId, id, ct);
        if (campaign is null) return null;

        var recipients = await _campaigns.ListRecipientsWithLeadAsync(id, ct);

        var stats = await ComputeStats(clinicId, recipients, ct);
        var recipientRows = recipients.Select(r => new CampaignRecipientRow(
            r.Id, r.LeadId, r.Lead?.FullName, r.PhoneNumber, r.Status,
            r.SkipReason, r.FailureCode, r.FailureReason,
            r.QueuedAt, r.SentAt, r.DeliveredAt, r.ReadAt, r.RepliedAt, r.BookedAt, r.FailedAt)).ToList();

        return new CampaignDetailsResponse(ToResponse(campaign), stats, recipientRows, campaign.WhatsAppTemplate?.Body);
    }

    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name is required.", nameof(request));
        }
        await _entitlements.EnsureFeatureAsync(request.ClinicId, EntitlementKeys.Campaigns, ct);

        var template = await _templates.GetAsync(request.ClinicId, request.WhatsAppTemplateId, ct);
        if (template is null)
        {
            throw new ArgumentException("Template not found for this clinic.", nameof(request));
        }

        var audienceType = string.IsNullOrWhiteSpace(request.AudienceType) ? CampaignAudienceType.Custom : request.AudienceType;
        if (!CampaignAudienceType.All.Contains(audienceType))
        {
            throw new ArgumentException($"Unknown audience type '{audienceType}'.", nameof(request));
        }

        CampaignAudienceFilters.Validate(request.AudienceFilters);

        var explicitLeadIds = request.LeadIds.Distinct().ToList();
        List<Lead> leads;
        if (explicitLeadIds.Count > 0)
        {
            // Manual selection — the caller already picked exactly who to include (the Campaign
            // page's "pick leads yourself" escape hatch, still audience_type = custom).
            leads = await _leads.ListByIdsAsync(request.ClinicId, explicitLeadIds, ct);
        }
        else
        {
            // No explicit list — resolve the audience server-side (all_eligible / reactivation /
            // a filter-built custom audience, all via the same ICampaignAudienceService) and
            // snapshot it now. For "custom" with no filters either, this correctly resolves to
            // "every contactable lead" (the mandatory-exclusion base with no further narrowing) —
            // the UI always requires picking a filter or a lead first, so that's not user-reachable.
            leads = await _audience.GetEligibleLeadsAsync(request.ClinicId, audienceType, request.AudienceFilters, ct);
        }

        if (leads.Count == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            Name = request.Name,
            CampaignType = string.IsNullOrWhiteSpace(request.CampaignType) ? CampaignType.Custom : request.CampaignType,
            Channel = CampaignChannel.WhatsApp,
            WhatsAppTemplateId = request.WhatsAppTemplateId,
            AudienceType = audienceType,
            AudienceFilters = request.AudienceFilters,
            Status = CampaignStatus.Draft,
            ScheduledAt = request.ScheduledAt,
            CreatedAt = now,
            UpdatedAt = now
        };
        _campaigns.Add(campaign);

        foreach (var lead in leads)
        {
            // Mandatory exclusions apply even to a manually hand-picked lead list — see
            // ICampaignAudienceService, the single place this rule is defined. The lead still gets a
            // recipient row (Skipped, with why) so the audience snapshot accounts for everyone matched,
            // per the "CampaignRecipients records who was actually selected" contract.
            var skipReason = _audience.GetSkipReasonIfNotContactable(lead);

            var variables = request.VariablesByLeadId is not null && request.VariablesByLeadId.TryGetValue(lead.Id, out var vars)
                ? JsonSerializer.Serialize(vars)
                : null;

            _campaigns.AddRecipient(new CampaignRecipient
            {
                Id = Guid.NewGuid(),
                ClinicId = request.ClinicId,
                CampaignId = campaign.Id,
                LeadId = lead.Id,
                PhoneNumber = lead.Phone ?? string.Empty,
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

    public async Task<CampaignResponse?> ScheduleAsync(Guid clinicId, Guid id, DateTimeOffset scheduledAt, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clinicId, id, ct);
        if (campaign is null) return null;
        if (campaign.Status != CampaignStatus.Draft)
        {
            throw new InvalidOperationException($"Only a draft campaign can be scheduled (this one is '{campaign.Status}').");
        }
        await _entitlements.EnsureFeatureAsync(clinicId, EntitlementKeys.Campaigns, ct);

        campaign.Status = CampaignStatus.Scheduled;
        campaign.ScheduledAt = scheduledAt;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToResponse(campaign);
    }

    public async Task<ProcessCampaignBatchResult?> SendAsync(Guid clinicId, Guid id, int batchSize = 20, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clinicId, id, ct);
        if (campaign is null) return null;
        await _entitlements.EnsureFeatureAsync(clinicId, EntitlementKeys.Campaigns, ct);

        if (campaign.Status is CampaignStatus.Draft or CampaignStatus.Scheduled)
        {
            var pending = await _campaigns.ListRecipientsInStatusAsync(id, new[] { CampaignRecipientStatus.Pending }, ct);

            var now = DateTimeOffset.UtcNow;
            foreach (var r in pending)
            {
                r.Status = CampaignRecipientStatus.Queued;
                r.QueuedAt = now;
                r.UpdatedAt = now;
            }

            campaign.Status = CampaignStatus.Running;
            campaign.StartedAt = now;
            campaign.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
        }
        else if (campaign.Status != CampaignStatus.Running)
        {
            throw new InvalidOperationException($"Campaign can't be sent from status '{campaign.Status}'.");
        }

        return await ProcessBatchAsync(clinicId, id, batchSize, ct);
    }

    public async Task<ProcessCampaignBatchResult?> ProcessBatchAsync(Guid clinicId, Guid id, int batchSize = 20, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clinicId, id, ct);
        if (campaign is null) return null;

        if (campaign.Status != CampaignStatus.Running)
        {
            var remaining = await _campaigns.CountRecipientsInStatusAsync(id, CampaignRecipientStatus.Queued, ct);
            return new ProcessCampaignBatchResult(0, 0, 0, remaining, campaign.Status == CampaignStatus.Completed);
        }
        await _entitlements.EnsureFeatureAsync(clinicId, EntitlementKeys.Campaigns, ct);

        var batch = await _campaigns.ListQueuedBatchAsync(id, batchSize, ct);

        var succeeded = 0;
        var failed = 0;
        foreach (var recipient in batch)
        {
            var ok = await ProcessRecipientAsync(clinicId, recipient, ct);
            if (ok) succeeded++; else failed++;
        }

        var remainingQueued = await _campaigns.CountRecipientsInStatusAsync(id, CampaignRecipientStatus.Queued, ct);

        var completed = remainingQueued == 0;
        if (completed)
        {
            campaign.Status = CampaignStatus.Completed;
            campaign.CompletedAt = DateTimeOffset.UtcNow;
            campaign.UpdatedAt = campaign.CompletedAt.Value;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return new ProcessCampaignBatchResult(batch.Count, succeeded, failed, remainingQueued, completed);
    }

    /// <summary>Sends one recipient's template and records the outcome. A single recipient's failure (bad phone
    /// number, WhatsApp API error, etc.) must not abort the batch — EXCEPT running out of prepaid balance or losing
    /// the subscription: then the recipient stays queued and the exception stops the batch, so the campaign
    /// continues where it left off after a top-up instead of burning the rest of the audience as failed.</summary>
    private async Task<bool> ProcessRecipientAsync(Guid clinicId, CampaignRecipient recipient, CancellationToken ct)
    {
        try
        {
            var campaign = await _campaigns.GetByIdAsync(recipient.CampaignId, ct)
                ?? throw new InvalidOperationException("Campaign not found.");
            if (campaign.WhatsAppTemplateId is null)
            {
                throw new InvalidOperationException("Campaign has no WhatsApp template to send.");
            }
            var conversation = await _conversations.GetOrCreateForLeadAsync(clinicId, recipient.LeadId, ConversationChannel.WhatsApp, ct);

            var variables = string.IsNullOrEmpty(recipient.VariablesJson)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(recipient.VariablesJson) ?? Array.Empty<string>();

            var message = await _messages.SendCampaignTemplateAsync(
                clinicId, conversation.Id, campaign.WhatsAppTemplateId.Value, variables,
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
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            recipient.Status = CampaignRecipientStatus.Failed;
            recipient.FailureReason = ex.Message;
            recipient.FailedAt = now;
            recipient.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            return false;
        }
    }

    public async Task<CampaignResponse?> CancelAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var campaign = await _campaigns.GetAsync(clinicId, id, ct);
        if (campaign is null) return null;
        if (campaign.Status is CampaignStatus.Completed or CampaignStatus.Cancelled)
        {
            throw new InvalidOperationException($"Campaign is already '{campaign.Status}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var outstanding = await _campaigns.ListRecipientsInStatusAsync(id,
            new[] { CampaignRecipientStatus.Pending, CampaignRecipientStatus.Queued }, ct);
        foreach (var r in outstanding)
        {
            r.Status = CampaignRecipientStatus.Failed;
            r.FailureReason = "Campaign cancelled.";
            r.FailedAt = now;
            r.UpdatedAt = now;
        }

        campaign.Status = CampaignStatus.Cancelled;
        campaign.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);

        return ToResponse(campaign);
    }

    /// <summary>A hand-picked lead list is stored as audience_type = custom with no filters (see Pages/Campaigns/Create).</summary>
    private static bool IsManualSelection(Campaign c) =>
        c.AudienceType == CampaignAudienceType.Custom && string.IsNullOrWhiteSpace(c.AudienceFilters);

    private async Task<CampaignStatsResponse> ComputeStats(Guid clinicId, List<CampaignRecipient> recipients, CancellationToken ct)
    {
        var inboundByConversation = await FetchInboundReplyLookupAsync(clinicId, recipients, ct);
        return ComputeStatsCore(recipients, inboundByConversation);
    }

    /// <summary>Every inbound WhatsApp-customer message's timestamp, grouped by conversation, for every
    /// conversation any of these recipients belong to — one query regardless of how many recipients/campaigns
    /// are passed in. Raw timestamps (not aggregated to a single "earliest") because each recipient's own
    /// SentAt is the threshold a reply has to beat, and different recipients in the same batch (e.g. the same
    /// lead contacted by two different campaigns) can have different SentAt values for the same conversation.</summary>
    private async Task<Dictionary<Guid, List<DateTimeOffset>>> FetchInboundReplyLookupAsync(
        Guid clinicId, List<CampaignRecipient> recipients, CancellationToken ct)
    {
        var conversationIds = recipients.Where(r => r.ConversationId.HasValue && r.SentAt.HasValue)
            .Select(r => r.ConversationId!.Value).Distinct().ToList();
        if (conversationIds.Count == 0) return new Dictionary<Guid, List<DateTimeOffset>>();

        var inboundMessages = await _messageRows.ListInboundCustomerMessageTimesAsync(clinicId, conversationIds, ct);

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
        // earliest-ever inbound message, which could predate this campaign entirely (a lead who'd messaged
        // the clinic before would otherwise never count as "replied" to a later campaign).
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
        c.Id, c.ClinicId, c.Name, c.CampaignType, c.Channel, c.WhatsAppTemplateId, templateName ?? c.WhatsAppTemplate?.Name,
        c.AudienceType, c.AudienceFilters, c.Status,
        c.ScheduledAt, c.StartedAt, c.CompletedAt, c.CreatedAt, c.UpdatedAt
    );
}
