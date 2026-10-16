namespace Omni.App.Entities.Dtos.PlatformAdmin;

public record ClientCounts(int Staff, int Leads, int Conversations, int Messages, int MessagesLast7Days, int AiMessagesLast7Days,
    int FailedMessagesLast7Days, int UpcomingAppointments, int Services, int ActiveServices, int KnowledgeDocuments,
    int Campaigns, int RunningCampaigns, int Templates, int UnreadNotifications);
