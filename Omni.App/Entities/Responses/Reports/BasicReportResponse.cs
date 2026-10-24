using Omni.App.Entities.Dtos.Reports;

namespace Omni.App.Entities.Responses.Reports;

/// <summary>The Reports page's basic sections for one period. Bookings and Campaigns are null when the plan has no
/// bookings / campaigns (the page hides them).</summary>
public sealed record BasicReportResponse(
    int NewConversations,
    int MessagesReceived,
    int MessagesSent,
    IReadOnlyList<ReportChannelRow> Channels,
    int NewContacts,
    IReadOnlyList<ReportCountRow> ContactsBySource,
    IReadOnlyList<ReportCountRow> ContactsByStage,
    ReportBookingCounts? Bookings,
    IReadOnlyList<ReportCampaignRow>? Campaigns);
