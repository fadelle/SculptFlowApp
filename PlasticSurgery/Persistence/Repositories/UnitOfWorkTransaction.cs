using Microsoft.EntityFrameworkCore.Storage;
using PlasticSurgery.Persistence.Contracts;

namespace PlasticSurgery.Persistence.Repositories;

public sealed class UnitOfWorkTransaction : IUnitOfWorkTransaction
{
    private readonly IDbContextTransaction _transaction;

    public UnitOfWorkTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public Task CommitAsync(CancellationToken ct = default) => _transaction.CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct = default) => _transaction.RollbackAsync(ct);

    public ValueTask DisposeAsync() => _transaction.DisposeAsync();
}
