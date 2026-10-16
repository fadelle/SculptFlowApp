using Omni.App.Entities.Dtos.Meta;

namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>What Meta says about an access token: the permissions it was granted and the client WhatsApp business
/// accounts it can see (platform-admin diagnostic for the embedded-signup setup).</summary>
public sealed record WhatsAppTokenDebugResult(IReadOnlyList<string> Scopes, IReadOnlyList<ClientWabaInfo> ClientWabas);
