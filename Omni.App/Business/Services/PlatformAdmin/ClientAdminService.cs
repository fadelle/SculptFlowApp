using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Contracts.Services.Inbox;
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

    public ClientAdminService(IClientAdminRepository repository, IUnitOfWork unitOfWork, TimeProvider time, IInboxSettingsService inboxSettings,
        IBusinessTypeService businessTypes)
    {
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
        return new PlatformAdminChange(id);
    }

    public Task<InboxSettingsDto> GetInboxSettingsAsync(Guid id, CancellationToken ct) => _inboxSettings.GetAsync(id, ct);

    public async Task<PlatformAdminChange> UpdateInboxSettingsAsync(Guid id, UpdateInboxSettingsRequest request, CancellationToken ct)
    {
        if (request is null) throw new ArgumentException("Send newConversations and reopenedConversations.");
        await _inboxSettings.UpdateAsync(id, request, ct);
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
