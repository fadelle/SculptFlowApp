namespace Omni.App.Common.Exceptions;

/// <summary>The client's subscription doesn't allow this (no active plan, feature not in the plan, limit reached).
/// Also an InvalidOperationException, for the same 422 mapping.</summary>
public class EntitlementDeniedException : InvalidOperationException
{
    /// <summary>The entitlement key that was checked, or "subscription" when there's no active plan at all.</summary>
    public string Entitlement { get; }

    // Strings-file keys of the refusals ([ApiErrors] words them in the request's language).
    public const string InactiveSendKey = "billing.errors.inactiveSend";
    public const string NotIncludedKey = "billing.errors.notIncluded";
    public const string InactiveFeatureKey = "billing.errors.inactiveFeature";
    public const string LimitReachedKey = "billing.errors.limitReached";
    public const string InactiveKey = "billing.errors.inactive";

    public EntitlementDeniedException(string entitlement, string message) : base(message)
    {
        Entitlement = entitlement;
    }
}
