namespace Omni.App.Common.Exceptions;

/// <summary>
/// A save was rejected by a unique index: another request already inserted the same row (webhook retries and races
/// end up here). Thrown by IUnitOfWork.SaveChangesAsync so callers never need to know the database's error codes.
/// </summary>
public class DuplicateRecordException : Exception
{
    /// <summary>Strings-file key of the message when the database refused the save.</summary>
    public const string DefaultKey = "common.errors.duplicate";

    public DuplicateRecordException(Exception innerException)
        : base(DefaultKey, innerException)
    {
    }

    /// <summary>A duplicate a service spotted before saving (the message may be a strings-file key, see TextError).</summary>
    public DuplicateRecordException(string message)
        : base(message)
    {
    }
}
