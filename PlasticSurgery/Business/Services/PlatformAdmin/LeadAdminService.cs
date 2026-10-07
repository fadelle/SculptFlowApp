using PlasticSurgery.Business.Contracts.Services.Appointments;
using PlasticSurgery.Business.Contracts.Services.Inbox;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Business.Mappers.PlatformAdmin;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Requests.PlatformAdmin;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;

namespace PlasticSurgery.Business.Services.PlatformAdmin;

public class LeadAdminService : ILeadAdminService
{
    private readonly ILeadAdminRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationService _conversations;
    private readonly IAppointmentService _appointments;
    private readonly TimeProvider _time;

    public LeadAdminService(ILeadAdminRepository repository, IUnitOfWork unitOfWork, IConversationService conversations,
        IAppointmentService appointments, TimeProvider time)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _conversations = conversations;
        _appointments = appointments;
        _time = time;
    }

    public Task<PagedResponse<LeadRow>> ListLeadsAsync(Guid? clinicId, string? status, string? search, int page, CancellationToken ct) =>
        _repository.ListLeadsAsync(clinicId, status, search, page, ct);

    public async Task<LeadDetail?> GetLeadAsync(Guid id, CancellationToken ct) =>
        await _repository.GetLeadAsync(id, ct) is { } lead ? PlatformAdminMapper.ToDetail(lead) : null;

    public async Task<List<LeadEventRow>> LeadEventsAsync(Guid leadId, CancellationToken ct) =>
        (await _repository.LeadEventsAsync(leadId, ct)).Select(PlatformAdminMapper.ToRow).ToList();

    public Task<PagedResponse<ConversationRow>> ListConversationsAsync(Guid? clinicId, Guid? leadId, string? channel, string? mode,
        string? status, int page, CancellationToken ct) =>
        _repository.ListConversationsAsync(clinicId, leadId, channel, mode, status, page, ct);

    public async Task<ConversationDetail?> GetConversationAsync(Guid id, CancellationToken ct) =>
        await _repository.GetConversationAsync(id, ct) is { } c ? PlatformAdminMapper.ToDetail(c, _time.GetUtcNow()) : null;

    public async Task<List<MessageDetail>> MessagesAsync(Guid conversationId, int take, CancellationToken ct) =>
        (await _repository.MessagesAsync(conversationId, Math.Clamp(take, 1, 1000), ct)).Select(PlatformAdminMapper.ToDetail).ToList();

    public Task<PagedResponse<MessageRow>> ListMessagesAsync(Guid? clinicId, bool failedOnly, string? sender, string? search, int page,
        CancellationToken ct) =>
        _repository.ListMessagesAsync(clinicId, failedOnly, sender, search, page, ct);

    public Task<PagedResponse<AppointmentRow>> ListAppointmentsAsync(Guid? clinicId, Guid? leadId, string? status, bool upcomingOnly,
        int page, CancellationToken ct) =>
        _repository.ListAppointmentsAsync(clinicId, leadId, status, upcomingOnly, page, ct);

    public async Task<PlatformAdminChange> UpdateLeadAsync(Guid id, LeadUpdate update, CancellationToken ct)
    {
        if (!LeadStatus.All.Contains(update.Status)) throw new ArgumentException("Unknown lead status.");
        if (!LeadQualificationStatus.All.Contains(update.QualificationStatus)) throw new ArgumentException("Unknown qualification.");
        var lead = await _repository.GetLeadForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Lead not found.");
        var now = _time.GetUtcNow();
        lead.Status = update.Status;
        lead.QualificationStatus = update.QualificationStatus;
        if (lead.MarketingOptIn && !update.MarketingOptIn) lead.OptedOutAt = now;
        if (!lead.MarketingOptIn && update.MarketingOptIn) lead.OptedOutAt = null;
        lead.MarketingOptIn = update.MarketingOptIn;
        lead.Notes = string.IsNullOrWhiteSpace(update.Notes) ? null : update.Notes.Trim();
        lead.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
        return new PlatformAdminChange(lead.ClinicId);
    }

    public async Task<PlatformAdminChange> SetModeAsync(Guid id, string mode, CancellationToken ct)
    {
        if (mode != ConversationMode.Ai && mode != ConversationMode.Human) throw new ArgumentException("Mode must be ai or human.");
        var conversation = await _repository.GetConversationAsync(id, ct) ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.Mode != mode)
        {
            _ = mode == ConversationMode.Human
                ? await _conversations.TakeOverAsync(conversation.ClinicId, id, ct)
                : await _conversations.ReturnToAiAsync(conversation.ClinicId, id, ct);
        }
        return new PlatformAdminChange(conversation.ClinicId);
    }

    public async Task<PlatformAdminChange> SetStatusAsync(Guid id, string status, CancellationToken ct)
    {
        if (status is not (ConversationStatus.Active or ConversationStatus.Closed or ConversationStatus.Archived))
            throw new ArgumentException("Unknown conversation status.");
        var conversation = await _repository.GetConversationForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.Status == status) return new PlatformAdminChange(conversation.ClinicId);
        conversation.Status = status;
        conversation.UpdatedAt = _time.GetUtcNow();
        await _unitOfWork.SaveChangesAsync(ct);
        return new PlatformAdminChange(conversation.ClinicId);
    }

    public async Task<PlatformAdminChange> SetAppointmentStatusAsync(Guid id, string status, CancellationToken ct)
    {
        var appointment = await _repository.GetAppointmentForUpdateAsync(id, ct) ?? throw new KeyNotFoundException("Appointment not found.");
        // The clinic's own path: validates the status and pushes the change to a connected calendar.
        if (appointment.Status != status) await _appointments.UpdateStatusAsync(appointment.ClinicId, id, status, ct);
        return new PlatformAdminChange(appointment.ClinicId);
    }
}
