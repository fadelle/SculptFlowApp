using PlasticSurgery.Persistence.Contracts.Channels;
using PlasticSurgery.Persistence.Contracts.Clinics;
using PlasticSurgery.Persistence.Contracts.Events;
using PlasticSurgery.Persistence.Contracts.Inbox;

namespace PlasticSurgery.Persistence.Contracts.Billing;

/// <summary>
/// One billing operation's own unit of work. Every repository here shares this unit's context. Money operations:
///   1. open a READ COMMITTED transaction (<see cref="BeginTransactionAsync"/>),
///   2. lock the clinic's billing account (<see cref="IBillingAccountRepository.LockAsync"/>) — this serializes all money
///      operations of one clinic, so two workers can never spend the same balance,
///   3. check idempotency (existing usage record / ledger key) AFTER taking the lock,
///   4. change balances only through the ledger (Business/Engines/Billing/BillingLedger),
///   5. commit once at the end. Any exception before the commit rolls everything back.
/// </summary>
public interface IBillingUnitOfWork : IAsyncDisposable
{
    IBillingAccountRepository Accounts { get; }
    IBillingUsageRepository Usage { get; }
    IBillingLedgerRepository Ledger { get; }
    IBillingPlanRepository Plans { get; }
    IBillingRateCardRepository RateCards { get; }
    IClinicSubscriptionRepository Subscriptions { get; }
    IChannelAccountBillingRepository ChannelBilling { get; }
    IChannelIntegrationRepository Channels { get; }
    IClinicRepository Clinics { get; }
    IMessageRepository Messages { get; }
    IEventLogRepository Events { get; }

    /// <summary>A READ COMMITTED transaction on this unit's context.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Writes this unit's pending changes. A unique-index violation is thrown as DuplicateRecordException and an
    /// exclusion-constraint violation as OverlappingRecordException.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
