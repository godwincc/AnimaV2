using System.Text.Json;
using Anima.Core.Models;
using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using AnimaUnit = Anima.Core.Models.Anima;

namespace Anima.Server.Persistence;

// The DB-backed replacement for what used to be one in-memory SanctumRoster instance held for the
// whole process (see SanctumRoster's own doc comment: "no save/load exists yet" is now resolved,
// here). Anima.Core stays completely unaware of persistence -- this is the only place that knows a
// SanctumRoster is actually a set of PersistedAnimaEntity rows.
public class SanctumRosterRepository(AnimaDbContext db, AccountLockRegistry locks)
{
    public async Task<SanctumRoster> LoadAsync(Guid accountId, CancellationToken ct = default)
    {
        var rows = await db.PersistedAnimas.Where(a => a.AccountId == accountId).ToListAsync(ct);
        var roster = new SanctumRoster();

        foreach (var row in rows)
        {
            var anima = JsonSerializer.Deserialize<AnimaUnit>(row.AnimaJson)
                ?? throw new InvalidDataException($"Corrupt AnimaJson for PersistedAnimaEntity {row.Id}.");
            roster.Animas.Add(anima);
        }

        return roster;
    }

    // Upserts a single Anima -- the natural unit of a save. AnimaMaterializationService.Create
    // (Weave/Boss-hatch) mints exactly one new Anima at a time; Reforge's temporary part swaps and
    // combat HP attrition mutate exactly one existing Anima at a time. Nothing in the current design
    // ever needs to add/update the whole roster in one call.
    public async Task SaveAnimaAsync(Guid accountId, AnimaUnit anima, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var row = await db.PersistedAnimas
            .SingleOrDefaultAsync(a => a.AccountId == accountId && a.AnimaId == anima.Id, ct);

        var json = JsonSerializer.Serialize(anima);

        if (row is null)
        {
            db.PersistedAnimas.Add(new PersistedAnimaEntity
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                AnimaId = anima.Id,
                Name = anima.Name,
                Color = anima.Color.ToString(),
                Gen = anima.Gen,
                AnimaJson = json,
            });
        }
        else
        {
            row.Name = anima.Name;
            row.Color = anima.Color.ToString();
            row.Gen = anima.Gen;
            row.AnimaJson = json;
        }

        await db.SaveChangesAsync(ct);
    }
}
