namespace Omni.App.Entities.Dtos.Reports;

/// <summary>One channel on the Reports page: conversations started and messages received/sent in the period.</summary>
public sealed record ReportChannelRow(string Channel, int NewConversations, int Received, int Sent);
