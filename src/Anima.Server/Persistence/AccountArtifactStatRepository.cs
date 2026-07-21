using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Persistence;

// Reads/writes AccountArtifactStatEntity, for the Collection screen's per-Artifact discovered/
// won-with-count display. RecordDiscoveryAsync (NEW this session, wired from GameHub.
// ClaimTreasureNode) is the first real write path -- the "won a Delve while held" count is still
// untouched here, since that needs Boss-victory resolution (Phase 5).
public class AccountArtifactStatRepository(AnimaDbContext db, AccountLockRegistry locks)
{
    public async Task<IReadOnlyDictionary<string, AccountArtifactStatEntity>> LoadAsync(Guid accountId, CancellationToken ct = default)
    {
        var rows = await db.ArtifactStats.Where(s => s.AccountId == accountId).ToListAsync(ct);
        return rows.ToDictionary(r => r.ArtifactName);
    }

    // Idempotent: a re-discovery of an already-discovered Artifact (a 2nd/3rd Treasure hit on the
    // same one across separate Delves) is a no-op -- FirstDiscoveredAtUtc stays at its original
    // value, and there's no "times discovered" counter to bump (only DelvesWonWithCount, untouched
    // here).
    public async Task RecordDiscoveryAsync(Guid accountId, string artifactName, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var exists = await db.ArtifactStats.AnyAsync(s => s.AccountId == accountId && s.ArtifactName == artifactName, ct);
        if (exists) return;

        db.ArtifactStats.Add(new AccountArtifactStatEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            ArtifactName = artifactName,
            FirstDiscoveredAtUtc = DateTime.UtcNow,
            DelvesWonWithCount = 0,
        });

        await db.SaveChangesAsync(ct);
    }

    // The "won a Delve while held" write path (Phase 5b) -- called once per currently-held Artifact
    // on a confirmed Boss Victory (see GameHub.SubmitAction's Boss branch). Upserts rather than
    // assuming a row already exists: in practice every held Artifact was already granted via
    // ArtifactService.Grant, which always runs through RecordDiscoveryAsync first (Treasure/Shop),
    // so a row should always be there by the time this runs -- but upserting costs nothing extra
    // and avoids a foreign-key-shaped assumption if some future grant path ever skips discovery.
    public async Task RecordWinAsync(Guid accountId, string artifactName, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var row = await db.ArtifactStats.SingleOrDefaultAsync(s => s.AccountId == accountId && s.ArtifactName == artifactName, ct);
        if (row is null)
        {
            db.ArtifactStats.Add(new AccountArtifactStatEntity
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ArtifactName = artifactName,
                FirstDiscoveredAtUtc = DateTime.UtcNow,
                DelvesWonWithCount = 1,
            });
        }
        else
        {
            row.DelvesWonWithCount++;
        }

        await db.SaveChangesAsync(ct);
    }
}
