using System.Text.Json;
using Anima.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Persistence;

// Account-row-level reads/writes that don't belong to SanctumRosterRepository or
// PersistentLedgerRepository -- currently just the active-team selection (AccountEntity.
// TeamAnimaIdsJson). Kept separate from those two rather than bolted onto either, since "which 3
// Anima are on the team" is account state, not roster state or ledger state.
public class AccountRepository(AnimaDbContext db, AccountLockRegistry locks)
{
    public async Task<IReadOnlyList<string>> LoadTeamAsync(Guid accountId, CancellationToken ct = default)
    {
        var json = await db.Accounts
            .Where(a => a.Id == accountId)
            .Select(a => a.TeamAnimaIdsJson)
            .SingleOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
        return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
    }

    public async Task SaveTeamAsync(Guid accountId, IReadOnlyList<string> animaIds, CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, ct);
        account.TeamAnimaIdsJson = JsonSerializer.Serialize(animaIds);

        await db.SaveChangesAsync(ct);
    }
}
