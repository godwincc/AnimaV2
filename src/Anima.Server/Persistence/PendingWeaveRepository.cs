using System.Text.Json;
using Anima.Core.Weaving;
using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Anima.Server.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Persistence;

// The DB-backed replacement for what used to be a purely in-memory Session.PendingWeave -- see
// PendingWeaveEntity's own comment for the gap this closes. Save/Delete are called from GameHub.
// AttemptWeave/ConfirmWeave; Load is called once per (re)connect from PlayerSessionRegistry.
// CreateAsync, same pattern as SanctumRosterRepository/PersistentLedgerRepository.
public class PendingWeaveRepository(AnimaDbContext db, AccountLockRegistry locks)
{
    public async Task<PendingWeave?> LoadAsync(Guid accountId, CancellationToken ct = default)
    {
        var row = await db.PendingWeaves.SingleOrDefaultAsync(w => w.AccountId == accountId, ct);
        if (row is null) return null;

        var primary = JsonSerializer.Deserialize<WeavingResult>(row.PrimaryJson)
            ?? throw new InvalidDataException($"Corrupt PrimaryJson for PendingWeaveEntity {row.Id}.");
        var twin = row.TwinJson is null ? null : JsonSerializer.Deserialize<WeavingResult>(row.TwinJson);

        return new PendingWeave
        {
            ParentAId = row.ParentAId,
            ParentBId = row.ParentBId,
            WispCost = row.WispCost,
            Primary = primary,
            Twin = twin,
        };
    }

    public async Task SaveAsync(Guid accountId, PendingWeave pending, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var row = await db.PendingWeaves.SingleOrDefaultAsync(w => w.AccountId == accountId, ct);
        var primaryJson = JsonSerializer.Serialize(pending.Primary);
        var twinJson = pending.Twin is null ? null : JsonSerializer.Serialize(pending.Twin);

        if (row is null)
        {
            db.PendingWeaves.Add(new PendingWeaveEntity
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ParentAId = pending.ParentAId,
                ParentBId = pending.ParentBId,
                WispCost = pending.WispCost,
                PrimaryJson = primaryJson,
                TwinJson = twinJson,
            });
        }
        else
        {
            row.ParentAId = pending.ParentAId;
            row.ParentBId = pending.ParentBId;
            row.WispCost = pending.WispCost;
            row.PrimaryJson = primaryJson;
            row.TwinJson = twinJson;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var row = await db.PendingWeaves.SingleOrDefaultAsync(w => w.AccountId == accountId, ct);
        if (row is not null)
        {
            db.PendingWeaves.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }
}
