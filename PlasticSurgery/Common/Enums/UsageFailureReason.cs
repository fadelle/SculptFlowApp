namespace PlasticSurgery.Common.Enums;

public static class UsageFailureReason
{
    /// <summary>No rate card had a rate for this event — nothing is guessed, the usage isn't allowed.</summary>
    public const string RateNotFound = "rate_not_found";
    /// <summary>Included credit + wallet minus what's already reserved couldn't cover it.</summary>
    public const string InsufficientFunds = "insufficient_funds";
    /// <summary>The rate is in another currency than the clinic's account.</summary>
    public const string CurrencyMismatch = "currency_mismatch";
}
