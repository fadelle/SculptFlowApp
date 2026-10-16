using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Procedures;

namespace Omni.App.Persistence.Repositories.Procedures;

public class ProcedureBookingRepository : IProcedureBookingRepository
{
    private readonly ApplicationDbContext _db;

    public ProcedureBookingRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<ProcedureBooking?> GetAsync(Guid clientId, Guid bookingId, CancellationToken ct = default) =>
        _db.ProcedureBookings.FirstOrDefaultAsync(b => b.ClientId == clientId && b.Id == bookingId, ct);

    public async Task<(IReadOnlyList<ProcedureBooking> Items, int TotalCount)> ListAsync(Guid clientId, string? status,
        int skip, int take, CancellationToken ct = default)
    {
        var query = _db.ProcedureBookings
            .Include(b => b.Lead)
            .Include(b => b.Procedure)
            .Where(b => b.ClientId == clientId);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(b => b.Status == status);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(b => b.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount);
    }

    public void Add(ProcedureBooking booking) => _db.ProcedureBookings.Add(booking);

    public async Task LoadDetailsAsync(ProcedureBooking booking, CancellationToken ct = default)
    {
        await _db.Entry(booking).Reference(x => x.Lead).LoadAsync(ct);
        await _db.Entry(booking).Reference(x => x.Procedure).LoadAsync(ct);
    }
}
