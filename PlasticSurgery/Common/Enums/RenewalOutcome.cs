using Microsoft.Extensions.Options;

namespace PlasticSurgery.Common.Enums;

public enum RenewalOutcome
{
    /// <summary>Nothing was due.</summary>
    NotDue,
    Renewed,
    /// <summary>The renewal couldn't be paid; the subscription is (still) past due, within its grace period.</summary>
    PastDue,
    Expired,
    Cancelled
}
