using System.Text.RegularExpressions;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>Result of a direct balance change (top-up, adjustment).</summary>
public sealed record LedgerResult(Guid LedgerEntryId, decimal BalanceAfter, bool Duplicate);
