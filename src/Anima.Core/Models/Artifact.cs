using Anima.Core.Combat;
using Anima.Core.Economy;

namespace Anima.Core.Models;

public class Artifact
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Action<CombatState>? OnCombatStart { get; set; }

    // Fires once, the moment this Artifact is granted to a RunLedger (see ArtifactService.Grant)
    // -- e.g. Marked Coin's one-time bonus resource roll. Distinct from OnCombatStart, which
    // fires every combat for as long as the Artifact is owned.
    public Action<PersistentLedger, Random>? OnPickup { get; set; }
}
