namespace Omni.App.Common.Exceptions;

/// <summary>A save was rejected by an exclusion constraint (e.g. two versions of one rate whose effective periods
/// overlap). Thrown by the billing unit of work so callers never need to know the database's error codes.</summary>
public class OverlappingRecordException : Exception
{
    /// <summary>Strings-file key of the message.</summary>
    public const string DefaultKey = "common.errors.overlap";

    /// <summary>Refused before saving, with a strings-file key (e.g. a booking on top of another, QA t5-ops-01).</summary>
    public OverlappingRecordException(string key) : base(key)
    {
    }

    public OverlappingRecordException(Exception innerException)
        : base(DefaultKey, innerException)
    {
    }
}
