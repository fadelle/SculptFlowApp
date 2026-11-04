namespace Omni.App.Common.Configs;

/// <summary>Proxy:* config: what the edge in front of the app tells it about the visitor.</summary>
public class ProxyOptions
{
    /// <summary>The request header holding the visitor's IP, set by the edge and not by the client (CF-Connecting-IP behind
    /// Render's Cloudflare). Empty = use the connection's address. Only set it where every request passes that edge.</summary>
    public string? ClientIpHeader { get; set; }
}
