using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Appointments;

/// <summary>A client's weekly opening rules, booking settings and date exceptions.</summary>
public interface IAvailabilityRepository
{
    /// <summary>Tracked.</summary>
    Task<List<AvailabilityRule>> ListRulesAsync(Guid clientId, CancellationToken ct = default);

    Task<List<AvailabilityRule>> ListRulesReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    void AddRule(AvailabilityRule rule);

    /// <summary>Tracked.</summary>
    Task<BookingSettings?> GetBookingSettingsAsync(Guid clientId, CancellationToken ct = default);

    Task<BookingSettings?> GetBookingSettingsReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    void AddBookingSettings(BookingSettings settings);

    /// <summary>Read-only, dates in [from, to].</summary>
    Task<IReadOnlyList<AvailabilityException>> ListExceptionsAsync(Guid clientId, DateOnly from, DateOnly to,
        CancellationToken ct = default);

    /// <summary>Read-only, from <paramref name="fromDate"/> on, ordered by date.</summary>
    Task<IReadOnlyList<AvailabilityException>> ListExceptionsFromAsync(Guid clientId, DateOnly fromDate, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<AvailabilityException?> GetExceptionByDateAsync(Guid clientId, DateOnly date, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<AvailabilityException?> GetExceptionAsync(Guid clientId, Guid exceptionId, CancellationToken ct = default);

    void AddException(AvailabilityException exception);

    void RemoveException(AvailabilityException exception);
}
