using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Requests.Campaigns;
using Omni.App.Entities.Responses.Campaigns;

namespace Omni.App.Business.Contracts.Services.Campaigns;

/// <summary>
/// Orchestrates sending one approved WhatsAppTemplate to many contacts. A Campaign never stores
/// message content itself — every actual send goes through IMessageService.SendCampaignTemplateAsync,
/// which writes a normal row to the existing messages table in the contact's own Conversation (see
/// IConversationService.GetOrCreateForContactAsync). Sending is always processed in small bounded
/// batches (ProcessBatchAsync) rather than one giant synchronous loop over every recipient, so a
/// campaign with thousands of contacts never blocks a single HTTP request. The dashboard sends the first
/// batch; the background sender (Business/Jobs/CampaignSenderWorker) starts scheduled campaigns when
/// their time comes and keeps sending batches of every running campaign until it completes. Only one
/// sender works on a campaign at a time, and Stop/Cancel are re-checked before every message.
/// </summary>
public interface ICampaignService
{
    Task<IReadOnlyList<CampaignListRow>> ListAsync(Guid clientId, CancellationToken ct = default);

    Task<CampaignDetailsResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default);

    /// <summary>Creates a Campaign in Draft status plus one CampaignRecipient per (de-duplicated)
    /// contact — nothing is sent yet.</summary>
    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken ct = default);

    Task<CampaignResponse?> ScheduleAsync(Guid clientId, Guid id, DateTimeOffset scheduledAt, CancellationToken ct = default);

    /// <summary>Transitions Draft/Scheduled/Paused → Running (queues every Pending recipient), then processes
    /// the first batch immediately. On a Paused (stopped) campaign this is "Resume sending". Calling it again
    /// on an already-Running campaign just processes another batch — safe to call repeatedly.</summary>
    Task<ProcessCampaignBatchResult?> SendAsync(Guid clientId, Guid id, int batchSize = 20, CancellationToken ct = default);

    /// <summary>Processes up to batchSize Queued recipients of a Running campaign, stopping early if the campaign
    /// is stopped or cancelled meanwhile. Marks it Completed once no recipients remain queued. If another sender
    /// holds the campaign it sends nothing and just reports progress.</summary>
    Task<ProcessCampaignBatchResult?> ProcessBatchAsync(Guid clientId, Guid id, int batchSize = 20, CancellationToken ct = default);

    /// <summary>For the background sender: scheduled campaigns whose time has come and running ones, across clients.</summary>
    Task<IReadOnlyList<DueCampaign>> ListDueAsync(CancellationToken ct = default);

    /// <summary>For the background sender: starts a Scheduled campaign whose ScheduledAt has passed, or sends the next
    /// batch of a Running one. Null (nothing done) when it is anything else by now, e.g. stopped or cancelled.</summary>
    Task<ProcessCampaignBatchResult?> SendDueAsync(Guid clientId, Guid id, int batchSize, CancellationToken ct = default);

    /// <summary>Stops a Scheduled or Running campaign (status Paused, shown as "Stopped"): nothing more is sent,
    /// including the rest of a batch already in progress, and a scheduled one won't start. Recipients not yet
    /// messaged stay waiting, so <see cref="SendAsync"/> resumes it and <see cref="CancelAsync"/> ends it.</summary>
    Task<CampaignResponse?> StopAsync(Guid clientId, Guid id, CancellationToken ct = default);

    /// <summary>Cancels a Draft/Scheduled/Running/Paused campaign. Recipients already sent are untouched;
    /// any still Pending/Queued are marked Failed with an explanatory ErrorMessage.</summary>
    Task<CampaignResponse?> CancelAsync(Guid clientId, Guid id, CancellationToken ct = default);
}
