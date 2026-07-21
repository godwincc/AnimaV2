using Anima.Core.Weaving;

namespace Anima.Server.Sessions;

// Holds one Boss Victory's already-rolled (and already-granted-alongside: Wisp/Ember/Echo Shard
// are committed straight to the ledger the moment Victory resolves, same as every other Wisp/Ember
// grant in this hub) hatched Anima genome until ConfirmBossHatch supplies the mandatory name -- see
// the Anima Reveal screen's contract in CLAUDE.md: naming is mandatory, not skippable, same as
// Weaving's own PendingWeave (which this deliberately mirrors). No "discard/cancel" path exists for
// the same reason PendingWeave has none.
public sealed class PendingBossHatch
{
    public required AnimaGenome Genome { get; init; }
}
