using Omni.App.Entities.Dtos.Reports;

namespace Omni.App.Persistence.Contracts.Reports;

/// <summary>Read-only queries behind the Reports page, one client at a time. Date ranges are [from, to); either bound may be
/// null (open-ended).</summary>
public interface IReportRepository
{
    /// <summary>Per channel: conversations created and messages received/sent in the range (busiest first).</summary>
    Task<IReadOnlyList<ReportChannelRow>> GetChannelActivityAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    /// <summary>Contacts created in the range, by source (null = not set), most first.</summary>
    Task<IReadOnlyList<ReportCountRow>> CountNewContactsBySourceAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    /// <summary>Contacts created in the range, by their current stage key, most first.</summary>
    Task<IReadOnlyList<ReportCountRow>> CountNewContactsByStageAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    /// <summary>Same date rules as the dashboard: made = created in range; outcomes = scheduled to start in range.</summary>
    Task<ReportBookingCounts> GetBookingCountsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    /// <summary>Campaigns that started sending in the range, newest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<ReportCampaignRow>> ListCampaignsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to, int take,
        CancellationToken ct = default);

    /// <summary>Every wait that started in the range: an inbound message with no other inbound message since the last reply
    /// before it, and the first reply (AI, Inbox or WhatsApp Business app) after it.</summary>
    Task<IReadOnlyList<ReportWait>> ListWaitsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    Task<ReportReplyCounts> GetReplyCountsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    /// <summary>Messages received/sent per 15-minute UTC slot in the range, oldest first (slots without messages left out).</summary>
    Task<IReadOnlyList<ReportMessageBucket>> GetMessageBucketsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    /// <summary>Usage settled (charged) in the range, by event type, highest amount first.</summary>
    Task<IReadOnlyList<ReportUsageRow>> GetSettledUsageAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);
}
