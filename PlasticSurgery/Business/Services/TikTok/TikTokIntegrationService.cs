using PlasticSurgery.Business.Contracts.HttpClients.TikTok;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Notifications;
using PlasticSurgery.Business.Contracts.Services.TikTok;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.TikTok;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.TikTok;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.TikTok;

namespace PlasticSurgery.Business.Services.TikTok;

public class TikTokIntegrationService : ITikTokIntegrationService
{
    // Same margin as CalendarIntegrationService — refresh proactively rather than hand out a token that
    // might expire mid-request, or wait for something else to notice it's stale.
    private TimeSpan TokenRefreshMargin => TimeSpan.FromMinutes(_config.IntegrationsTokenRefreshMarginMinutes);

    private readonly ITikTokIntegrationRepository _tikTok;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITikTokProviderClient _provider;
    private readonly INotificationService _notifications;
    private readonly IConfigManager _config;

    public TikTokIntegrationService(ITikTokIntegrationRepository tikTok, IUnitOfWork unitOfWork, ITikTokProviderClient provider, INotificationService notifications, IConfigManager config)
    {
        _config = config;
        _tikTok = tikTok;
        _unitOfWork = unitOfWork;
        _provider = provider;
        _notifications = notifications;
    }

    public async Task<TikTokIntegrationResponse> GetAsync(Guid clinicId, CancellationToken ct = default)
    {
        var row = await _tikTok.GetAsync(clinicId, ct);
        if (row is null)
        {
            return new TikTokIntegrationResponse(Guid.Empty, clinicId, TikTokIntegrationStatus.Disconnected, null, null, true, null);
        }

        if (row.Status == TikTokIntegrationStatus.Connected)
        {
            await EnsureFreshAccessTokenAsync(row, ct);
        }

        return ToResponse(row);
    }

    public async Task RequestConnectAsync(Guid clinicId, CancellationToken ct = default)
    {
        var row = await GetOrCreateAsync(clinicId, ct);
        row.Status = TikTokIntegrationStatus.Pending;
        row.LastProblemMessage = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<TikTokIntegrationResponse> CompleteConnectAsync(
        Guid clinicId, TikTokOAuthTokenResult tokens, TikTokAccountInfo account, CancellationToken ct = default)
    {
        var row = await GetOrCreateAsync(clinicId, ct);
        row.AccessToken = tokens.AccessToken;
        row.RefreshToken = tokens.RefreshToken ?? row.RefreshToken;
        row.TokenExpiresAt = tokens.ExpiresAt;
        row.RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt;
        row.OpenId = account.OpenId;
        row.UnionId = account.UnionId ?? row.UnionId;
        row.DisplayName = account.DisplayName;
        row.AvatarUrl = account.AvatarUrl;
        row.Status = TikTokIntegrationStatus.Connected;
        row.IsHealthy = true;
        row.LastProblemMessage = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    public async Task FailConnectAsync(Guid clinicId, string errorMessage, CancellationToken ct = default)
    {
        var row = await GetOrCreateAsync(clinicId, ct);
        row.Status = TikTokIntegrationStatus.Error;
        row.LastProblemMessage = errorMessage;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task DisconnectAsync(Guid clinicId, CancellationToken ct = default)
    {
        var row = await _tikTok.GetAsync(clinicId, ct);
        if (row is null) return; // already disconnected — nothing to do

        if (!string.IsNullOrWhiteSpace(row.AccessToken))
        {
            await _provider.RevokeAsync(row.AccessToken, ct);
        }

        row.Status = TikTokIntegrationStatus.Disconnected;
        row.OpenId = null;
        row.UnionId = null;
        row.DisplayName = null;
        row.AvatarUrl = null;
        row.AccessToken = null;
        row.RefreshToken = null;
        row.TokenExpiresAt = null;
        row.RefreshTokenExpiresAt = null;
        row.IsHealthy = true;
        row.LastProblemMessage = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<TikTokIntegration> GetOrCreateAsync(Guid clinicId, CancellationToken ct)
    {
        var row = await _tikTok.GetAsync(clinicId, ct);
        if (row is not null) return row;

        var now = DateTimeOffset.UtcNow;
        row = new TikTokIntegration { Id = Guid.NewGuid(), ClinicId = clinicId, Status = TikTokIntegrationStatus.Disconnected, CreatedAt = now, UpdatedAt = now };
        _tikTok.Add(row);
        return row;
    }

    /// <summary>Refreshes and persists a new access token when the stored one is missing/near expiry. Marks
    /// the connection unhealthy (notifying once, on the transition) and does NOT throw if there's no refresh
    /// token, the refresh token itself has expired, or TikTok rejects the refresh — callers should treat the
    /// row's IsHealthy/LastProblemMessage as the source of truth afterward rather than relying on an exception.</summary>
    private async Task EnsureFreshAccessTokenAsync(TikTokIntegration row, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(row.AccessToken) && row.TokenExpiresAt is { } expires
            && expires - DateTimeOffset.UtcNow > TokenRefreshMargin)
        {
            return;
        }

        var wasHealthy = row.IsHealthy;
        string? problem = null;

        if (string.IsNullOrWhiteSpace(row.RefreshToken))
        {
            problem = "This connection has no refresh token on file — reconnect TikTok from Integrations.";
        }
        else if (row.RefreshTokenExpiresAt is { } refreshExpires && refreshExpires <= DateTimeOffset.UtcNow)
        {
            problem = "TikTok's authorization for this account has expired — reconnect to continue.";
        }
        else
        {
            try
            {
                var refreshed = await _provider.RefreshAccessTokenAsync(row.RefreshToken, ct);
                row.AccessToken = refreshed.AccessToken;
                row.RefreshToken = refreshed.RefreshToken ?? row.RefreshToken;
                row.TokenExpiresAt = refreshed.ExpiresAt;
                row.RefreshTokenExpiresAt = refreshed.RefreshTokenExpiresAt ?? row.RefreshTokenExpiresAt;
                row.IsHealthy = true;
                row.LastProblemMessage = null;
            }
            catch (Exception ex)
            {
                problem = $"TikTok authorization stopped working — reconnect to continue. ({ex.Message})";
            }
        }

        if (problem is not null)
        {
            row.IsHealthy = false;
            row.LastProblemMessage = problem;
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        // Only on the transition into unhealthy — see CalendarIntegrationService.ApplySyncCallbackAsync for
        // the same rule: a connection that's already flagged must not spam a repeat notification.
        if (problem is not null && wasHealthy)
        {
            await _notifications.CreateAsync(row.ClinicId, NotificationType.IntegrationUnhealthy,
                "TikTok connection needs attention", problem, link: "/settings/integrations", ct: ct);
        }
    }

    private static TikTokIntegrationResponse ToResponse(TikTokIntegration t) =>
        new(t.Id, t.ClinicId, t.Status, t.DisplayName, t.AvatarUrl, t.IsHealthy, t.LastProblemMessage);
}
