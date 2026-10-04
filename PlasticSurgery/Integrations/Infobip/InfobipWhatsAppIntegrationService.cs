using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.Telegram;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.Infobip;

/// <summary>
/// Connects a clinic's WhatsApp to its sender number on SculptFlow's Infobip account. There's no Embedded
/// Signup on this path: the number is registered as a WhatsApp sender in the Infobip portal first (by
/// SculptFlow), then the clinic (or SculptFlow on its behalf) enters it in Settings → Messaging Integrations.
///
/// Connect:
///   1. Infobip must be the active provider (WhatsApp:Provider = infobip) and configured (Infobip:BaseUrl/ApiKey)
///   2. GET business-info proves the number is a WhatsApp sender on our Infobip account
///   3. the number may be connected to only one clinic (also enforced by ux_channel_integrations_provider_sender)
///   4. upsert the clinic's single WhatsApp row: provider=infobip, provider_sender_id, a per-connection webhook
///      secret, status=connected. Meta-only fields are cleared so Meta webhooks can no longer route to it.
/// Disconnect is the generic ChannelIntegrationService.DisconnectAsync (status=disconnected, secret dropped,
/// so the webhook URL stops working at once).
/// </summary>
public interface IInfobipWhatsAppIntegrationService
{
    /// <summary>Throws ArgumentException (malformed number) or InvalidOperationException (Infobip not active or
    /// configured, number not on our Infobip account, number used by another clinic, Infobip unreachable).
    /// Messages are shown to clinic staff, so they never name Infobip or config keys (details go to the log).</summary>
    Task<ChannelIntegrationResponse> ConnectAsync(Guid clinicId, string? senderNumber, CancellationToken ct = default);

    /// <summary>The clinic's webhook URL to paste into the Infobip portal (contains the secret — show it only to
    /// the clinic's own signed-in staff, never in an API response). Null when not connected through Infobip.</summary>
    Task<string?> GetWebhookUrlAsync(Guid clinicId, CancellationToken ct = default);
}

public class InfobipWhatsAppIntegrationService : IInfobipWhatsAppIntegrationService
{
    private readonly ApplicationDbContext _db;
    private readonly IInfobipClient _client;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<InfobipWhatsAppIntegrationService> _logger;

    public InfobipWhatsAppIntegrationService(
        ApplicationDbContext db, IInfobipClient client, IConfiguration configuration,
        IHttpContextAccessor http, ILogger<InfobipWhatsAppIntegrationService> logger)
    {
        _db = db;
        _client = client;
        _configuration = configuration;
        _http = http;
        _logger = logger;
    }

    public async Task<ChannelIntegrationResponse> ConnectAsync(Guid clinicId, string? senderNumber, CancellationToken ct = default)
    {
        var sender = InfobipNumbers.Normalize(senderNumber);
        if (sender.Length is < 8 or > 15)
        {
            throw new ArgumentException("Enter the WhatsApp number in international format, e.g. +44 7860 099299.");
        }

        if (WhatsAppService.ActiveProviderName(_configuration) != ChannelProvider.Infobip)
        {
            throw new InvalidOperationException("Connecting a WhatsApp number this way isn't available right now. Contact SculptFlow support.");
        }
        if (!_client.IsConfigured)
        {
            _logger.LogError("WhatsApp:Provider is infobip but Infobip:BaseUrl / Infobip:ApiKey are not set.");
            throw new InvalidOperationException("WhatsApp can't be connected right now. Contact SculptFlow support.");
        }

        InfobipSenderInfo? info;
        try
        {
            info = await _client.GetWhatsAppSenderAsync(sender, ct);
        }
        catch (Exception ex) when (ex is InfobipApiException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Infobip sender check failed for clinic {ClinicId}.", clinicId);
            throw new InvalidOperationException("Couldn't check this number right now. Try again in a minute.");
        }
        if (info is null)
        {
            throw new InvalidOperationException(
                $"+{sender} isn't set up for WhatsApp on SculptFlow yet. Contact SculptFlow support to activate it.");
        }

        var usedElsewhere = await _db.ChannelIntegrations.AnyAsync(c =>
            c.Channel == ChannelType.WhatsApp && c.Provider == ChannelProvider.Infobip && c.ProviderSenderId == sender
            && c.Status == ChannelIntegrationStatus.Connected && c.ClinicId != clinicId, ct);
        if (usedElsewhere)
        {
            throw new InvalidOperationException($"+{sender} is already connected to another clinic.");
        }

        var row = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == ChannelType.WhatsApp, ct);

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new ChannelIntegration
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                Channel = ChannelType.WhatsApp,
                CreatedAt = now
            };
            _db.ChannelIntegrations.Add(row);
        }

        // Keep the existing secret when reconnecting the same Infobip number, so the URL already pasted into
        // the Infobip portal keeps working; anything else gets a fresh one.
        var sameConnection = ChannelProvider.Of(row) == ChannelProvider.Infobip && row.ProviderSenderId == sender
                             && !string.IsNullOrEmpty(row.WebhookVerifyToken);
        if (!sameConnection)
        {
            row.WebhookVerifyToken = TelegramWebhookSecret.Generate();
        }

        row.Provider = ChannelProvider.Infobip;
        row.ProviderSenderId = sender;
        row.DisplayName = "+" + sender;
        row.VerifiedName = info.DisplayName;

        // Meta-only identifiers/credentials don't apply to an Infobip connection.
        row.PhoneNumberId = null;
        row.WhatsAppBusinessId = null;
        row.MetaBusinessId = null;
        row.AccessToken = null;
        row.Pin = null;

        row.Status = ChannelIntegrationStatus.Connected;
        row.LastVerifiedAt = now;
        row.LastError = null;
        row.IsHealthy = true;
        row.HealthLevel = WhatsAppHealthLevel.Healthy;
        row.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Clinic {ClinicId} connected WhatsApp through Infobip (connection {ConnectionId}).", clinicId, row.Id);
        return ChannelIntegrationService.ToResponse(row);
    }

    public async Task<string?> GetWebhookUrlAsync(Guid clinicId, CancellationToken ct = default)
    {
        var row = await _db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == ChannelType.WhatsApp, ct);
        if (row is null || ChannelProvider.Of(row) != ChannelProvider.Infobip
            || row.Status != ChannelIntegrationStatus.Connected || string.IsNullOrEmpty(row.WebhookVerifyToken))
        {
            return null;
        }

        var baseUrl = ResolvePublicBaseUrl();
        return baseUrl is null ? null : InfobipWebhookUrls.Build(baseUrl, row.Id, row.WebhookVerifyToken);
    }

    /// <summary>Same App:PublicBaseUrl-first convention as TelegramIntegrationService.ResolvePublicBaseUrl,
    /// but returns null instead of throwing — this only feeds a URL shown on the settings page.</summary>
    private string? ResolvePublicBaseUrl()
    {
        var configured = _configuration["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        var request = _http.HttpContext?.Request;
        if (request is null) return null;
        var host = request.Host.Host;
        var isLocal = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                      || host.StartsWith("127.") || host == "::1" || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        return isLocal ? null : $"https://{request.Host}";
    }
}
