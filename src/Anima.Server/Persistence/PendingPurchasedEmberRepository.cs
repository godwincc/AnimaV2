using Anima.Core.Enums;
using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Persistence;

// DB-backed FIFO queue of paid-for, unresolved Ember purchases -- see PendingPurchasedEmberEntity's
// own comment for the gap this closes (Phase 4 audit). Ordered by PurchasedAtUtc/Id for a stable
// "oldest first" resolution order, same "sequential, never batched" rule the free in-memory
// PlayerSession.PendingEmbers queue already follows.
public class PendingPurchasedEmberRepository(AnimaDbContext db, AccountLockRegistry locks)
{
    public async Task<IReadOnlyList<AnimaColor>> LoadAsync(Guid accountId, CancellationToken ct = default)
    {
        var rows = await db.PendingPurchasedEmbers
            .Where(e => e.AccountId == accountId)
            .OrderBy(e => e.PurchasedAtUtc).ThenBy(e => e.Id)
            .ToListAsync(ct);

        return rows.Select(r => Enum.Parse<AnimaColor>(r.Color)).ToList();
    }

    public async Task AddAsync(Guid accountId, AnimaColor color, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        db.PendingPurchasedEmbers.Add(new PendingPurchasedEmberEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Color = color.ToString(),
            PurchasedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    // Removes the oldest pending purchased Ember (the front of the queue) for this account, if
    // any. Callers are expected to have already read/resolved that color via LoadAsync before
    // calling this -- same "caller drives the choice, this just commits the removal" split as
    // Session.PendingEmbers.Dequeue().
    public async Task RemoveOldestAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var row = await db.PendingPurchasedEmbers
            .Where(e => e.AccountId == accountId)
            .OrderBy(e => e.PurchasedAtUtc).ThenBy(e => e.Id)
            .FirstOrDefaultAsync(ct);

        if (row is not null)
        {
            db.PendingPurchasedEmbers.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }
}
