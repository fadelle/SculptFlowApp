using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Appointments;
using PlasticSurgery.Persistence.Contracts.Billing;
using PlasticSurgery.Persistence.Contracts.Calendars;
using PlasticSurgery.Persistence.Contracts.Campaigns;
using PlasticSurgery.Persistence.Contracts.Channels;
using PlasticSurgery.Persistence.Contracts.Clinics;
using PlasticSurgery.Persistence.Contracts.Configuration;
using PlasticSurgery.Persistence.Contracts.Dashboard;
using PlasticSurgery.Persistence.Contracts.Events;
using PlasticSurgery.Persistence.Contracts.Inbox;
using PlasticSurgery.Persistence.Contracts.Knowledge;
using PlasticSurgery.Persistence.Contracts.Leads;
using PlasticSurgery.Persistence.Contracts.Notifications;
using PlasticSurgery.Persistence.Contracts.Procedures;
using PlasticSurgery.Persistence.Contracts.TikTok;
using PlasticSurgery.Persistence.Contracts.Users;
using PlasticSurgery.Persistence.Contracts.WhatsApp;
using PlasticSurgery.Persistence.Repositories;
using PlasticSurgery.Persistence.Repositories.Appointments;
using PlasticSurgery.Persistence.Repositories.Billing;
using PlasticSurgery.Persistence.Repositories.Calendars;
using PlasticSurgery.Persistence.Repositories.Campaigns;
using PlasticSurgery.Persistence.Repositories.Channels;
using PlasticSurgery.Persistence.Repositories.Clinics;
using PlasticSurgery.Persistence.Repositories.Configuration;
using PlasticSurgery.Persistence.Repositories.Dashboard;
using PlasticSurgery.Persistence.Repositories.Events;
using PlasticSurgery.Persistence.Repositories.Inbox;
using PlasticSurgery.Persistence.Repositories.Knowledge;
using PlasticSurgery.Persistence.Repositories.Leads;
using PlasticSurgery.Persistence.Repositories.Notifications;
using PlasticSurgery.Persistence.Repositories.Procedures;
using PlasticSurgery.Persistence.Repositories.TikTok;
using PlasticSurgery.Persistence.Repositories.Users;
using PlasticSurgery.Persistence.Repositories.WhatsApp;

namespace PlasticSurgery.Common.Extensions;

/// <summary>
/// DI registration for Persistence/: the unit of work and every repository. All are scoped and share the request's
/// ApplicationDbContext, so changes made through any of them are saved together by IUnitOfWork.
/// </summary>
public static class PersistenceModule
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IClinicRepository, ClinicRepository>();
        services.AddScoped<ISettingRepository, SettingRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IEventLogRepository, EventLogRepository>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IProcedureRepository, ProcedureRepository>();
        services.AddScoped<IProcedureBookingRepository, ProcedureBookingRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IChannelIntegrationRepository, ChannelIntegrationRepository>();
        services.AddScoped<IWhatsAppTemplateRepository, WhatsAppTemplateRepository>();
        services.AddScoped<IWhatsAppHealthEventRepository, WhatsAppHealthEventRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<ICampaignAudienceRepository, CampaignAudienceRepository>();
        services.AddScoped<ITikTokIntegrationRepository, TikTokIntegrationRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();
        services.AddScoped<ICalendarIntegrationRepository, CalendarIntegrationRepository>();
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
        services.AddScoped<IKnowledgeSettingsRepository, KnowledgeSettingsRepository>();
        services.AddScoped<IWebsiteSourceRepository, WebsiteSourceRepository>();
        services.AddScoped<IKnowledgeBenchmarkRepository, KnowledgeBenchmarkRepository>();
        // Billing tables used inside an ordinary request (sign-up, entitlement checks). Money operations use their own
        // IBillingUnitOfWork instead (see BillingModule).
        services.AddScoped<IBillingAccountRepository, BillingAccountRepository>();
        services.AddScoped<IClinicSubscriptionRepository, ClinicSubscriptionRepository>();

        return services;
    }
}
