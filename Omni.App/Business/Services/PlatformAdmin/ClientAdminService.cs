using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Common.Statics;
using Omni.App.Business.Contracts.Services.Clients;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Business.Mappers.PlatformAdmin;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Requests.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Services.PlatformAdmin;

public class ClientAdminService : IClientAdminService
{
    private readonly IClientAdminRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IInboxSettingsService _inboxSettings;
    private readonly IBusinessTypeService _businessTypes;
    private readonly IClientProfileService _profile;
    private readonly ICacheManager _cache;

    private readonly IClientRegistrationService _registration;
    private readonly IInviteService _invites;
    private readonly ILogger<ClientAdminService> _logger;

    public ClientAdminService(IClientAdminRepository repository, IUnitOfWork unitOfWork, TimeProvider time, IInboxSettingsService inboxSettings,
        IBusinessTypeService businessTypes, IClientProfileService profile, ICacheManager cache, IClientRegistrationService registration,
        IInviteService invites, ILogger<ClientAdminService> logger)
    {
        _registration = registration;
        _invites = invites;
        _logger = logger;
        _cache = cache;
        _profile = profile;
        _inboxSettings = inboxSettings;
        _businessTypes = businessTypes;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public Task<PagedResponse<ClientRow>> ListAsync(string? search, bool? active, int page, CancellationToken ct) =>
        _repository.ListAsync(search, active, page, ct);

    public Task<List<ClientOption>> OptionsAsync(CancellationToken ct) => _repository.OptionsAsync(ct);

    public async Task<ClientDetail?> GetAsync(Guid id, CancellationToken ct) =>
        await _repository.GetAsync(id, ct) is { } client ? PlatformAdminMapper.ToDetail(client) : null;

    public Task<ClientCounts> CountsAsync(Guid id, CancellationToken ct) => _repository.CountsAsync(id, ct);

    public async Task<PlatformAdminChange> UpdateAsync(Guid id, ClientUpdate update, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(update.Name)) throw new ArgumentException("Client name is required.");
        var tz = string.IsNullOrWhiteSpace(update.Timezone) ? "UTC" : update.Timezone.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
            throw new ArgumentException($"Unknown time zone '{tz}'. Use an IANA name such as Asia/Beirut.");
        var client = await _repository.GetForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Client not found.");
        client.Name = update.Name.Trim();
        client.Phone = Clean(update.Phone);
        client.Email = Clean(update.Email);
        client.Website = Clean(update.Website);
        client.CountryCode = Clean(update.CountryCode)?.ToUpperInvariant();
        client.Timezone = tz;
        client.Address = Clean(update.Address);
        client.OperatingHours = Clean(update.OperatingHours);
        client.BookingInfo = Clean(update.BookingInfo);
        client.UpdatedAt = _time.GetUtcNow();
        await _unitOfWork.SaveChangesAsync(ct);
        return new PlatformAdminChange(id);
    }

    public async Task<PlatformAdminChange> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var client = await _repository.GetForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Client not found.");
        if (client.IsActive == active) return new PlatformAdminChange(id);
        client.IsActive = active;
        client.UpdatedAt = _time.GetUtcNow();
        await _unitOfWork.SaveChangesAsync(ct);
        // Who may use the app is cached per user (UserAccess) and needs an active client: drop it so the client's people are
        // signed out on their next request (or let back in). Rare, so the whole prefix rather than a per-user lookup.
        await _cache.RemoveByPrefixAsync(CacheKeys.MembershipPrefix, ct);
        return new PlatformAdminChange(id);
    }

    public Task<InboxSettingsDto> GetInboxSettingsAsync(Guid id, CancellationToken ct) => _inboxSettings.GetAsync(id, ct);

    public async Task<PlatformAdminChange> UpdateAccountAiAsync(Guid id, Guid channelIntegrationId, UpdateAccountAiRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentException("Send aiEnabled, botId, aiBotNewConversations and aiBotReopenedConversations."); // text-ok: read by the admin portal's developer
        await _inboxSettings.UpdateAccountAsync(id, channelIntegrationId, request, ct);
        return new PlatformAdminChange(id);
    }

    public async Task<ClientCreated> CreateAsync(CreateClientRequest request, string requestBaseUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = Clean(request.OwnerName);
        if (name?.Length > 200) throw new ArgumentException("members.errors.nameTooLong").WithArgs(200);
        var opened = await _registration.OpenForOwnerAsync(request.Name, request.BusinessTypeCode, request.Language, request.OwnerEmail,
            request.PlanCode, ct);
        var client = opened.Client;
        try
        {
            var invite = await _invites.InviteOwnerAsync(client.Id, request.OwnerEmail, name, requestBaseUrl, ct);
            return new ClientCreated(client.Id, client.Slug, opened.PlanCode, opened.PlanStarted, true, invite.ExpiresAt);
        }
        catch (EmailSendException ex)
        {
            // The client is there; the portal offers "Send the invite again".
            _logger.LogWarning(ex, "Client {ClientId} opened, but the owner invite email couldn't go.", client.Id);
            return new ClientCreated(client.Id, client.Slug, opened.PlanCode, opened.PlanStarted, false, null);
        }
    }

    public async Task<OwnerInviteSent> ResendOwnerInviteAsync(Guid id, string requestBaseUrl, CancellationToken ct)
    {
        var invite = await _invites.ResendOwnerInviteAsync(id, requestBaseUrl, ct);
        return new OwnerInviteSent(id, invite.Email, invite.ExpiresAt);
    }

    public async Task<PlatformAdminChange> SetDefaultLanguageAsync(Guid id, string? language, CancellationToken ct)
    {
        await _profile.SetDefaultLanguageAsync(id, language, ct);
        return new PlatformAdminChange(id);
    }

    public Task<BusinessTypeChangePreview> PreviewBusinessTypeChangeAsync(Guid id, string? toCode, CancellationToken ct) =>
        _businessTypes.PreviewClientChangeAsync(id, toCode, ct);

    public async Task<PlatformAdminChange> ChangeBusinessTypeAsync(Guid id, string? toCode, CancellationToken ct)
    {
        await _businessTypes.ChangeClientTypeAsync(id, toCode, ct);
        return new PlatformAdminChange(id);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
