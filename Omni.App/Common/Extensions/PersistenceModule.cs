using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Appointments;
using Omni.App.Persistence.Contracts.Billing;
using Omni.App.Persistence.Contracts.Calendars;
using Omni.App.Persistence.Contracts.Campaigns;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Configuration;
using Omni.App.Persistence.Contracts.Dashboard;
using Omni.App.Persistence.Contracts.Events;
using Omni.App.Persistence.Contracts.Inbox;
using Omni.App.Persistence.Contracts.Knowledge;
using Omni.App.Persistence.Contracts.Leads;
using Omni.App.Persistence.Contracts.Notifications;
using Omni.App.Persistence.Contracts.Procedures;
using Omni.App.Persistence.Contracts.TikTok;
using Omni.App.Persistence.Contracts.Users;
using Omni.App.Persistence.Contracts.WhatsApp;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Appointments;
using Omni.App.Persistence.Repositories.Billing;
using Omni.App.Persistence.Repositories.Calendars;
using Omni.App.Persistence.Repositories.Campaigns;
using Omni.App.Persistence.Repositories.Channels;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Configuration;
using Omni.App.Persistence.Repositories.Dashboard;
using Omni.App.Persistence.Repositories.Events;
using Omni.App.Persistence.Repositories.Inbox;
using Omni.App.Persistence.Repositories.Knowledge;
using Omni.App.Persistence.Repositories.Leads;
using Omni.App.Persistence.Repositories.Notifications;
using Omni.App.Persistence.Repositories.Procedures;
using Omni.App.Persistence.Repositories.TikTok;
using Omni.App.Persistence.Repositories.Users;
using Omni.App.Persistence.Repositories.WhatsApp;
using Omni.App.Persistence.Contracts.PlatformAdmin;
using Omni.App.Persistence.Repositories.PlatformAdmin;
using Omni.App.Persistence.Contracts.Monitoring;
using Omni.App.Persistence.Repositories.Monitoring;

namespace Omni.App.Common.Extensions;

/// <summary>
/// DI registration for Persistence/: the unit of work and every repository. All are scoped and share the request's
/// ApplicationDbContext, so changes made through any of them are saved together by IUnitOfWork.
/// </summary>
public static class PersistenceModule
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<ISettingRepository, SettingRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IEventLogRepository, EventLogRepository>();
        services.AddScoped<IApiRouteStatRepository, ApiRouteStatRepository>();
        services.AddScoped<IAppLogRepository, AppLogRepository>();
        services.AddScoped<IMessageTraceStepRepository, MessageTraceStepRepository>();
        services.AddScoped<IMessageTraceAdminRepository, MessageTraceAdminRepository>();
        services.AddScoped<ILeadRepository, LeadRepository>();
        services.AddScoped<IProcedureRepository, ProcedureRepository>();
        services.AddScoped<IProcedureBookingRepository, ProcedureBookingRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IInboxSettingsRepository, InboxSettingsRepository>();
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
        services.AddScoped<IClientSubscriptionRepository, ClientSubscriptionRepository>();

        // Platform admin (cross-client reads for the admin portal)
        services.AddScoped<IClientAdminRepository, ClientAdminRepository>();
        services.AddScoped<IChannelAdminRepository, ChannelAdminRepository>();
        services.AddScoped<IStaffAdminRepository, StaffAdminRepository>();
        services.AddScoped<ILeadAdminRepository, LeadAdminRepository>();
        services.AddScoped<IContentAdminRepository, ContentAdminRepository>();
        services.AddScoped<IOverviewAdminRepository, OverviewAdminRepository>();

        return services;
    }
}
