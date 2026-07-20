using Anima.Core.Weaving;

namespace Anima.Server.Sessions;

// Holds one GameHub.AttemptWeave's already-resolved (and already-paid-for) result until
// ConfirmWeave supplies the mandatory name(s) -- see the Anima Reveal screen's contract in
// CLAUDE.md: naming is mandatory, not skippable, so there is deliberately no "discard/cancel"
// path here. GameHub.AttemptWeave refuses to start a second Weave while one is still pending
// exactly so the Wisp/WeaveCount charge WeavingService.AttemptWeave already committed for this
// one is never silently orphaned by an overwrite.
public sealed class PendingWeave
{
    public required string ParentAId { get; init; }
    public required string ParentBId { get; init; }
    public required WeavingResult Primary { get; init; }
    public WeavingResult? Twin { get; init; }
}
