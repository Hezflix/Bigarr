using System.Collections.Concurrent;
using System.Data.Common;
using Bigarr.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bigarr.Api.Infrastructure.Events;

/// <summary>
/// Wakes the dispatcher once a save that carried outbox rows has committed. Never before: a consumer must not see an
/// uncommitted event. Inside an explicit transaction the wake-up waits for the commit.
/// </summary>
public sealed class OutboxInterceptor(OutboxSignal signal) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly ConcurrentDictionary<DbContext, byte> _pendingCommit = new(ReferenceEqualityComparer.Instance);

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        AfterSave(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AfterSave(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        AfterCommit(eventData.Context);
    }

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        AfterCommit(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (eventData.Context is not null)
        {
            _pendingCommit.TryRemove(eventData.Context, out _);
        }
    }

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TransactionRolledBack(transaction, eventData);
        return Task.CompletedTask;
    }

    private void AfterSave(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // After a save the freshly inserted rows are tracked as Unchanged and still undispatched.
        if (!context.ChangeTracker.Entries<OutboxEvent>().Any(e => e.Entity.DispatchedAt is null))
        {
            return;
        }

        if (context.Database.CurrentTransaction is not null)
        {
            _pendingCommit[context] = 0;
        }
        else
        {
            signal.Notify();
        }
    }

    private void AfterCommit(DbContext? context)
    {
        if (context is not null && _pendingCommit.TryRemove(context, out _))
        {
            signal.Notify();
        }
    }
}
