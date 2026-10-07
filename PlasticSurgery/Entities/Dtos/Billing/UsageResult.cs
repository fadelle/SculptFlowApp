using System.Text.RegularExpressions;
using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>Result of a usage operation. Duplicate = the key was already processed and the existing state is
/// returned instead of acting twice.</summary>
public sealed record UsageResult(UsageOutcome Outcome, Guid? UsageRecordId, decimal? Amount, string? FailureReason, bool Duplicate)
{
    public bool Succeeded => Outcome is UsageOutcome.Reserved or UsageOutcome.Settled or UsageOutcome.Recorded;
}
