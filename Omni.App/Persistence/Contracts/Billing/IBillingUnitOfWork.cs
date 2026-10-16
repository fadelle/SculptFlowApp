using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Events;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Persistence.Contracts.Billing;

/// <summary>
/// One billing operation's own unit of work. Every repository here shares this unit's context. Money operations:
///   1. open a READ COMMITTED transaction (<see cref="BeginTransactionAsync"/>),
///   2. lock the client's billing account (<see cref="IBillingAccountRepository.LockAsync"/>) — this serializes all money
///      operations of one client, so two workers can never spend the same balance,
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
    IClientSubscriptionRepository Subscriptions { get; }
    IChannelAccountBillingRepository ChannelBilling { get; }
    IChannelIntegrationRepository Channels { get; }
    IClientRepository Clients { get; }
    IMessageRepository Messages { get; }
    IEventLogRepository Events { get; }

    /// <summary>A READ COMMITTED transaction on this unit's context.</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Writes this unit's pending changes. A unique-index violation is thrown as DuplicateRecordException and an
    /// exclusion-constraint violation as OverlappingRecordException.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
