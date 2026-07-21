using System.Text.Json;
using Anima.Core.Weaving;
using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Anima.Server.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Persistence;

// The DB-backed replacement for what would otherwise be a purely in-memory Session.PendingBossHatch
// -- see PendingBossHatchEntity's own comment for the gap this closes, mirroring
// PendingWeaveRepository exactly. Save/Delete are called from GameHub.SubmitAction (Boss Victory)/
// ConfirmBossHatch; Load is called once per (re)connect from PlayerSessionRegistry.CreateAsync.
public class PendingBossHatchRepository(AnimaDbContext db, AccountLockRegistry locks)
{
    public async Task<PendingBossHatch?> LoadAsync(Guid accountId, CancellationToken ct = default)
    {
        var row = await db.PendingBossHatches.SingleOrDefaultAsync(h => h.AccountId == accountId, ct);
        if (row is null) return null;

        var genome = JsonSerializer.Deserialize<AnimaGenome>(row.GenomeJson)
            ?? throw new InvalidDataException($"Corrupt GenomeJson for PendingBossHatchEntity {row.Id}.");

        return new PendingBossHatch { Genome = genome };
    }

    public async Task SaveAsync(Guid accountId, PendingBossHatch pending, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var row = await db.PendingBossHatches.SingleOrDefaultAsync(h => h.AccountId == accountId, ct);
        var genomeJson = JsonSerializer.Serialize(pending.Genome);

        if (row is null)
        {
            db.PendingBossHatches.Add(new PendingBossHatchEntity
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                GenomeJson = genomeJson,
            });
        }
        else
        {
            row.GenomeJson = genomeJson;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var row = await db.PendingBossHatches.SingleOrDefaultAsync(h => h.AccountId == accountId, ct);
        if (row is not null)
        {
            db.PendingBossHatches.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }
}
