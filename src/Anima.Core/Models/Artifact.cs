using Anima.Core.Combat;
using Anima.Core.Economy;
using Anima.Core.Enums;

namespace Anima.Core.Models;

public class Artifact
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Action<CombatState>? OnCombatStart { get; set; }

    // Fires once, the moment this Artifact is granted to a RunLedger (see ArtifactService.Grant)
    // -- e.g. Marked Coin's one-time bonus resource roll. Distinct from OnCombatStart, which
    // fires every combat for as long as the Artifact is owned. Returns the dropped Ember color, if
    // the roll happened to land on Ember (null otherwise) -- Ember is a momentary pickup, never
    // written straight to the ledger, so it has to come back out through the return value instead.
    public Func<PersistentLedger, Random, AnimaColor?>? OnPickup { get; set; }
}
