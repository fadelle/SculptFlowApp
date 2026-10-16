namespace Omni.App.Entities.Dtos.Bookings;

public record AvailabilityExceptionDto(Guid Id, string Date, bool IsClosed, string? Start, string? End, string? Reason);
