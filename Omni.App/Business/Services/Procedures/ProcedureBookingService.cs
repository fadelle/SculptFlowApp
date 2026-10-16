using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Procedures;
using Omni.App.Business.Mappers.Procedures;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Procedures;
using Omni.App.Entities.Responses.Procedures;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Leads;
using Omni.App.Persistence.Contracts.Procedures;

namespace Omni.App.Business.Services.Procedures;

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
        // A new procedure booking may only reference one of this client's ACTIVE procedures.
        await _procedures.EnsureUsableAsync(request.ClientId, request.ProcedureId, ct);
        if (!await _leads.ExistsAsync(request.ClientId, request.LeadId, ct))
        {
            throw new ArgumentException("Lead not found for this client.");
        }

        var booking = new ProcedureBooking
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
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

    public async Task<ProcedureBookingResponse?> UpdateAsync(Guid clientId, Guid id, UpdateProcedureBookingRequest request, CancellationToken ct = default)
    {
        var booking = await _bookings.GetAsync(clientId, id, ct);
        if (booking is null) return null;

        var previousStatus = booking.Status;
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

        // The surgery counts as booked from the moment the booking is confirmed (booked, deposit paid or completed), once:
        // moving on from booked to completed must not log a second procedure_booked event.
        if (IsConfirmed(request.Status) && !IsConfirmed(previousStatus))
        {
            var lead = await _leads.GetByIdAsync(booking.LeadId, ct);
            if (lead is not null)
            {
                lead.Status = LeadStatus.SurgeryBooked;
                lead.UpdatedAt = DateTimeOffset.UtcNow;
            }

            _events.Log(clientId, EventTypes.ProcedureBooked, leadId: booking.LeadId,
                metadataJson: $"{{\"amount\": {booking.FinalAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}}}");
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(booking, ct);
    }

    private static bool IsConfirmed(string? status) =>
        status is ProcedureBookingStatus.Booked or ProcedureBookingStatus.DepositPaid or ProcedureBookingStatus.Completed;

    public async Task<(IReadOnlyList<ProcedureBookingResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? status, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount) = await _bookings.ListAsync(clientId, status, skip, take, ct);
        return (items.Select(ProcedureMapper.ToResponse).ToList(), totalCount);
    }

    private async Task<ProcedureBookingResponse> ToResponseAsync(ProcedureBooking b, CancellationToken ct)
    {
        await _bookings.LoadDetailsAsync(b, ct);
        return ProcedureMapper.ToResponse(b);
    }
}
