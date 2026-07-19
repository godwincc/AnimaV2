using Anima.Core.Models;

namespace Anima.Core.Economy;

// Delve-scoped state that resets when a Delve ends -- never persisted. Artifacts (not yet
// granted/consumed by anything) live here; this is where a future Run layer would add/remove
// them. Reforge's temporary part swaps deliberately do NOT live here -- they already mutate the
// Anima instance directly and revert with it (see ReforgeService.Accept's own doc comment), so
// there's nothing extra to track.
public sealed class RunLedger
{
    public List<Artifact> Artifacts { get; } = new();
}
