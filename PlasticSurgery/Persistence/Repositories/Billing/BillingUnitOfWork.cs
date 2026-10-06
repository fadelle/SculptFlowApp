using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Billing;
using PlasticSurgery.Persistence.Contracts.Channels;
using PlasticSurgery.Persistence.Contracts.Clinics;
using PlasticSurgery.Persistence.Contracts.Events;
using PlasticSurgery.Persistence.Contracts.Inbox;
using PlasticSurgery.Persistence.Helpers;
using PlasticSurgery.Persistence.Repositories.Channels;
using PlasticSurgery.Persistence.Repositories.Clinics;
using PlasticSurgery.Persistence.Repositories.Events;
using PlasticSurgery.Persistence.Repositories.Inbox;

namespace PlasticSurgery.Persistence.Repositories.Billing;

public sealed class BillingUnitOfWork : IBillingUnitOfWork
{
    private readonly ApplicationDbContext _db;

    public BillingUnitOfWork(ApplicationDbContext db)
    {
        _db = db;
        Accounts = new BillingAccountRepository(db);
        Usage = new BillingUsageRepository(db);
        Ledger = new BillingLedgerRepository(db);
        Plans = new BillingPlanRepository(db);
        RateCards = new BillingRateCardRepository(db);
        Subscriptions = new ClinicSubscriptionRepository(db);
        ChannelBilling = new ChannelAccountBillingRepository(db);
        Channels = new ChannelIntegrationRepository(db);
        Clinics = new ClinicRepository(db);
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
