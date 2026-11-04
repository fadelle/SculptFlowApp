namespace Omni.App.Common.Exceptions;

/// <summary>A save the database refused because the data itself is wrong (QA t1-accounts-02): a reference to a row that
/// doesn't exist (or no longer does), a value a check constraint forbids, or text longer than its column. An ArgumentException,
/// so [ApiErrors] answers 400 and pages show the message, never an error page. Thrown by the unit of work, so callers never
/// need to know the database's error codes.</summary>
public class InvalidRecordException : ArgumentException
{
    /// <summary>A reference to a row that doesn't exist (foreign-key violation).</summary>
    public const string MissingReferenceKey = "common.errors.missingReference";
    /// <summary>A value the database's rules forbid (check violation, too long, out of range).</summary>
    public const string InvalidValueKey = "common.errors.invalidValue";

    public InvalidRecordException(string key, Exception innerException) : base(key, innerException)
    {
    }
}
