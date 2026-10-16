namespace Omni.App.Entities.Requests.BusinessTypes;

/// <summary>Body of PUT /api/platform-admin/clients/{id}/business-type.</summary>
public record ChangeBusinessTypeRequest(string? BusinessTypeCode);
