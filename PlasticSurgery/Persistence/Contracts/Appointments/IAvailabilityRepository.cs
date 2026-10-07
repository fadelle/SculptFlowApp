using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Appointments;

/// <summary>A clinic's weekly opening rules, booking settings and date exceptions.</summary>
public interface IAvailabilityRepository
{
    /// <summary>Tracked.</summary>
    Task<List<ClinicAvailabilityRule>> ListRulesAsync(Guid clinicId, CancellationToken ct = default);

    Task<List<ClinicAvailabilityRule>> ListRulesReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    void AddRule(ClinicAvailabilityRule rule);

    /// <summary>Tracked.</summary>
    Task<ClinicBookingSettings?> GetBookingSettingsAsync(Guid clinicId, CancellationToken ct = default);

    Task<ClinicBookingSettings?> GetBookingSettingsReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    void AddBookingSettings(ClinicBookingSettings settings);

    /// <summary>Read-only, dates in [from, to].</summary>
    Task<IReadOnlyList<ClinicAvailabilityException>> ListExceptionsAsync(Guid clinicId, DateOnly from, DateOnly to,
        CancellationToken ct = default);

    /// <summary>From <paramref name="fromDate"/> on, ordered by date.</summary>
    Task<IReadOnlyList<ClinicAvailabilityException>> ListExceptionsFromAsync(Guid clinicId, DateOnly fromDate, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<ClinicAvailabilityException?> GetExceptionByDateAsync(Guid clinicId, DateOnly date, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<ClinicAvailabilityException?> GetExceptionAsync(Guid clinicId, Guid exceptionId, CancellationToken ct = default);

    void AddException(ClinicAvailabilityException exception);

    void RemoveException(ClinicAvailabilityException exception);
}
