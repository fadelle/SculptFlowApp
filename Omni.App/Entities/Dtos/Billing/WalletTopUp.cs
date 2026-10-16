using System.Text.RegularExpressions;

namespace Omni.App.Entities.Dtos.Billing;

public sealed record WalletTopUp(Guid ClientId, decimal Amount, string IdempotencyKey, string? Reference, string? Reason,
    string Source, string? Actor);
