using System.Text.RegularExpressions;

namespace PlasticSurgery.Common.Enums;

public enum UsageOutcome
{
    /// <summary>Funds are held; settle or release it later.</summary>
    Reserved,
    /// <summary>Charged.</summary>
    Settled,
    /// <summary>Not billable after all; the hold was returned.</summary>
    Released,
    /// <summary>Couldn't be priced or paid; nothing was charged (FailureReason says why).</summary>
    Failed,
    /// <summary>Usage recorded; SculptFlow doesn't charge it (someone else pays the provider, or there's no fee).
    /// No reservation, no wallet or credit use, no ledger row.</summary>
    Recorded,
    /// <summary>No usage record with that key (the usage wasn't billable, or billing was off when it happened).</summary>
    NotFound,
    /// <summary>The record is already final in the other direction (e.g. settle after release) — nothing changed.</summary>
    Conflict
}
