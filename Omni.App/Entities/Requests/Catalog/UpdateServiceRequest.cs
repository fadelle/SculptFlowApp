namespace Omni.App.Entities.Requests.Catalog;

/// <summary>Full replacement of the editable fields (name, code, description, duration). Active/inactive
/// is changed separately via SetServiceActiveRequest — services are never deleted.</summary>
public record UpdateServiceRequest(
    string Name,
    string? Code,
    string? Description,
    int? BookingDuration
);
