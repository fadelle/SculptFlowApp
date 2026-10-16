namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record SystemTotals(int Clients, int ActiveClients, int StaffUsers, int Leads, int LeadsLast7Days, int Conversations,
    int ConversationsInHumanMode, int MessagesLast24h, int MessagesLast7Days, int AiMessagesLast7Days, int InboundLast7Days,
    int FailedMessagesLast7Days, int UpcomingBookings, int BookingsBookedLast7Days, int RunningCampaigns,
    int ConnectedChannels, int UnhealthyChannels, int CalendarProblems, int KnowledgeDocuments);
