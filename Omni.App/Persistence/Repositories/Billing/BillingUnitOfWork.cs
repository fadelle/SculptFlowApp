using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Omni.App.Common.Exceptions;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Billing;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.Clinics;
using Omni.App.Persistence.Contracts.Events;
using Omni.App.Persistence.Contracts.Inbox;
using Omni.App.Persistence.Helpers;
using Omni.App.Persistence.Repositories.Channels;
using Omni.App.Persistence.Repositories.Clinics;
using Omni.App.Persistence.Repositories.Events;
using Omni.App.Persistence.Repositories.Inbox;

using Omni.App.Business.Contracts.Managers;

namespace Omni.App.Persistence.Repositories.Billing;

public sealed class BillingUnitOfWork : IBillingUnitOfWork
{
    private readonly ApplicationDbContext _db;

    public BillingUnitOfWork(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        Accounts = new BillingAccountRepository(db);
        Usage = new BillingUsageRepository(db);
        Ledger = new BillingLedgerRepository(db);
        Plans = new BillingPlanRepository(db);
        RateCards = new BillingRateCardRepository(db, cache);
        Subscriptions = new ClinicSubscriptionRepository(db);
        ChannelBilling = new ChannelAccountBillingRepository(db, cache);
        Channels = new ChannelIntegrationRepository(db, cache);
        Clinics = new ClinicRepository(db, cache);
        Messages = new MessageRepository(db);
        Events = new EventLogRepository(db);
    }

    public IBillingAccountRepository Accounts { get; }
    public IBillingUsageRepository Usage { get; }
    public IBillingLedgerRepository Ledger { get; }
    public IBillingPlanRepository Plans { get; }
    public IBillingRateCardRepository RateCards { get; }
    public IClinicSubscriptionRepository Subscriptions { get; }
    public IChannelAccountBillingRepository ChannelBilling { get; }
    public IChannelIntegrationRepository Channels { get; }
    public IClinicRepository Clinics { get; }
    public IMessageRepository Messages { get; }
    public IEventLogRepository Events { get; }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
        new UnitOfWorkTransaction(await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct));

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new DuplicateRecordException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            throw new OverlappingRecordException(ex);
        }
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}
