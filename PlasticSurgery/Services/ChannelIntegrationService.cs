using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class ChannelIntegrationService : IChannelIntegrationService
{
    private readonly ApplicationDbContext _db;
    private readonly IMetaGraphClient _graph;

    public ChannelIntegrationService(ApplicationDbContext db, IMetaGraphClient graph)
    {
        _db = db;
        _graph = graph;
    }

    public async Task<IReadOnlyList<ChannelIntegrationResponse>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var existing = await _db.ChannelIntegrations
            .Where(c => c.ClinicId == clinicId)
            .ToDictionaryAsync(c => c.Channel, ct);

        // Always return one card per known channel, even if it's never been saved — the Settings
        // page shouldn't have to special-case "no row yet" vs. "disconnected".
        return ChannelType.All
            .Select(channel => existing.TryGetValue(channel, out var row)
                ? ToResponse(row)
                : new ChannelIntegrationResponse(
                    Guid.Empty, clinicId, channel, ChannelIntegrationStatus.Disconnected,
                    null, null, null, null, null, false, false, null, null, DateTimeOffset.MinValue))
            .ToList();
    }

    public async Task<ChannelIntegrationResponse> SaveAsync(SaveChannelIntegrationRequest request, CancellationToken ct = default)
    {
        if (!ChannelType.All.Contains(request.Channel))
        {
            throw new ArgumentException($"Unknown channel '{request.Channel}'.", nameof(request));
        }

        var row = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == request.ClinicId && c.Channel == request.Channel, ct);

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new ChannelIntegration
            {
                Id = Guid.NewGuid(),
                ClinicId = request.ClinicId,
                Channel = request.Channel,
                CreatedAt = now,
            };
            _db.ChannelIntegrations.Add(row);
        }

        row.DisplayName = request.DisplayName;
        row.PhoneNumberId = request.PhoneNumberId;
        row.WhatsAppBusinessId = request.WhatsAppBusinessId;
        row.PageId = request.PageId;
        row.InstagramBusinessId = request.InstagramBusinessId;

        // Only overwrite a secret if a new value was actually typed in — the form never
        // round-trips a saved token back out, so an empty submission means "leave it as-is".
        if (!string.IsNullOrWhiteSpace(request.AccessToken))
        {
            row.AccessToken = request.AccessToken;
        }
        if (!string.IsNullOrWhiteSpace(request.WebhookVerifyToken))
        {
            row.WebhookVerifyToken = request.WebhookVerifyToken;
        }
        if (!string.IsNullOrWhiteSpace(request.Pin))
        {
            row.Pin = request.Pin;
        }

        // NOTE: this MVP doesn't call out to the WhatsApp/Meta Graph API to actually verify the
        // credentials (Project 5) — "connected" here just means "an access token is on file".
        // Wire real verification in here (and set LastVerifiedAt/LastError from the result) once
        // that integration exists.
        row.Status = string.IsNullOrWhiteSpace(row.AccessToken)
            ? ChannelIntegrationStatus.Disconnected
            : ChannelIntegrationStatus.Connected;
        row.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return ToResponse(row);
    }

    public async Task DisconnectAsync(Guid clinicId, string channel, CancellationToken ct = default)
    {
        var row = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == channel, ct);
        if (row is null)
        {
            return;
        }

        row.Status = ChannelIntegrationStatus.Disconnected;
        row.AccessToken = null;
        row.WebhookVerifyToken = null;
        row.Pin = null;
        row.LastVerifiedAt = null;
        row.LastError = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<ChannelIntegrationResponse> ConnectWhatsAppAsync(ConnectWhatsAppRequest request, CancellationToken ct = default)
    {
        var token = !string.IsNullOrWhiteSpace(request.AccessToken)
            ? request.AccessToken
            : !string.IsNullOrWhiteSpace(request.Code)
                ? await _graph.ExchangeCodeForTokenAsync(request.Code, request.RedirectUri, ct)
                : throw new InvalidOperationException("Either AccessToken or Code must be provided.");

        var wabaId = request.WabaId;
        var phoneNumberId = request.PhoneNumberId;

        if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(phoneNumberId))
        {
            // No popup postMessage to read these from (full-page redirect flow) — discover them
            // via the Graph API instead: which WABA did this login just grant access to, and
            // which phone number is registered under it.
            var wabas = await _graph.GetClientWhatsAppBusinessAccountsAsync(token, ct);
            var waba = wabas.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "No WhatsApp Business Account was found for this login — make sure a WABA was selected during signup.");
            wabaId = waba.WabaId;

            var phones = await _graph.GetPhoneNumbersForWabaAsync(wabaId, token, ct);
            var phone = phones.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"No phone number was found under WhatsApp Business Account '{waba.WabaName}' ({wabaId}).");
            phoneNumberId = phone.PhoneNumberId;
        }

        var phoneInfo = await _graph.GetWhatsAppPhoneNumberAsync(phoneNumberId, token, ct);

        // Embedded Signup grants us access to the number, but Meta won't let it send/receive via
        // the Cloud API until it's registered with a two-step-verification PIN — a step the flow
        // otherwise silently skips. Generate one and register on every connect (safe to repeat —
        // Meta treats it as (re)confirming the PIN, not an error).
        var pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await _graph.RegisterPhoneNumberAsync(phoneNumberId, token, pin, ct);

        return await SaveAsync(new SaveChannelIntegrationRequest(
            request.ClinicId,
            ChannelType.WhatsApp,
            DisplayName: phoneInfo?.DisplayPhoneNumber ?? phoneInfo?.VerifiedName,
            PhoneNumberId: phoneNumberId,
            WhatsAppBusinessId: wabaId,
            PageId: null,
            InstagramBusinessId: null,
            AccessToken: token,
            WebhookVerifyToken: null,
            Pin: pin), ct);
    }

    public async Task<ChannelIntegrationResponse> ConnectFacebookAsync(ConnectFacebookRequest request, CancellationToken ct = default)
    {
        var shortLivedToken = !string.IsNullOrWhiteSpace(request.Code)
            ? await _graph.ExchangeCodeForTokenAsync(request.Code, ct: ct)
            : !string.IsNullOrWhiteSpace(request.AccessToken)
                ? request.AccessToken
                : throw new InvalidOperationException("Either Code or AccessToken must be provided.");

        var longLivedToken = await _graph.GetLongLivedTokenAsync(shortLivedToken, ct);

        var page = await _graph.GetFirstManagedPageAsync(longLivedToken, ct)
            ?? throw new InvalidOperationException(
                "No Facebook Page found for this account — the logged-in user must be an admin on at least one Page.");

        return await SaveAsync(new SaveChannelIntegrationRequest(
            request.ClinicId,
            ChannelType.Facebook,
            DisplayName: page.PageName,
            PhoneNumberId: null,
            WhatsAppBusinessId: null,
            PageId: page.PageId,
            InstagramBusinessId: null,
            AccessToken: page.PageAccessToken,
            WebhookVerifyToken: null), ct);
    }

    private static ChannelIntegrationResponse ToResponse(ChannelIntegration c) => new(
        c.Id, c.ClinicId, c.Channel, c.Status, c.DisplayName,
        c.PhoneNumberId, c.WhatsAppBusinessId, c.PageId, c.InstagramBusinessId,
        HasAccessToken: !string.IsNullOrEmpty(c.AccessToken),
        HasWebhookVerifyToken: !string.IsNullOrEmpty(c.WebhookVerifyToken),
        c.LastVerifiedAt, c.LastError, c.UpdatedAt, c.Pin);
}
