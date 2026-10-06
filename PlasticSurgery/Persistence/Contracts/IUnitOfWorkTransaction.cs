namespace PlasticSurgery.Persistence.Contracts;

/// <summary>An open database transaction. Disposing it without <see cref="CommitAsync"/> rolls it back.</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
