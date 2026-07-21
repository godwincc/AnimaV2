namespace Anima.Server.Data.Entities;

// DB-backed counterpart to Sessions.PendingBossHatch, mirroring PendingWeaveEntity exactly -- same
// class of gap: a Boss Victory's guaranteed hatched Anima is a real, already-granted-in-substance
// reward (the fight is won, the roll already happened) sitting unresolved until the player supplies
// a name. Losing it to a dropped connection between Boss Victory and ConfirmBossHatch would be a
// genuine loss of a guaranteed, one-per-Boss-clear reward -- at least as high-stakes as a paid-for
// Weave, arguably more so (a Weave can be re-attempted for more Wisp; a specific Boss clear cannot
// be re-fought for free). At most one row per account (mirrors PendingWeaveEntity's "one pending at
// a time" rule -- GameHub.StartDelve refuses to start a new Delve while one is unresolved, same
// spirit as AttemptWeave's own guard). PlayerSessionRegistry.CreateAsync reloads this on every
// (re)connect.
//
// GenomeJson serializes the whole Anima.Core.Weaving.AnimaGenome (same "serialize the whole
// object" pattern PendingWeaveEntity.PrimaryJson/PersistedAnimaEntity.AnimaJson already use).
public class PendingBossHatchEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string GenomeJson { get; set; }
    public int Version { get; set; }
}
