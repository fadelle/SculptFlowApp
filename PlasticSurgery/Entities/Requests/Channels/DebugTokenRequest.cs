namespace PlasticSurgery.Entities.Requests.Channels;

/// <summary>DEBUG-ONLY: asks the backend to report a token's (or a code's) granted scopes.</summary>
public record DebugTokenRequest(string? Code = null, string? AccessToken = null, string? RedirectUri = null);
