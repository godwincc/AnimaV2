using Anima.Core.Combat;
using Anima.Core.Models;

namespace Anima.Core.Economy;

// Non-combat-engine Artifact effects: granting an Artifact in the first place, plus the two
// Delve-layer (not per-combat) effects that don't fit inside CombatEngine -- Withering Fang
// (fires on ANY node visit, not just combat) and Ember Core (a Shop-price discount, nothing to
// do with combat at all).
public static class ArtifactService
{
    // Full shop/pickup UI flow isn't built yet -- this is the simple "give the player this
    // Artifact" entry point both real code and tests use, firing the Artifact's own OnPickup hook
    // (e.g. Marked Coin's one-time bonus) exactly once as part of the grant.
    public static void Grant(RunLedger runLedger, Artifact artifact, PersistentLedger persistentLedger, Random rng)
    {
        runLedger.Artifacts.Add(artifact);
        artifact.OnPickup?.Invoke(persistentLedger, rng);
    }

    // Withering Fang: a single consumable charge, consumed the next time the player visits ANY
    // node -- combatState is null for a non-combat node (wasted, no effect) or that node's
    // CombatState for a combat node (sets the lowest-current-HP living enemy to exactly 1 HP).
    // Lowest-HP was chosen over front/position-1 to echo the game's existing "execute" design
    // language (Ember's own Execute skill already targets any-position lowest-HP) rather than
    // inventing a positional-snipe rule with no precedent -- flag to the user if front/position-1
    // was actually intended. No Run layer exists yet to call this automatically on a real node
    // visit, so it's a direct "the player just visited a node" test hook, same pattern as
    // ArtifactService.Grant above.
    public static void OnNodeVisited(RunLedger runLedger, CombatState? combatState = null)
    {
        var witheringFang = runLedger.Artifacts.FirstOrDefault(a => a.Name == "Withering Fang");
        if (witheringFang == null) return;

        runLedger.Artifacts.Remove(witheringFang); // charge consumed regardless of outcome

        if (combatState == null) return; // non-combat node -- wasted, no effect

        var target = combatState.EnemyTeam.Where(e => e.CurrentHp > 0).OrderBy(e => e.CurrentHp).FirstOrDefault();
        if (target == null) return;

        target.CurrentHp = 1;
    }

    // Ember Core's flat discount. Picked 20% (the top of the stated 15-20% range) to match Wisp
    // Charm's own +20% for a consistent "economy" Artifact power level, and because it's a clean,
    // easily-reasoned-about number rather than an arbitrary point in the range. Exposed publicly
    // so any Shop-tier cost calculation can apply it uniformly -- wired into ReforgeService.Accept
    // today; Augment costs have no actual pricing system anywhere in the codebase yet to discount,
    // so that half is flagged rather than invented.
    private const double EmberCoreDiscountMultiplier = 0.8;

    public static int ApplyEmberCoreDiscount(int baseCost, RunLedger? runLedger)
    {
        if (runLedger == null || !runLedger.Artifacts.Any(a => a.Name == "Ember Core")) return baseCost;
        return (int)Math.Round(baseCost * EmberCoreDiscountMultiplier);
    }
}
