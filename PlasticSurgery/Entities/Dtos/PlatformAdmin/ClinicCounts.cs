namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record ClinicCounts(int Staff, int Leads, int Conversations, int Messages, int MessagesLast7Days, int AiMessagesLast7Days,
    int FailedMessagesLast7Days, int UpcomingAppointments, int Procedures, int ActiveProcedures, int KnowledgeDocuments,
    int Campaigns, int RunningCampaigns, int Templates, int UnreadNotifications);
