namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

public record SystemTotals(int Clinics, int ActiveClinics, int StaffUsers, int Leads, int LeadsLast7Days, int Conversations,
    int ConversationsInHumanMode, int MessagesLast24h, int MessagesLast7Days, int AiMessagesLast7Days, int InboundLast7Days,
    int FailedMessagesLast7Days, int UpcomingAppointments, int AppointmentsBookedLast7Days, int RunningCampaigns,
    int ConnectedChannels, int UnhealthyChannels, int CalendarProblems, int KnowledgeDocuments);
