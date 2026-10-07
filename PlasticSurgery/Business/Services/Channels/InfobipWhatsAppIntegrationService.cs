using PlasticSurgery.Business.Contracts.HttpClients.Infobip;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Channels;
using PlasticSurgery.Business.Services.Inbox;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Dtos.Infobip;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Channels;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Channels;

namespace PlasticSurgery.Business.Services.Channels;

public class InfobipWhatsAppIntegrationService : IInfobipWhatsAppIntegrationService
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInfobipClient _client;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<InfobipWhatsAppIntegrationService> _logger;
    private readonly IEntitlementService _entitlements;
    private readonly ICacheManager _cache;

    public InfobipWhatsAppIntegrationService(
        IChannelIntegrationRepository integrations, IUnitOfWork unitOfWork, IInfobipClient client, IConfiguration configuration,
        IHttpContextAccessor http, ILogger<InfobipWhatsAppIntegrationService> logger, IEntitlementService entitlements,
        ICacheManager cache)
    {
        _integrations = integrations;
        _unitOfWork = unitOfWork;
        _client = client;
        _configuration = configuration;
        _http = http;
        _logger = logger;
        _entitlements = entitlements;
        _cache = cache;
    }

    public async Task<ChannelIntegrationResponse> ConnectAsync(Guid clinicId, string? senderNumber, CancellationToken ct = default)
    {
        var sender = InfobipNumbers.Normalize(senderNumber);
        if (sender.Length is < 8 or > 15)
        {
            throw new ArgumentException("Enter the WhatsApp number in international format, e.g. +44 7860 099299.");
        }
        await _entitlements.EnsureCanConnectChannelAsync(clinicId, ChannelType.WhatsApp, ct);

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

        var usedElsewhere = await _integrations.IsInfobipSenderConnectedElsewhereAsync(sender, clinicId, ct);
        if (usedElsewhere)
        {
            throw new InvalidOperationException($"+{sender} is already connected to another clinic.");
        }

        var row = await _integrations.GetAsync(clinicId, ChannelType.WhatsApp, ct);

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
            _integrations.Add(row);
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

        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, ct);
        _logger.LogInformation("Clinic {ClinicId} connected WhatsApp through Infobip (connection {ConnectionId}).", clinicId, row.Id);
        return ChannelIntegrationService.ToResponse(row);
    }

    public async Task<string?> GetWebhookUrlAsync(Guid clinicId, CancellationToken ct = default)
    {
        var row = await _integrations.GetReadOnlyAsync(clinicId, ChannelType.WhatsApp, ct);
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
