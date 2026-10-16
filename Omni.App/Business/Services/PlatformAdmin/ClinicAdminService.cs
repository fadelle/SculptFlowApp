using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Business.Mappers.PlatformAdmin;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Entities.Requests.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Services.PlatformAdmin;

public class ClinicAdminService : IClinicAdminService
{
    private readonly IClinicAdminRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IInboxSettingsService _inboxSettings;

    public ClinicAdminService(IClinicAdminRepository repository, IUnitOfWork unitOfWork, TimeProvider time, IInboxSettingsService inboxSettings)
    {
        _inboxSettings = inboxSettings;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public Task<PagedResponse<ClinicRow>> ListAsync(string? search, bool? active, int page, CancellationToken ct) =>
        _repository.ListAsync(search, active, page, ct);

    public Task<List<ClinicOption>> OptionsAsync(CancellationToken ct) => _repository.OptionsAsync(ct);

    public async Task<ClinicDetail?> GetAsync(Guid id, CancellationToken ct) =>
        await _repository.GetAsync(id, ct) is { } clinic ? PlatformAdminMapper.ToDetail(clinic) : null;

    public Task<ClinicCounts> CountsAsync(Guid id, CancellationToken ct) => _repository.CountsAsync(id, ct);

    public async Task<PlatformAdminChange> UpdateAsync(Guid id, ClinicUpdate update, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(update.Name)) throw new ArgumentException("Clinic name is required.");
        var tz = string.IsNullOrWhiteSpace(update.Timezone) ? "UTC" : update.Timezone.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
            throw new ArgumentException($"Unknown time zone '{tz}'. Use an IANA name such as Asia/Beirut.");
        var clinic = await _repository.GetForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Clinic not found.");
        clinic.Name = update.Name.Trim();
        clinic.Phone = Clean(update.Phone);
        clinic.Email = Clean(update.Email);
        clinic.Website = Clean(update.Website);
        clinic.CountryCode = Clean(update.CountryCode)?.ToUpperInvariant();
        clinic.Timezone = tz;
        clinic.Address = Clean(update.Address);
        clinic.OperatingHours = Clean(update.OperatingHours);
        clinic.ConsultationInfo = Clean(update.ConsultationInfo);
        clinic.UpdatedAt = _time.GetUtcNow();
        await _unitOfWork.SaveChangesAsync(ct);
        return new PlatformAdminChange(id);
    }

    public async Task<PlatformAdminChange> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var clinic = await _repository.GetForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Clinic not found.");
        if (clinic.IsActive == active) return new PlatformAdminChange(id);
        clinic.IsActive = active;
        clinic.UpdatedAt = _time.GetUtcNow();
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

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
