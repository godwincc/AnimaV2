using Anima.Core.Models;

namespace Anima.Core.Weaving;

// Name is always null the instant a Vessel is produced here (both a normal Weave's Primary and an
// Echo Twin) -- a transitional state, not a bug: the real game's UI will immediately prompt the
// player to name a new Vessel before it's usable, but that naming-prompt flow doesn't exist yet
// (no UI is built at all currently). No Anima-materialization step exists yet either (nothing
// converts a WeavingResult into a real playable Anima with its own Id) -- that's a separate,
// larger, not-yet-scoped piece; this only makes sure the eventual naming step has an honestly
// empty field to fill in, rather than silently defaulting to something invented here.
public sealed record WeavingResult(
    AnimaGenome Genome,
    Stats Stats,
    bool HybridTriggered,
    IReadOnlyList<PartResolution> PartResolutions,
    string? Name = null);
