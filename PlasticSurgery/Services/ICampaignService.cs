using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// Orchestrates sending one approved WhatsAppTemplate to many leads. A Campaign never stores
/// message content itself — every actual send goes through IMessageService.SendCampaignTemplateAsync,
/// which writes a normal row to the existing messages table in the lead's own Conversation (see
/// IConversationService.GetOrCreateForLeadAsync). Sending is always processed in small bounded
/// batches (ProcessBatchAsync) rather than one giant synchronous loop over every recipient, so a
/// campaign with thousands of leads never blocks a single HTTP request — call it repeatedly
/// (dashboard "Send more" action, or an n8n schedule) until CampaignCompleted comes back true.
/// </summary>
public interface ICampaignService
{
    Task<IReadOnlyList<CampaignListRow>> ListAsync(Guid clinicId, CancellationToken ct = default);

    Task<CampaignDetailsResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    /// <summary>Creates a Campaign in Draft status plus one CampaignRecipient per (de-duplicated)
    /// lead — nothing is sent yet.</summary>
    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken ct = default);

    Task<CampaignResponse?> ScheduleAsync(Guid clinicId, Guid id, DateTimeOffset scheduledAt, CancellationToken ct = default);

    /// <summary>Transitions Draft/Scheduled → Running (queues every Pending recipient), then processes
    /// the first batch immediately. Calling it again on an already-Running campaign just processes
    /// another batch — safe to call repeatedly.</summary>
    Task<ProcessCampaignBatchResult?> SendAsync(Guid clinicId, Guid id, int batchSize = 20, CancellationToken ct = default);

    /// <summary>Processes up to batchSize Queued recipients of a Running campaign. Marks the campaign
    /// Completed once no recipients remain queued. Meant to be called repeatedly — by the dashboard,
    /// or an n8n schedule — until CampaignCompleted is true.</summary>
    Task<ProcessCampaignBatchResult?> ProcessBatchAsync(Guid clinicId, Guid id, int batchSize = 20, CancellationToken ct = default);

    /// <summary>Cancels a Draft/Scheduled/Running campaign. Recipients already sent are untouched;
    /// any still Pending/Queued are marked Failed with an explanatory ErrorMessage.</summary>
    Task<CampaignResponse?> CancelAsync(Guid clinicId, Guid id, CancellationToken ct = default);
}
