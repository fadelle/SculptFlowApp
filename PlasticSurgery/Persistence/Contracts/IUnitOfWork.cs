namespace PlasticSurgery.Persistence.Contracts;

/// <summary>
/// The request's unit of work. Every repository in a request shares one scoped DbContext, so entities added or changed
/// through any repository are written together by the service that owns the operation, never by the repository.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Writes every pending change from all repositories in this request. A unique-index violation is
    /// thrown as DuplicateRecordException.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="SaveChangesAsync"/>, but when a unique index rejects the write (another request inserted the same
    /// row first) it discards this request's pending changes and returns false instead of throwing.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);

    /// <summary>Starts a database transaction that covers every repository call until it is committed.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Forgets every pending (unsaved) change and every tracked entity.</summary>
    void DiscardChanges();

    /// <summary>Is this exception a unique-index rejection (from this unit of work, or from a library such as ASP.NET
    /// Identity saving through the same context)?</summary>
    bool IsDuplicateRecord(Exception exception);
}
