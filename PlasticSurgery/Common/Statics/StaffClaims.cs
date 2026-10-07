namespace PlasticSurgery.Common.Statics;

/// <summary>Identity claim types the main app stores on staff users (identity_user_claims).</summary>
public static class StaffClaims
{
    /// <summary>The user's full name (set at registration by ClinicRegistrationService).</summary>
    public const string FullName = "full_name";
}
