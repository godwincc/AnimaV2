namespace Anima.Server.Data.Entities;

public enum DelveOutcome
{
    Victory,
    Defeat,
    Retreat,
}

// One row per (Anima, Delve) the Anima was on the team for, capped at the 5 most recent per
// (AccountId, AnimaId) pair -- DelveHistoryRepository.AppendAsync inserts then trims, not a
// windowed read. Backs Anima Profile's "Delve History" section (a real gap flagged as open since
// Phase 5b/5c -- see CLAUDE.md's own note that this needed a new entity/migration/repository,
// unlike the lifetime CompletedDelveCount/FailedDelveCount counters on Models.Anima itself, which
// need no migration at all since they ride the existing opaque AnimaJson blob).
//
// CombatsWon/ElitesDefeated are captured by counting the real DelveRun.ClearedNodes by
// MapNodeType at the moment the Delve ends -- no separate tracking exists or is needed.
// TeammateNamesJson mirrors AccountEntity.TeamAnimaIdsJson's own convention (a small string array,
// JSON-serialized) rather than a delimited string.
public class DelveHistoryEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }

    // Matches PersistedAnimaEntity.AnimaId -- the stable Anima.Core.Models.Anima.Id, not this
    // entity's own Id.
    public required string AnimaId { get; set; }

    public required string Outcome { get; set; } // DelveOutcome, stored as string (same convention PersistedLedgerEntryEntity.ResourceType already uses)
    public int FloorIndexReached { get; set; }
    public int CombatsWon { get; set; }
    public int ElitesDefeated { get; set; }
    public bool BossDefeated { get; set; }
    public required string TeammateNamesJson { get; set; }
    public int WispEarnedThisRun { get; set; }
    public DateTime Timestamp { get; set; }

    public int Version { get; set; }
}
