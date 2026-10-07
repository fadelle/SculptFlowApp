using System.Text.RegularExpressions;

namespace PlasticSurgery.Entities.Dtos.Billing;

public sealed record WalletTopUp(Guid ClinicId, decimal Amount, string IdempotencyKey, string? Reference, string? Reason,
    string Source, string? Actor);
