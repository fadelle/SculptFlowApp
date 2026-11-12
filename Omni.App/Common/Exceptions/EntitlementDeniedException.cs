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

    public const string AdminInactiveSendKey = "billing.errors.admin.inactiveSend";
    public const string AdminNotIncludedKey = "billing.errors.admin.notIncluded";
    public const string AdminInactiveFeatureKey = "billing.errors.admin.inactiveFeature";
    public const string AdminLimitReachedKey = "billing.errors.admin.limitReached";
    public const string AdminInactiveKey = "billing.errors.admin.inactive";

    /// <summary>The same refusal in an admin's words, for the platform-admin API (QA t6-admin-19: "your plan ... See Settings →
    /// Billing" — the admin has no Settings page and it isn't their plan). Null for a key without an admin wording.</summary>
    public static string? AdminKeyFor(string key) => key switch
    {
        InactiveSendKey => AdminInactiveSendKey,
        NotIncludedKey => AdminNotIncludedKey,
        InactiveFeatureKey => AdminInactiveFeatureKey,
        LimitReachedKey => AdminLimitReachedKey,
        InactiveKey => AdminInactiveKey,
        _ => null
    };

    public EntitlementDeniedException(string entitlement, string message) : base(message)
    {
        Entitlement = entitlement;
    }
}
