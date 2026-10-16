using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Exceptions;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Helpers;

namespace Omni.App.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _db;

    public UnitOfWork(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new DuplicateRecordException(ex);
        }
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
        new UnitOfWorkTransaction(await _db.Database.BeginTransactionAsync(ct));

    public void DiscardChanges() => _db.ChangeTracker.Clear();

    public bool IsDuplicateRecord(Exception exception) =>
        exception is DuplicateRecordException || (exception is DbUpdateException ex && DbErrors.IsUniqueViolation(ex));
}
