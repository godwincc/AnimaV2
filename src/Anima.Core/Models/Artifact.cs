using Anima.Core.Combat;

namespace Anima.Core.Models;

public class Artifact
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Action<CombatState>? OnCombatStart { get; set; }
    // FUTURE: run-scoped artifacts (Wisp Charm, Weaver's Thread) will need a separate hook
    // once the Run layer exists, since they affect the meta-economy, not combat directly.
}
