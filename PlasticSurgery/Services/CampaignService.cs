using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class CampaignService : ICampaignService
{
    private readonly ApplicationDbContext _db;
    private readonly IConversationService _conversations;
    private readonly IMessageService _messages;
    private readonly ICampaignAudienceService _audience;

    public CampaignService(ApplicationDbContext db, IConversationService conversations, IMessageService messages, ICampaignAudienceService audience)
    {
        _db = db;
        _conversations = conversations;
        _messages = messages;
        _audience = audience;
    }

    public async Task<IReadOnlyList<CampaignListRow>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var campaigns = await _db.Campaigns
            .Include(c => c.WhatsAppTemplate)
            .Where(c => c.ClinicId == clinicId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var rows = new List<CampaignListRow>(campaigns.Count);
        foreach (var c in campaigns)
        {
            var recipients = await _db.CampaignRecipients.Where(r => r.CampaignId == c.Id).ToListAsync(ct);
            rows.Add(new CampaignListRow(
                c.Id, c.Name, c.WhatsAppTemplate?.Name, c.Status,
                recipients.Count,
                recipients.Count(r => r.Status == CampaignRecipientStatus.Sent || r.Status == CampaignRecipientStatus.Delivered || r.Status == CampaignRecipientStatus.Read),
                recipients.Count(r => r.Status == CampaignRecipientStatus.Delivered || r.Status == CampaignRecipientStatus.Read),
                recipients.Count(r => r.Status == CampaignRecipientStatus.Failed),
                c.ScheduledAt, c.CreatedAt));
        }
        return rows;
    }

    public async Task<CampaignDetailsResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns
            .Include(c => c.WhatsAppTemplate)
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == id, ct);
        if (campaign is null) return null;

        var recipients = await _db.CampaignRecipients
            .Include(r => r.Lead)
            .Where(r => r.CampaignId == id)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        var stats = await ComputeStats(clinicId, recipients, ct);
        var recipientRows = recipients.Select(r => new CampaignRecipientRow(
            r.Id, r.LeadId, r.Lead?.FullName, r.PhoneNumber, r.Status,
            r.SkipReason, r.FailureCode, r.FailureReason,
            r.QueuedAt, r.SentAt, r.DeliveredAt, r.ReadAt, r.RepliedAt, r.BookedAt, r.FailedAt)).ToList();

        return new CampaignDetailsResponse(ToResponse(campaign), stats, recipientRows);
    }

    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name is required.", nameof(request));
        }

        var template = await _db.WhatsAppTemplates.FirstOrDefaultAsync(
            t => t.ClinicId == request.ClinicId && t.Id == request.WhatsAppTemplateId, ct);
        if (template is null)
        {
            throw new ArgumentException("Template not found for this clinic.", nameof(request));
        }

        var audienceType = string.IsNullOrWhiteSpace(request.AudienceType) ? CampaignAudienceType.Custom : request.AudienceType;
        if (!CampaignAudienceType.All.Contains(audienceType))
        {
            throw new ArgumentException($"Unknown audience type '{audienceType}'.", nameof(request));
        }

        var explicitLeadIds = request.LeadIds.Distinct().ToList();
        List<Lead> leads;
        if (explicitLeadIds.Count > 0)
        {
            // Manual selection — the caller already picked exactly who to include (the Campaign
            // page's "pick leads yourself" escape hatch, still audience_type = custom).
            leads = await _db.Leads
                .Where(l => l.ClinicId == request.ClinicId && explicitLeadIds.Contains(l.Id))
                .ToListAsync(ct);
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
        _db.Campaigns.Add(campaign);

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

            _db.CampaignRecipients.Add(new CampaignRecipient
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

        await _db.SaveChangesAsync(ct);
        return ToResponse(campaign, template.Name);
    }

    public async Task<CampaignResponse?> ScheduleAsync(Guid clinicId, Guid id, DateTimeOffset scheduledAt, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == id, ct);
        if (campaign is null) return null;
        if (campaign.Status != CampaignStatus.Draft)
        {
            throw new InvalidOperationException($"Only a draft campaign can be scheduled (this one is '{campaign.Status}').");
        }

        campaign.Status = CampaignStatus.Scheduled;
        campaign.ScheduledAt = scheduledAt;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToResponse(campaign);
    }

    public async Task<ProcessCampaignBatchResult?> SendAsync(Guid clinicId, Guid id, int batchSize = 20, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == id, ct);
        if (campaign is null) return null;

        if (campaign.Status is CampaignStatus.Draft or CampaignStatus.Scheduled)
        {
            var pending = await _db.CampaignRecipients
                .Where(r => r.CampaignId == id && r.Status == CampaignRecipientStatus.Pending)
                .ToListAsync(ct);

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
            await _db.SaveChangesAsync(ct);
        }
        else if (campaign.Status != CampaignStatus.Running)
        {
            throw new InvalidOperationException($"Campaign can't be sent from status '{campaign.Status}'.");
        }

        return await ProcessBatchAsync(clinicId, id, batchSize, ct);
    }

    public async Task<ProcessCampaignBatchResult?> ProcessBatchAsync(Guid clinicId, Guid id, int batchSize = 20, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == id, ct);
        if (campaign is null) return null;

        if (campaign.Status != CampaignStatus.Running)
        {
            var remaining = await _db.CampaignRecipients.CountAsync(
                r => r.CampaignId == id && r.Status == CampaignRecipientStatus.Queued, ct);
            return new ProcessCampaignBatchResult(0, 0, 0, remaining, campaign.Status == CampaignStatus.Completed);
        }

        var batch = await _db.CampaignRecipients
            .Where(r => r.CampaignId == id && r.Status == CampaignRecipientStatus.Queued)
            .OrderBy(r => r.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        var succeeded = 0;
        var failed = 0;
        foreach (var recipient in batch)
        {
            var ok = await ProcessRecipientAsync(clinicId, recipient, ct);
            if (ok) succeeded++; else failed++;
        }

        var remainingQueued = await _db.CampaignRecipients.CountAsync(
            r => r.CampaignId == id && r.Status == CampaignRecipientStatus.Queued, ct);

        var completed = remainingQueued == 0;
        if (completed)
        {
            campaign.Status = CampaignStatus.Completed;
            campaign.CompletedAt = DateTimeOffset.UtcNow;
            campaign.UpdatedAt = campaign.CompletedAt.Value;
            await _db.SaveChangesAsync(ct);
        }

        return new ProcessCampaignBatchResult(batch.Count, succeeded, failed, remainingQueued, completed);
    }

    /// <summary>Sends one recipient's template and records the outcome. Never throws — a single
    /// recipient's failure (bad phone number, WhatsApp API error, etc.) must not abort the batch.</summary>
    private async Task<bool> ProcessRecipientAsync(Guid clinicId, CampaignRecipient recipient, CancellationToken ct)
    {
        try
        {
            var campaign = await _db.Campaigns.FirstAsync(c => c.Id == recipient.CampaignId, ct);
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
                : await _db.Messages.Where(m => m.Id == message.Id).Select(m => m.ExternalMessageId).FirstOrDefaultAsync(ct);
            recipient.Status = CampaignRecipientStatus.Sent;
            recipient.SentAt = now;
            recipient.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            recipient.Status = CampaignRecipientStatus.Failed;
            recipient.FailureReason = ex.Message;
            recipient.FailedAt = now;
            recipient.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return false;
        }
    }

    public async Task<CampaignResponse?> CancelAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.Id == id, ct);
        if (campaign is null) return null;
        if (campaign.Status is CampaignStatus.Completed or CampaignStatus.Cancelled)
        {
            throw new InvalidOperationException($"Campaign is already '{campaign.Status}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var outstanding = await _db.CampaignRecipients
            .Where(r => r.CampaignId == id && (r.Status == CampaignRecipientStatus.Pending || r.Status == CampaignRecipientStatus.Queued))
            .ToListAsync(ct);
        foreach (var r in outstanding)
        {
            r.Status = CampaignRecipientStatus.Failed;
            r.FailureReason = "Campaign cancelled.";
            r.FailedAt = now;
            r.UpdatedAt = now;
        }

        campaign.Status = CampaignStatus.Cancelled;
        campaign.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        return ToResponse(campaign);
    }

    private async Task<CampaignStatsResponse> ComputeStats(Guid clinicId, List<CampaignRecipient> recipients, CancellationToken ct)
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

        // Best-effort "replied" signal: a customer inbound message landed in the recipient's
        // conversation any time after this campaign message was sent.
        var replied = 0;
        var sentRecipients = recipients.Where(r => r.ConversationId.HasValue && r.SentAt.HasValue).ToList();
        if (sentRecipients.Count > 0)
        {
            var conversationIds = sentRecipients.Select(r => r.ConversationId!.Value).Distinct().ToList();
            var inboundByConversation = await _db.Messages
                .Where(m => m.ClinicId == clinicId && conversationIds.Contains(m.ConversationId)
                    && m.Direction == MessageDirection.Inbound && m.Origin == MessageOrigin.WhatsAppCustomer)
                .GroupBy(m => m.ConversationId)
                .Select(g => new { ConversationId = g.Key, EarliestInboundAt = g.Min(m => m.CreatedAt) })
                .ToListAsync(ct);

            var earliestByConversation = inboundByConversation.ToDictionary(x => x.ConversationId, x => x.EarliestInboundAt);
            foreach (var r in sentRecipients)
            {
                if (earliestByConversation.TryGetValue(r.ConversationId!.Value, out var earliestInboundAt) && earliestInboundAt > r.SentAt!.Value)
                {
                    replied++;
                }
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
