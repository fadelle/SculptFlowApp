namespace PlasticSurgery.Common.Exceptions;

/// <summary>A save was rejected by an exclusion constraint (e.g. two versions of one rate whose effective periods
/// overlap). Thrown by the billing unit of work so callers never need to know the database's error codes.</summary>
public class OverlappingRecordException : Exception
{
    public OverlappingRecordException(Exception innerException)
        : base("The record overlaps an existing one.", innerException)
    {
    }
}
