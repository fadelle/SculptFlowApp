namespace PlasticSurgery.Common.Exceptions;

/// <summary>
/// A save was rejected by a unique index: another request already inserted the same row (webhook retries and races
/// end up here). Thrown by IUnitOfWork.SaveChangesAsync so callers never need to know the database's error codes.
/// </summary>
public class DuplicateRecordException : Exception
{
    public DuplicateRecordException(Exception innerException)
        : base("The record already exists.", innerException)
    {
    }
}
