using System.Text.Json;
using Anima.Core.Weaving;
using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Anima.Server.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Persistence;

// The DB-backed replacement for what would otherwise be a purely in-memory Session.
// PendingStarterReveal -- see PendingStarterRevealEntity's own comment for the gap this closes,
// mirroring PendingBossHatchRepository exactly. SaveAsync is called from AuthService.RegisterAsync
// (the initial roll) and GameHub.ConfirmStarterAnima (advancing NextUnnamedIndex); DeleteAsync fires
// once all 3 slots are named; LoadAsync is called once per (re)connect from
// PlayerSessionRegistry.CreateAsync.
public class PendingStarterRevealRepository(AnimaDbContext db, AccountLockRegistry locks)
{
    public async Task<PendingStarterReveal?> LoadAsync(Guid accountId, CancellationToken ct = default)
    {
        var row = await db.PendingStarterReveals.SingleOrDefaultAsync(r => r.AccountId == accountId, ct);
        if (row is null) return null;

        var rolls = JsonSerializer.Deserialize<List<StarterAnimaRoll>>(row.RollsJson)
            ?? throw new InvalidDataException($"Corrupt RollsJson for PendingStarterRevealEntity {row.Id}.");
        var confirmedAnimaIds = JsonSerializer.Deserialize<List<string>>(row.ConfirmedAnimaIdsJson) ?? [];

        return new PendingStarterReveal { Rolls = rolls, NextUnnamedIndex = row.NextUnnamedIndex, ConfirmedAnimaIds = confirmedAnimaIds };
    }

    public async Task SaveAsync(Guid accountId, PendingStarterReveal pending, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var row = await db.PendingStarterReveals.SingleOrDefaultAsync(r => r.AccountId == accountId, ct);
        var rollsJson = JsonSerializer.Serialize(pending.Rolls);
        var confirmedAnimaIdsJson = JsonSerializer.Serialize(pending.ConfirmedAnimaIds);

        if (row is null)
        {
            db.PendingStarterReveals.Add(new PendingStarterRevealEntity
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                RollsJson = rollsJson,
                NextUnnamedIndex = pending.NextUnnamedIndex,
                ConfirmedAnimaIdsJson = confirmedAnimaIdsJson,
            });
        }
        else
        {
            row.RollsJson = rollsJson;
            row.NextUnnamedIndex = pending.NextUnnamedIndex;
            row.ConfirmedAnimaIdsJson = confirmedAnimaIdsJson;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var row = await db.PendingStarterReveals.SingleOrDefaultAsync(r => r.AccountId == accountId, ct);
        if (row is not null)
        {
            db.PendingStarterReveals.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }
}
