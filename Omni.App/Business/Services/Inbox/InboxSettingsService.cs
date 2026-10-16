using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clinics;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Business.Services.Inbox;

public class InboxSettingsService : IInboxSettingsService
{
    private readonly IInboxSettingsRepository _settings;
    private readonly IClinicRepository _clinics;
    private readonly IEntitlementService _entitlements;
    private readonly IUnitOfWork _unitOfWork;

    public InboxSettingsService(IInboxSettingsRepository settings, IClinicRepository clinics, IEntitlementService entitlements,
        IUnitOfWork unitOfWork)
    {
        _settings = settings;
        _clinics = clinics;
        _entitlements = entitlements;
        _unitOfWork = unitOfWork;
    }

    public async Task<InboxSettingsDto> GetAsync(Guid clinicId, CancellationToken ct = default)
    {
        // All three reads are cached (inbox settings and clinic table caches, entitlements), so this is cheap per message.
        _ = await _clinics.GetReadOnlyAsync(clinicId, ct) ?? throw new KeyNotFoundException("Clinic not found.");
        var row = await _settings.GetForClinicReadOnlyAsync(clinicId, ct) ?? new InboxSettings();
        var entitlements = await _entitlements.GetAsync(clinicId, ct);
        return new InboxSettingsDto(entitlements.CanUseAiAgent, row.AiBotNewConversations, row.AiBotReopenedConversations);
    }

    public async Task<string> InitialModeAsync(Guid clinicId, CancellationToken ct = default) =>
        (await GetAsync(clinicId, ct)).AiBotAnswersNew ? ConversationMode.Ai : ConversationMode.Human;

    public async Task UpdateAsync(Guid clinicId, UpdateInboxSettingsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await _clinics.GetReadOnlyAsync(clinicId, ct) ?? throw new KeyNotFoundException("Clinic not found.");

        var now = DateTimeOffset.UtcNow;
        var row = await _settings.GetForClinicAsync(clinicId, ct);
        if (row is null)
        {
            row = new InboxSettings { Id = Guid.NewGuid(), ClinicId = clinicId, CreatedAt = now };
            _settings.Add(row);
        }
        else if (row.AiBotNewConversations == request.AiBotNewConversations
                 && row.AiBotReopenedConversations == request.AiBotReopenedConversations)
        {
            return;
        }

        row.AiBotNewConversations = request.AiBotNewConversations;
        row.AiBotReopenedConversations = request.AiBotReopenedConversations;
        row.UpdatedAt = now;
        // Tracked save: CacheInvalidationInterceptor drops the clinic's cached settings. Two first saves racing for the
        // same clinic hit the unique index; the second surfaces as DuplicateRecordException (409).
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
