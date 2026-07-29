using System.Text.Json;
using Anima.Server.Data;
using Anima.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anima.Server.Persistence;

// Backs Anima Profile's per-Anima "Delve History" -- a capped last-5 log per (AccountId, AnimaId),
// written once per team member at Delve-end (Boss Victory/Defeat/Retreat). AppendAsync inserts the
// new row then trims that same pair down to 5 by deleting the oldest beyond it, per the task's own
// instruction (delete-oldest-beyond-5 on write, not a windowed query on read).
public class DelveHistoryRepository(AnimaDbContext db, AccountLockRegistry locks)
{
    private const int MaxEntriesPerAnima = 5;

    public async Task<IReadOnlyList<DelveHistoryEntity>> LoadRecentAsync(Guid accountId, string animaId, CancellationToken ct = default)
    {
        return await db.DelveHistories
            .Where(h => h.AccountId == accountId && h.AnimaId == animaId)
            .OrderByDescending(h => h.Timestamp)
            .Take(MaxEntriesPerAnima)
            .ToListAsync(ct);
    }

    // Backs the Hub screen's account-level "Last delve" summary bar (NEW, Hub screen session --
    // this table only ever had a per-Anima read before this). Every team member gets their OWN row
    // for the same Delve-end event (AppendAsync is called once per team member, same Timestamp/
    // Outcome/FloorIndexReached/BossDefeated each time -- see its own comment), so the single most
    // recent row across the WHOLE account, regardless of which AnimaId it's filed under, is exactly
    // "the last Delve this account played." Null if the account has never finished a Delve yet
    // (brand-new account, or one that's only ever quit mid-run without a Boss Victory/Defeat/
    // Retreat resolving -- though in practice every real Delve end DOES call AppendAsync).
    public async Task<DelveHistoryEntity?> LoadMostRecentForAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        return await db.DelveHistories
            .Where(h => h.AccountId == accountId)
            .OrderByDescending(h => h.Timestamp)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AppendAsync(
        Guid accountId,
        string animaId,
        DelveOutcome outcome,
        int floorIndexReached,
        int combatsWon,
        int elitesDefeated,
        bool bossDefeated,
        IReadOnlyList<string> teammateNames,
        int wispEarnedThisRun,
        CancellationToken ct = default)
    {
        await using var _ = await locks.AcquireAsync(accountId, ct);

        db.DelveHistories.Add(new DelveHistoryEntity
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            AnimaId = animaId,
            Outcome = outcome.ToString(),
            FloorIndexReached = floorIndexReached,
            CombatsWon = combatsWon,
            ElitesDefeated = elitesDefeated,
            BossDefeated = bossDefeated,
            TeammateNamesJson = JsonSerializer.Serialize(teammateNames),
            WispEarnedThisRun = wispEarnedThisRun,
            Timestamp = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        // Trim AFTER the insert, scoped to this same (AccountId, AnimaId) pair only -- a fresh read
        // rather than reusing any in-memory tracked set, since a stale count would risk trimming to
        // the wrong size if this same pair was touched elsewhere in the same DbContext lifetime.
        var rows = await db.DelveHistories
            .Where(h => h.AccountId == accountId && h.AnimaId == animaId)
            .OrderByDescending(h => h.Timestamp)
            .ToListAsync(ct);

        if (rows.Count > MaxEntriesPerAnima)
        {
            db.DelveHistories.RemoveRange(rows.Skip(MaxEntriesPerAnima));
            await db.SaveChangesAsync(ct);
        }
    }
}
