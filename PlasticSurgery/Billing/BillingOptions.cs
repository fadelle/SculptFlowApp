namespace PlasticSurgery.Billing;

/// <summary>"Billing" config section. Everything is off until <see cref="Enabled"/> is true: no usage is rated or
/// reserved, every clinic is treated as entitled to everything, and the maintenance worker idles. Plans, rate cards
/// and wallets can still be set up through the admin API while it's off.</summary>
public class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>Master switch: prepaid usage billing + subscription entitlements.</summary>
    public bool Enabled { get; set; }

    /// <summary>The one account currency for now (ISO 4217). Plans and rates must use it.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>How long a past_due subscription keeps working before it expires.</summary>
    public int GracePeriodDays { get; set; } = 7;

    /// <summary>A reservation whose delivery outcome never arrived is resolved after this long: settled if the
    /// message was delivered after all, otherwise released.</summary>
    public int ReservationTimeoutHours { get; set; } = 72;

    /// <summary>How often the maintenance worker renews due subscriptions and resolves stale reservations.</summary>
    public int MaintenanceIntervalMinutes { get; set; } = 5;

    /// <summary>Plan code a newly registered clinic starts on (first period not charged — a trial). Empty = new
    /// clinics start without a plan and an admin assigns one.</summary>
    public string? SignupPlanCode { get; set; }

    public string NormalizedCurrency => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();
}
