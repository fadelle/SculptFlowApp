namespace Omni.App.Entities.Requests.Clients;

/// <summary>The Business Info → Details form.</summary>
public record UpdateClientDetailsRequest(string? Name, string? Phone, string? Email, string? Website, string? Address,
    string? OperatingHours, string? BookingInfo);
