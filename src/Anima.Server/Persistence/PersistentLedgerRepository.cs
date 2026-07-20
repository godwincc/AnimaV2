using Anima.Core.Economy;
using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Persistence;

// The DB-backed replacement for what used to be one in-memory PersistentLedger instance held for
// the whole process (see PersistentLedger's own doc comment). One row per (account, ResourceType);
// a missing row means balance 0, same as the dictionary's own GetValueOrDefault default today.
public class PersistentLedgerRepository(AnimaDbContext db, AccountLockRegistry locks)
{
    public async Task<PersistentLedger> LoadAsync(Guid accountId, CancellationToken ct = default)
    {
        var rows = await db.LedgerEntries.Where(l => l.AccountId == accountId).ToListAsync(ct);
        var ledger = new PersistentLedger();

        foreach (var row in rows)
        {
            var type = Enum.Parse<ResourceType>(row.ResourceType);
            ledger.Add(type, row.Amount);
        }

        return ledger;
    }

    // Snapshot-writes every ResourceType currently on `ledger`. Call frequency is low -- Wisp/Shard
    // balances only change at reward/shop/Delve-end checkpoints, never per combat action -- so a
    // full upsert-all is simpler and safer than diffing against the DB for just the deltas that
    // changed, and avoids a second source of "what actually changed" bugs.
    public async Task SaveAsync(Guid accountId, PersistentLedger ledger, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var rows = await db.LedgerEntries.Where(l => l.AccountId == accountId).ToListAsync(ct);
        var byType = rows.ToDictionary(r => r.ResourceType);

        foreach (var type in Enum.GetValues<ResourceType>())
        {
            var amount = ledger.GetBalance(type);
            if (byType.TryGetValue(type.ToString(), out var row))
            {
                row.Amount = amount;
            }
            else
            {
                db.LedgerEntries.Add(new PersistedLedgerEntryEntity
                {
                    Id = Guid.NewGuid(),
                    AccountId = accountId,
                    ResourceType = type.ToString(),
                    Amount = amount,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
