namespace PlasticSurgery.Entities.Dtos.Appointments;

public record AvailabilityExceptionDto(Guid Id, string Date, bool IsClosed, string? Start, string? End, string? Reason);
