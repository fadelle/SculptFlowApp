namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record ClientCounts(int Members, int Contacts, int Conversations, int Messages, int MessagesLast7Days, int AiMessagesLast7Days,
    int FailedMessagesLast7Days, int UpcomingBookings, int Services, int ActiveServices, int KnowledgeDocuments,
    int Campaigns, int RunningCampaigns, int Templates, int UnreadNotifications);
