namespace PlasticSurgery.Entities.Requests.Configuration;

/// <summary>Body of PUT /api/platform-admin/settings/{section}/{key}. <see cref="Note"/> is an optional reason, kept on the row.</summary>
public record SetSettingRequest(string? Value, string? Note);
