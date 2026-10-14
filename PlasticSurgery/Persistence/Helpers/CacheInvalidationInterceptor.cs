using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Caching;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Helpers;

/// <summary>
/// Singleton, added to every ApplicationDbContext (the request's and each billing unit of work's). Before a SaveChanges
/// it collects the cache keys of every tracked row being added, changed or deleted (CacheKeys.EvictionsFor, with the
/// row's current and original values), and removes them once the save succeeds. Inside a transaction it removes them
/// again when the transaction commits or rolls back, so a read that cached the old row between the save and the commit
/// (or this request's own uncommitted row) doesn't stay cached. Set-based statements (ExecuteUpdate/ExecuteDelete) skip
/// the change tracker: their repository methods remove their own keys.
/// </summary>
public sealed class CacheInvalidationInterceptor : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly ICacheManager _cache;
    // Per context: evictions collected before the save, and those still owed to the open transaction.
    private readonly ConditionalWeakTable<DbContext, State> _states = new();

    public CacheInvalidationInterceptor(ICacheManager cache)
    {
        _cache = cache;
    }

    // ---- SaveChanges

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AfterSaveAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        await AfterSaveAsync(eventData.Context, cancellationToken);
        return result;
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Discard(eventData.Context);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    public void SaveChangesCanceled(DbContextEventData eventData) => Discard(eventData.Context);

    public Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    // ---- Transactions

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        FlushTransactionAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        FlushTransactionAsync(eventData.Context, cancellationToken);

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        FlushTransactionAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        FlushTransactionAsync(eventData.Context, cancellationToken);

    // ------------------------------------------------------------------------------------------------------------

    private void Collect(DbContext? context)
    {
        if (context is null) return;
        var state = _states.GetOrCreateValue(context);
        var tracked = context.ChangeTracker.Entries().ToList();
        foreach (var entry in tracked)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            state.Pending.UnionWith(EvictionsFor(entry, tracked));
        }
    }

    private static IEnumerable<CacheEviction> EvictionsFor(EntityEntry entry, List<EntityEntry> tracked)
    {
        // A calendar list row doesn't carry its clinic: take it from its tracked connection when that is loaded.
        if (entry.Entity is CalendarIntegrationCalendar calendar)
        {
            var integration = tracked.Select(e => e.Entity).OfType<CalendarIntegration>()
                .FirstOrDefault(i => i.Id == calendar.CalendarIntegrationId);
            if (integration is not null) return [CacheEviction.Key(CacheKeys.Calendars(integration.ClinicId))];
        }

        var current = CacheKeys.EvictionsFor(entry.Entity);
        return entry.State == EntityState.Added
            ? current
            : current.Concat(CacheKeys.EvictionsFor(entry.OriginalValues.ToObject()));
    }

    private async Task AfterSaveAsync(DbContext? context, CancellationToken ct)
    {
        if (context is null || !_states.TryGetValue(context, out var state) || state.Pending.Count == 0) return;
        var evictions = state.Pending.ToList();
        state.Pending.Clear();
        if (context.Database.CurrentTransaction is not null) state.InTransaction.UnionWith(evictions);
        await _cache.RemoveAsync(evictions, ct);
    }

    private async Task FlushTransactionAsync(DbContext? context, CancellationToken ct)
    {
        if (context is null || !_states.TryGetValue(context, out var state) || state.InTransaction.Count == 0) return;
        var evictions = state.InTransaction.ToList();
        state.InTransaction.Clear();
        await _cache.RemoveAsync(evictions, ct);
    }

    private void Discard(DbContext? context)
    {
        if (context is not null && _states.TryGetValue(context, out var state)) state.Pending.Clear();
    }

    private sealed class State
    {
        public HashSet<CacheEviction> Pending { get; } = new();
        public HashSet<CacheEviction> InTransaction { get; } = new();
    }
}
