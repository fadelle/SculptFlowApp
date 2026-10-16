using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Automation;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Clinics;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clinics;
using Omni.App.Persistence.Contracts.Users;

namespace Omni.App.Business.Services.Automation;

public class AutomationCleanupService : IAutomationCleanupService
{
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheManager _cache;

    public AutomationCleanupService(IClinicRepository clinics, IUserRepository users, IUnitOfWork unitOfWork, ICacheManager cache)
    {
        _clinics = clinics;
        _users = users;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public Task<IReadOnlyList<ClinicSummaryRow>> ListClinicsAsync(CancellationToken ct = default) =>
        _clinics.ListByNamePrefixAsync(AutomationClinics.NamePrefix, ct);

    public async Task<AutomationDeleteOutcome> DeleteClinicAsync(Guid clinicId, CancellationToken ct = default)
    {
        var clinic = await _clinics.GetReadOnlyAsync(clinicId, ct);
        if (clinic is null) return AutomationDeleteOutcome.NotFound;

        var userIds = await _clinics.ListMemberUserIdsAsync(clinicId, ct);
        var emails = await _users.ListEmailsAsync(userIds, ct);

        var isAutomationClinic = clinic.Name.StartsWith(AutomationClinics.NamePrefix, StringComparison.Ordinal)
            && emails.Count > 0
            && emails.All(e => e.EndsWith(AutomationClinics.EmailDomain, StringComparison.OrdinalIgnoreCase));
        if (!isAutomationClinic) return AutomationDeleteOutcome.NotAutomationClinic;

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        await _clinics.DeleteAsync(clinicId, ct);
        await _users.DeleteAsync(userIds, ct);
        await tx.CommitAsync(ct);
        await _cache.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, ct);
        await _cache.RemoveAsync(CacheKeys.Entitlements(clinicId), ct);
        return AutomationDeleteOutcome.Deleted;
    }
}
