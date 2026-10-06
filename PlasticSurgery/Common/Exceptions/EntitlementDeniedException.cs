namespace PlasticSurgery.Common.Exceptions;

/// <summary>The clinic's subscription doesn't allow this (no active plan, feature not in the plan, limit reached).
/// Also an InvalidOperationException, for the same 422 mapping.</summary>
public class EntitlementDeniedException : InvalidOperationException
{
    /// <summary>The entitlement key that was checked, or "subscription" when there's no active plan at all.</summary>
    public string Entitlement { get; }

    public EntitlementDeniedException(string entitlement, string message) : base(message)
    {
        Entitlement = entitlement;
    }
}
