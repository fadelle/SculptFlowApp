using System.Security.Cryptography;
using Omni.App.Business.Contracts.HttpClients.Meta;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Channels;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Channels;
using Omni.App.Entities.Responses.Channels;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Common.Helpers;

namespace Omni.App.Business.Services.Channels;

public class ChannelIntegrationService : IChannelIntegrationService
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMetaGraphClient _graph;
    private readonly ITelegramIntegrationService _telegram;
    private readonly IEntitlementService _entitlements;
    private readonly ICacheManager _cache;

    public ChannelIntegrationService(IChannelIntegrationRepository integrations, IUnitOfWork unitOfWork, IMetaGraphClient graph, ITelegramIntegrationService telegram,
        IEntitlementService entitlements, ICacheManager cache)
    {
        _integrations = integrations;
        _unitOfWork = unitOfWork;
        _graph = graph;
        _telegram = telegram;
        _entitlements = entitlements;
        _cache = cache;
    }

    public async Task<IReadOnlyList<ChannelIntegrationResponse>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        var existing = (await _integrations.ListForClientAsync(clientId, ct)).ToDictionary(c => c.Channel);

        // Always return one card per known channel, even if it's never been saved — the Settings
        // page shouldn't have to special-case "no row yet" vs. "disconnected".
        return ChannelType.All
            .Select(channel => existing.TryGetValue(channel, out var row)
                ? ToResponse(row)
                : new ChannelIntegrationResponse(
                    Guid.Empty, clientId, channel, ChannelIntegrationStatus.Disconnected,
                    null, null, null, null, null, false, false, null, null, DateTimeOffset.MinValue))
            .ToList();
    }

    public async Task<IReadOnlySet<string>> ListConnectedChannelsAsync(Guid clientId, CancellationToken ct = default) =>
        (await _integrations.ListForClientReadOnlyAsync(clientId, ct))
            .Where(c => c.Status == ChannelIntegrationStatus.Connected).Select(c => c.Channel).ToHashSet(StringComparer.Ordinal);

    public async Task<ChannelIntegrationResponse> SaveAsync(SaveChannelIntegrationRequest request, CancellationToken ct = default)
    {
        if (!ChannelType.All.Contains(request.Channel))
        {
            throw new ArgumentException("channels.errors.unknown").WithArgs(request.Channel);
        }

        if (request.Channel == ChannelType.Telegram)
        {
            // A Telegram connection isn't "save some fields": it needs getMe + setWebhook, which only
            // ITelegramIntegrationService.ConnectAsync does. Saving raw fields would produce a
            // "connected" row with no webhook.
            throw new ArgumentException("channels.errors.telegramUseBotToken");
        }

        var row = await _integrations.GetAsync(request.ClientId, request.Channel, ct);

        // Saving with a token makes the channel connected — the plan's channel limits apply.
        if (!string.IsNullOrWhiteSpace(request.AccessToken) || !string.IsNullOrWhiteSpace(row?.AccessToken))
        {
            await _entitlements.EnsureCanConnectChannelAsync(request.ClientId, request.Channel, ct);
        }

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new ChannelIntegration
            {
                Id = Guid.NewGuid(),
                ClientId = request.ClientId,
                Channel = request.Channel,
                CreatedAt = now,
            };
            _integrations.Add(row);
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

        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, ct);
        return ToResponse(row);
    }

    public async Task DisconnectAsync(Guid clientId, string channel, CancellationToken ct = default)
    {
        if (!ChannelType.All.Contains(channel))
        {
            throw new ArgumentException("channels.errors.unknown").WithArgs(channel);
        }

        if (channel == ChannelType.Telegram)
        {
            // Also removes the webhook at Telegram — see TelegramIntegrationService.DisconnectAsync.
            await _telegram.DisconnectAsync(clientId, ct);
            return;
        }

        var row = await _integrations.GetAsync(clientId, channel, ct);
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

        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, ct);
    }

    public async Task<ChannelIntegrationResponse> ConnectWhatsAppAsync(ConnectWhatsAppRequest request, CancellationToken ct = default)
    {
        await _entitlements.EnsureCanConnectChannelAsync(request.ClientId, ChannelType.WhatsApp, ct);
        var token = !string.IsNullOrWhiteSpace(request.AccessToken)
            ? request.AccessToken
            : !string.IsNullOrWhiteSpace(request.Code)
                ? await _graph.ExchangeCodeForTokenAsync(request.Code, request.RedirectUri, ct)
                : throw new InvalidOperationException("channels.errors.codeOrTokenRequired");

        var wabaId = request.WabaId;
        var phoneNumberId = request.PhoneNumberId;

        if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(phoneNumberId))
        {
            // No popup postMessage to read these from (full-page redirect flow) — discover them
            // via the Graph API instead: which WABA did this login just grant access to, and
            // which phone number is registered under it.
            var wabas = await _graph.GetClientWhatsAppBusinessAccountsAsync(token, ct);
            var waba = wabas.FirstOrDefault()
                ?? throw new InvalidOperationException("channels.errors.noWaba");
            wabaId = waba.WabaId;

            var phones = await _graph.GetPhoneNumbersForWabaAsync(wabaId, token, ct);
            var phone = phones.FirstOrDefault()
                ?? throw new InvalidOperationException("channels.errors.noPhoneNumber").WithArgs(waba.WabaName, wabaId);
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
            request.ClientId,
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
        await _entitlements.EnsureCanConnectChannelAsync(request.ClientId, ChannelType.Facebook, ct);
        var shortLivedToken = !string.IsNullOrWhiteSpace(request.Code)
            ? await _graph.ExchangeCodeForTokenAsync(request.Code, ct: ct)
            : !string.IsNullOrWhiteSpace(request.AccessToken)
                ? request.AccessToken
                : throw new InvalidOperationException("channels.errors.codeOrTokenRequired");

        var longLivedToken = await _graph.GetLongLivedTokenAsync(shortLivedToken, ct);

        var page = await _graph.GetFirstManagedPageAsync(longLivedToken, ct)
            ?? throw new InvalidOperationException("channels.errors.noPage");

        return await SaveAsync(new SaveChannelIntegrationRequest(
            request.ClientId,
            ChannelType.Facebook,
            DisplayName: page.PageName,
            PhoneNumberId: null,
            WhatsAppBusinessId: null,
            PageId: page.PageId,
            InstagramBusinessId: null,
            AccessToken: page.PageAccessToken,
            WebhookVerifyToken: null), ct);
    }

    internal static ChannelIntegrationResponse ToResponse(ChannelIntegration c) => new(
        c.Id, c.ClientId, c.Channel, c.Status, c.DisplayName,
        c.PhoneNumberId, c.WhatsAppBusinessId, c.PageId, c.InstagramBusinessId,
        HasAccessToken: !string.IsNullOrEmpty(c.AccessToken),
        HasWebhookVerifyToken: !string.IsNullOrEmpty(c.WebhookVerifyToken),
        c.LastVerifiedAt, c.LastError, c.UpdatedAt, c.Pin,
        c.TelegramBotId, c.TelegramBotUsername, c.WebhookStatus, c.WebhookRegisteredAt, c.LastWebhookAt);
}
