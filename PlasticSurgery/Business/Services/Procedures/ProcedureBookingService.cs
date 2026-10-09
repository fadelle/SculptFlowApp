using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Procedures;
using PlasticSurgery.Business.Mappers.Procedures;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Procedures;
using PlasticSurgery.Entities.Responses.Procedures;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Leads;
using PlasticSurgery.Persistence.Contracts.Procedures;

namespace PlasticSurgery.Business.Services.Procedures;

public class ProcedureBookingService : IProcedureBookingService
{
    private readonly IProcedureBookingRepository _bookings;
    private readonly ILeadRepository _leads;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventLogger _events;
    private readonly IProcedureService _procedures;

    public ProcedureBookingService(IProcedureBookingRepository bookings, ILeadRepository leads, IUnitOfWork unitOfWork,
        IEventLogger events, IProcedureService procedures)
    {
        _bookings = bookings;
        _leads = leads;
        _unitOfWork = unitOfWork;
        _events = events;
        _procedures = procedures;
    }

    public async Task<ProcedureBookingResponse> CreateAsync(CreateProcedureBookingRequest request, CancellationToken ct = default)
    {
        // A new procedure booking may only reference one of this clinic's ACTIVE procedures.
        await _procedures.EnsureUsableAsync(request.ClinicId, request.ProcedureId, ct);
        if (!await _leads.ExistsAsync(request.ClinicId, request.LeadId, ct))
        {
            throw new ArgumentException("Lead not found for this clinic.");
        }

        var booking = new ProcedureBooking
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            LeadId = request.LeadId,
            ProcedureId = request.ProcedureId,
            AppointmentId = request.AppointmentId,
            Status = ProcedureBookingStatus.Considering,
            QuotedAmount = request.QuotedAmount,
            CurrencyCode = request.CurrencyCode,
            Notes = request.Notes,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _bookings.Add(booking);
        await _unitOfWork.SaveChangesAsync(ct);

        return await ToResponseAsync(booking, ct);
    }

    public async Task<ProcedureBookingResponse?> UpdateAsync(Guid clinicId, Guid id, UpdateProcedureBookingRequest request, CancellationToken ct = default)
    {
        var booking = await _bookings.GetAsync(clinicId, id, ct);
        if (booking is null) return null;

        if (request.Status is not null)
        {
            if (!ProcedureBookingStatus.All.Contains(request.Status))
            {
                throw new ArgumentException($"Invalid procedure booking status '{request.Status}'.", nameof(request));
            }
            booking.Status = request.Status;
        }

        if (request.QuotedAmount is not null) booking.QuotedAmount = request.QuotedAmount;
        if (request.DepositAmount is not null) booking.DepositAmount = request.DepositAmount;
        if (request.FinalAmount is not null) booking.FinalAmount = request.FinalAmount;
        if (request.CurrencyCode is not null) booking.CurrencyCode = request.CurrencyCode;
        if (request.ProcedureDate is not null) booking.ProcedureDate = request.ProcedureDate;
        if (request.Notes is not null) booking.Notes = request.Notes;

        booking.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.Status == ProcedureBookingStatus.Completed)
        {
            var lead = await _leads.GetByIdAsync(booking.LeadId, ct);
            if (lead is not null)
            {
                lead.Status = LeadStatus.SurgeryBooked;
                lead.UpdatedAt = DateTimeOffset.UtcNow;
            }

            _events.Log(clinicId, EventTypes.ProcedureBooked, leadId: booking.LeadId,
                metadataJson: $"{{\"amount\": {booking.FinalAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}}}");
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(booking, ct);
    }

    public async Task<(IReadOnlyList<ProcedureBookingResponse> Items, int TotalCount)> ListAsync(
        Guid clinicId, string? status, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount) = await _bookings.ListAsync(clinicId, status, skip, take, ct);
        return (items.Select(ProcedureMapper.ToResponse).ToList(), totalCount);
    }

    private async Task<ProcedureBookingResponse> ToResponseAsync(ProcedureBooking b, CancellationToken ct)
    {
        await _bookings.LoadDetailsAsync(b, ct);
        return ProcedureMapper.ToResponse(b);
    }
}
