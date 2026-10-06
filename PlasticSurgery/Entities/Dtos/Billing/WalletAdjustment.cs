using System.Text.RegularExpressions;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>A manual correction. Reason is required. Amount is signed; BalanceType is wallet or included_credit.</summary>
public sealed record WalletAdjustment(Guid ClinicId, decimal Amount, string BalanceType, string Reason, string IdempotencyKey,
    string Source, string? Actor);
