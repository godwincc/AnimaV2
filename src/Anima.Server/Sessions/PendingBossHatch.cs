using Anima.Core.Weaving;

namespace Anima.Server.Sessions;

// The Boss Victory "Delve Complete" appended summary (Phase 5c), per the locked Match Result
// design: floors reached, Anima used, total Wisp earned this run. Captured exactly once, at the
// moment Boss Victory resolves (GameHub.GrantBossVictoryRewardAsync) -- the last point DelveRun's
// own data (Team, ClearedNodes, WispEarnedSoFar) is still reachable, since Session.ActiveDelveRun
// is nulled immediately after (Boss is the map's terminal node). Deliberately NOT persisted to
// PendingBossHatchEntity/DB (unlike Genome below) -- this is purely cosmetic display data, not an
// economic reward: the Wisp/Echo Shard/Anima are already committed to the ledger/roster by the
// time this exists, so losing it to a disconnect between Boss Victory and ConfirmBossHatch costs
// at most a missing summary screen, not real value -- same "in-memory is fine" reasoning
// PlayerSession.PendingEmbers already uses for a free (not-yet-paid-for) Ember.
public sealed record DelveCompleteSnapshot(int FloorIndexReached, int NodesCleared, IReadOnlyList<string> AnimaUsedNames, int TotalWispEarnedThisRun);

// Holds one Boss Victory's already-rolled (and already-granted-alongside: Wisp/Ember/Echo Shard
// are committed straight to the ledger the moment Victory resolves, same as every other Wisp/Ember
// grant in this hub) hatched Anima genome until ConfirmBossHatch supplies the mandatory name -- see
// the Anima Reveal screen's contract in CLAUDE.md: naming is mandatory, not skippable, same as
// Weaving's own PendingWeave (which this deliberately mirrors). No "discard/cancel" path exists for
// the same reason PendingWeave has none.
public sealed class PendingBossHatch
{
    public required AnimaGenome Genome { get; init; }

    // Null only if a reconnect happened between Boss Victory and ConfirmBossHatch (see this type's
    // own comment for why that's an accepted, deliberate loss) -- every real, uninterrupted Boss
    // Victory populates it.
    public DelveCompleteSnapshot? CompleteSummary { get; init; }
}
