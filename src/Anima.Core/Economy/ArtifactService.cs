using Anima.Core.Combat;
using Anima.Core.Enums;
using Anima.Core.Models;
using AnimaUnit = Anima.Core.Models.Anima;

namespace Anima.Core.Economy;

// Non-combat-engine Artifact effects: granting an Artifact in the first place, plus the
// Delve-layer (not per-combat) effects that don't fit inside CombatEngine -- Withering Fang and
// Sapling Charm (both fire on ANY node visit, not just combat) and Ember Core (a Shop-price
// discount, nothing to do with combat at all).
public static class ArtifactService
{
    // Hard cap on held Artifacts for the whole Delve, no swap mechanic -- a Treasure reward that
    // would exceed it is skipped/lost entirely (no substitute), and a Shop's Artifact-for-sale
    // slot doesn't roll at all while at cap. Deliberately NOT enforced inside Grant itself: the two
    // call sites react differently to being at cap (Treasure loses the reward outright; Shop skips
    // generating the offer in the first place), so the check belongs at each call site, same
    // "no event bus, direct checks at each checkpoint" pattern the rest of the codebase uses.
    public const int MaxArtifactsPerDelve = 3;

    public static bool HasArtifactCapacity(RunLedger runLedger) => runLedger.Artifacts.Count < MaxArtifactsPerDelve;

    // Full shop/pickup UI flow isn't built yet -- this is the simple "give the player this
    // Artifact" entry point both real code and tests use, firing the Artifact's own OnPickup hook
    // (e.g. Marked Coin's one-time bonus) exactly once as part of the grant. Returns the dropped
    // Ember color if OnPickup happened to roll one (e.g. Marked Coin's Ember branch) -- the caller
    // is responsible for running it through the same pickup-choice flow (EmberService) as any other
    // dropped Ember, same as RewardService's own node-reward methods. Callers are responsible for
    // checking HasArtifactCapacity BEFORE calling this -- Grant itself doesn't check or enforce the
    // cap (see MaxArtifactsPerDelve's own comment for why).
    public static AnimaColor? Grant(RunLedger runLedger, Artifact artifact, PersistentLedger persistentLedger, Random rng)
    {
        runLedger.Artifacts.Add(artifact);
        return artifact.OnPickup?.Invoke(persistentLedger, rng);
    }

    // Sapling Charm's per-node heal. 10% is fixed by the brief -- not a judgment call.
    private const double SaplingCharmHealPercent = 0.10;

    // The single "the player just visited a node" checkpoint -- both Withering Fang and Sapling
    // Charm fire off it, since both trigger on ANY node type. playerTeam is required (not just
    // combatState.PlayerTeam) because Sapling Charm needs to heal the team even on the non-combat
    // node types where combatState is null. No Run layer exists yet to call this automatically on
    // a real node visit, so it's a direct test hook, same pattern as ArtifactService.Grant above.
    //
    // Withering Fang: a single consumable charge, consumed the next time the player visits ANY
    // node -- combatState is null for a non-combat node (wasted, no effect) or that node's
    // CombatState for a combat node (sets the lowest-current-HP living enemy to exactly 1 HP).
    // Lowest-HP was chosen over front/position-1 to echo the game's existing "execute" design
    // language (Ember's own Execute skill already targets any-position lowest-HP) rather than
    // inventing a positional-snipe rule with no precedent -- flag to the user if front/position-1
    // was actually intended.
    //
    // Sapling Charm: heals the whole team 10% of MaxHp on every node entry, any type. Only heals
    // currently-living members (CurrentHp > 0) -- a fallen Anima staying down until a real heal
    // source revives them (e.g. Shop) was judged the safer reading of "heals the whole team" than
    // silently reviving anyone who died, which would make death nearly consequence-free for as
    // long as this Artifact is held. Flag to the user if reviving was actually intended.
    public static void OnNodeVisited(RunLedger runLedger, List<AnimaUnit> playerTeam, CombatState? combatState = null)
    {
        var witheringFang = runLedger.Artifacts.FirstOrDefault(a => a.Name == "Withering Fang");
        if (witheringFang != null)
        {
            runLedger.Artifacts.Remove(witheringFang); // charge consumed regardless of outcome

            if (combatState != null)
            {
                var target = combatState.EnemyTeam.Where(e => e.CurrentHp > 0).OrderBy(e => e.CurrentHp).FirstOrDefault();
                if (target != null) target.CurrentHp = 1;
            }
        }

        if (runLedger.Artifacts.Any(a => a.Name == "Sapling Charm"))
        {
            foreach (var anima in playerTeam.Where(a => a.CurrentHp > 0))
            {
                var healAmount = (int)Math.Round(anima.MaxHp * SaplingCharmHealPercent);
                anima.CurrentHp = Math.Min(anima.MaxHp, anima.CurrentHp + healAmount);
            }
        }
    }

    // Ember Core's flat discount. Picked 20% (the top of the stated 15-20% range) to match Wisp
    // Charm's own +20% for a consistent "economy" Artifact power level, and because it's a clean,
    // easily-reasoned-about number rather than an arbitrary point in the range. Exposed publicly
    // so any Shop-tier cost calculation can apply it uniformly -- wired into ReforgeService.Accept,
    // EmberService.TryBuyEmber (buying Ember from Wares), and AugmentService.TryApplyAugment's own
    // Wisp tier today, matching this Artifact's own description ("Reforge and Augment costs are
    // reduced by 20%").
    private const double EmberCoreDiscountMultiplier = 0.8;

    public static int ApplyEmberCoreDiscount(int baseCost, RunLedger? runLedger)
    {
        if (runLedger == null || !runLedger.Artifacts.Any(a => a.Name == "Ember Core")) return baseCost;
        return (int)Math.Round(baseCost * EmberCoreDiscountMultiplier);
    }
}
