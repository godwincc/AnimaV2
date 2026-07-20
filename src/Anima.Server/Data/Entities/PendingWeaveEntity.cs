namespace Anima.Server.Data.Entities;

// DB-backed counterpart to Sessions.PendingWeave. See the Phase 3 audit finding this fixes: the
// in-memory-only guard (Session.PendingWeave, keyed off a SignalR ConnectionId) is dropped
// entirely on disconnect, including the resolved-but-unnamed genome it holds -- GameHub.
// AttemptWeave already spent real Wisp/Echo Shards and incremented both parents' WeaveCount
// before that guard is ever set, so losing it on a dropped connection meant a paid-for Weave
// became permanently unclaimable (and the guard silently reset, letting the player pay for and
// orphan another one). At most one row per account (mirrors the hub's own "one pending Weave at a
// time" rule) -- PlayerSessionRegistry.CreateAsync reloads this on every (re)connect.
//
// PrimaryJson/TwinJson serialize the whole Anima.Core.Weaving.WeavingResult (same "serialize the
// whole object" pattern PersistedAnimaEntity.AnimaJson already uses) -- ConfirmWeave only ever
// reads Genome/Stats off it, but keeping the full record (including HybridTriggered) means
// GameHub.GetPendingWeave can rebuild the exact same WeaveRevealSnapshot a reconnected client
// would have gotten from the original AttemptWeave call, not just enough to blindly confirm.
public class PendingWeaveEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string ParentAId { get; set; }
    public required string ParentBId { get; set; }
    public int WispCost { get; set; }
    public required string PrimaryJson { get; set; }
    public string? TwinJson { get; set; }
    public int Version { get; set; }
}
