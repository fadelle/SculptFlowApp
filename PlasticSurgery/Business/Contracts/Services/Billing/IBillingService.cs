using Microsoft.Extensions.Options;
using PlasticSurgery.Entities.Dtos.Billing;

namespace PlasticSurgery.Business.Contracts.Services.Billing;

/// <summary>
/// The billing core: usage recording, rating, reservation, settlement, release, refunds and wallet top-ups/adjustments,
/// for any channel. It knows nothing about WhatsApp/SMS/... — channels hand it normalized <see cref="BillableEvent"/>s
/// (see IChannelBillingPolicy / MessageBillingService), already carrying who pays the provider and whether SculptFlow
/// charges for the usage.
///
///   SculptFlow charges it:  Reserve (rate it, hold funds) ──> Settle (charge: included credit first, then wallet)
///                                                         └─> Release (not billable after all: hold returned)
///   SculptFlow doesn't:     Record (usage + provider cost where known; no rate needed, no hold, no wallet, no ledger)
///                           ──> Settle / Release only update the provider outcome
///
/// Prepaid: a reservation needs included credit + wallet - already reserved >= its amount. Every operation is one
/// transaction holding the clinic's billing account row lock, and is idempotent by key (see IBillingUnitOfWork).
/// </summary>
public interface IBillingService
{
    /// <summary>Records the event. When SculptFlow charges it (<see cref="BillableEvent.ChargesUsage"/>): rates it
    /// and holds its price; Failed (nothing held) when there's no rate or not enough balance — the failure is recorded
    /// as its own usage row. When it doesn't: records it as not charged (Recorded), with the provider cost when a rate
    /// knows it; never fails for a missing rate. A key seen before returns that record unchanged.</summary>
    Task<UsageResult> ReserveAsync(BillableEvent billableEvent, CancellationToken ct = default);

    /// <summary>The usage became billable at the provider. If SculptFlow reserved for it: charges it (at the reserved
    /// unit price, for finalQuantity units when given, else the reserved quantity) and returns any unused hold. If it
    /// isn't charged: only records the outcome (and final quantity). Settling twice is a no-op; settling a released
    /// record does nothing (Conflict).</summary>
    Task<UsageResult> SettleAsync(Guid clinicId, string idempotencyKey, decimal? finalQuantity = null, CancellationToken ct = default);

    /// <summary>The usage turned out not billable at the provider: returns the hold (or, for uncharged usage, just
    /// records the outcome). Idempotent; never undoes a settlement.</summary>
    Task<UsageResult> ReleaseAsync(Guid clinicId, string idempotencyKey, string reason, CancellationToken ct = default);

    /// <summary>Records and settles in one step, for usage that is billable the moment it is known (no hold first).
    /// Charged usage is still prepaid: fails when the balance can't cover it.</summary>
    Task<UsageResult> ChargeAsync(BillableEvent billableEvent, CancellationToken ct = default);

    /// <summary>Gives a settled usage's full amount back to where it was paid from (credit and/or wallet). Once only.</summary>
    Task<UsageResult> RefundAsync(Guid clinicId, Guid usageRecordId, string reason, string source, string? actor, CancellationToken ct = default);

    Task<LedgerResult> TopUpAsync(WalletTopUp request, CancellationToken ct = default);

    /// <summary>Manual correction with a mandatory reason. A debit can't take the balance or the spendable amount below zero.</summary>
    Task<LedgerResult> AdjustAsync(WalletAdjustment request, CancellationToken ct = default);
}
