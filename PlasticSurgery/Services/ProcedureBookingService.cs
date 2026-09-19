using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class ProcedureBookingService : IProcedureBookingService
{
    private readonly ApplicationDbContext _db;
    private readonly IEventLogger _events;
    private readonly IProcedureService _procedures;

    public ProcedureBookingService(ApplicationDbContext db, IEventLogger events, IProcedureService procedures)
    {
        _db = db;
        _events = events;
        _procedures = procedures;
    }

    public async Task<ProcedureBookingResponse> CreateAsync(CreateProcedureBookingRequest request, CancellationToken ct = default)
    {
        // A new procedure booking may only reference one of this clinic's ACTIVE procedures.
        await _procedures.EnsureUsableAsync(request.ClinicId, request.ProcedureId, ct);

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

        _db.ProcedureBookings.Add(booking);
        await _db.SaveChangesAsync(ct);

        return await ToResponseAsync(booking, ct);
    }

    public async Task<ProcedureBookingResponse?> UpdateAsync(Guid clinicId, Guid id, UpdateProcedureBookingRequest request, CancellationToken ct = default)
    {
        var booking = await _db.ProcedureBookings.FirstOrDefaultAsync(b => b.ClinicId == clinicId && b.Id == id, ct);
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
            var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == booking.LeadId, ct);
            if (lead is not null)
            {
                lead.Status = LeadStatus.SurgeryBooked;
                lead.UpdatedAt = DateTimeOffset.UtcNow;
            }

            _events.Log(clinicId, EventTypes.ProcedureBooked, leadId: booking.LeadId,
                metadataJson: $"{{\"amount\": {booking.FinalAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}}}");
        }

        await _db.SaveChangesAsync(ct);
        return await ToResponseAsync(booking, ct);
    }

    public async Task<(IReadOnlyList<ProcedureBookingResponse> Items, int TotalCount)> ListAsync(
        Guid clinicId, string? status, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.ProcedureBookings
            .Include(b => b.Lead)
            .Include(b => b.Procedure)
            .Where(b => b.ClinicId == clinicId);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(b => b.Status == status);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(b => b.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);

        return (items.Select(ToResponse).ToList(), totalCount);
    }

    private async Task<ProcedureBookingResponse> ToResponseAsync(ProcedureBooking b, CancellationToken ct)
    {
        await _db.Entry(b).Reference(x => x.Lead).LoadAsync(ct);
        await _db.Entry(b).Reference(x => x.Procedure).LoadAsync(ct);
        return ToResponse(b);
    }

    private static ProcedureBookingResponse ToResponse(ProcedureBooking b) => new(
        b.Id, b.ClinicId, b.LeadId, b.Lead?.FullName, b.ProcedureId, b.Procedure?.Name, b.AppointmentId,
        b.Status, b.QuotedAmount, b.DepositAmount, b.FinalAmount, b.CurrencyCode, b.ProcedureDate, b.CreatedAt
    );
}
