using PlasticSurgery.Integrations.Telegram;
using PlasticSurgery.Integrations.WhatsApp;

namespace PlasticSurgery.Billing;

/// <summary>
/// DI registration for the Subscriptions &amp; Billing module. Everything it owns lives in Billing/ (plus its tables and
/// entities); the rest of the app talks to it only through IEntitlementService (may the clinic do X?) and
/// IMessageBillingService (reserve/settle/release for a message), and channels plug in with IChannelBillingPolicy.
/// That boundary is what would make it extractable later. See docs/billing.md.
/// </summary>
public static class BillingModule
{
    public static IServiceCollection AddBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.Section));
        services.Configure<WhatsAppBillingOptions>(configuration.GetSection(BillingOptions.Section + ":WhatsApp"));
        services.Configure<ProviderBillingOptions>(configuration.GetSection(BillingOptions.Section + ":ProviderBilling"));
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<BillingDbFactory>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IMessageBillingService, MessageBillingService>();
        services.AddScoped<IProviderBillingService, ProviderBillingService>();
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<IRateCardService, RateCardService>();
        services.AddScoped<IBillingQueryService, BillingQueryService>();

        // One billing policy per messaging channel (the channel's own pricing rules).
        services.AddScoped<IChannelBillingPolicy, WhatsAppBillingPolicy>();
        services.AddScoped<IChannelBillingPolicy, TelegramBillingPolicy>();

        services.AddHostedService<BillingMaintenanceWorker>();
        return services;
    }
}
