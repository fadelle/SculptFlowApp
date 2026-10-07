using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Business.Mappers.PlatformAdmin;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Requests.PlatformAdmin;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;

namespace PlasticSurgery.Business.Services.PlatformAdmin;

public class ClinicAdminService : IClinicAdminService
{
    private readonly IClinicAdminRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public ClinicAdminService(IClinicAdminRepository repository, IUnitOfWork unitOfWork, TimeProvider time)
    {
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

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
