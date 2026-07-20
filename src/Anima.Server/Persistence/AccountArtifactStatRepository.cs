using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Persistence;

// Read side of AccountArtifactStatEntity, for the Collection screen's per-Artifact discovered/
// won-with-count display. See AccountArtifactStatEntity's own comment: no write path exists yet
// (that lands with Treasure/Shop pickup and Boss-victory resolution, a later phase), so every
// account currently loads an empty dictionary here -- an honest reflection of real state, not a
// bug.
public class AccountArtifactStatRepository(AnimaDbContext db)
{
    public async Task<IReadOnlyDictionary<string, AccountArtifactStatEntity>> LoadAsync(Guid accountId, CancellationToken ct = default)
    {
        var rows = await db.ArtifactStats.Where(s => s.AccountId == accountId).ToListAsync(ct);
        return rows.ToDictionary(r => r.ArtifactName);
    }
}
