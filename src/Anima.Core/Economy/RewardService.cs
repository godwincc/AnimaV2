using Anima.Core.Enums;

namespace Anima.Core.Economy;

// Grants node-outcome rewards straight into a PersistentLedger. Nothing here reads a DungeonMap
// node -- callers (a future Run layer) decide which node was cleared and call the matching method.
//
// Wisp amounts didn't exist anywhere in the codebase before this (MapGenerator.cs's own comment
// flagged "Wisp costs, node effects, etc. is not wired up yet"). These are a first pass, sized
// loosely against Weaving's 50-400 cost curve and Reforge's 40/80 cost so a single node's payout
// roughly funds a cheap early action -- the ORDER (Resource < Combat < Elite < Boss) is locked per
// CLAUDE.md's reward tier ladder, the exact numbers are not and can be tuned freely.
//
// Ember is momentary, not a ledger balance (see ResourceType's own comment) -- every method that
// can drop Ember returns the dropped color(s) instead of writing them anywhere. The caller (a
// future Run layer) is responsible for resolving each dropped Ember individually and sequentially
// through EmberService (Augment now vs. convert to Wisp) before the reward screen closes, per the
// pickup-flow spec. Wisp itself is still granted straight into the ledger here, same as before.
public static class RewardService
{
    public const int ResourceNodeWisp = 30;
    public const int CombatWinWisp = 50;
    public const int EliteWinWisp = 120;
    public const int BossWinWisp = 300;

    // Elite's 2nd/3rd Ember slots -- each rolled independently at this chance, on top of the 1
    // guaranteed drop, capping Elite at 3 Ember total. Not specified beyond "25% chance each for a
    // 2nd and 3rd" -- that's the locked number, not a judgment call.
    public const double EliteBonusEmberChance = 0.25;

    // Resource's new bonus-Ember roll, additive to its existing flat 30 Wisp (which is otherwise
    // unchanged). Locked number from the spec, not a judgment call.
    public const double ResourceBonusEmberChance = 0.15;

    // Elite's shard-fragment chance. Not specified anywhere, so picked deliberately: 25% echoes
    // Ember's own per-color drop granularity (a quarter chance) while staying clearly below the
    // Boss's guaranteed drop, so Elite still meaningfully under-delivers relative to Boss despite
    // being repeatable multiple times in a single Delve. Flag to the user if a different rate was
    // actually intended.
    public const double EliteShardChance = 0.25;

    private static readonly AnimaColor[] EmberColors =
        [AnimaColor.Crimson, AnimaColor.Onyx, AnimaColor.Verdant, AnimaColor.Azure];

    // Vulcan/Mirage never drop their own Ember since they're hybrid-only outcomes, never a directly
    // rollable base color -- every Ember roll picks uniformly among the 4 base colors only.
    private static AnimaColor RollEmberColor(Random rng) => EmberColors[rng.Next(EmberColors.Length)];

    // Wisp Charm's flat multiplier -- applied to every Wisp grant below whenever it's owned.
    private const double WispCharmMultiplier = 1.2;

    // runLedger is optional so every pre-existing call site (none of which know about Artifacts)
    // still compiles unchanged and behaves exactly as before -- omitting it just means no
    // Artifact-driven bonus applies.
    //
    // Combat: 1 Ember, random color, guaranteed.
    public static List<AnimaColor> GrantCombatWin(PersistentLedger ledger, Random rng, RunLedger? runLedger = null)
    {
        ledger.Add(ResourceType.Wisp, ApplyWispCharm(CombatWinWisp, runLedger));
        return [RollEmberColor(rng)];
    }

    // Elite: 1 Ember guaranteed + an independent EliteBonusEmberChance roll for each of a 2nd and
    // 3rd (max 3 total), each still an independent random color.
    public static List<AnimaColor> GrantEliteWin(PersistentLedger ledger, Random rng, RunLedger? runLedger = null)
    {
        ledger.Add(ResourceType.Wisp, ApplyWispCharm(EliteWinWisp, runLedger));

        var embers = new List<AnimaColor> { RollEmberColor(rng) };
        if (rng.NextDouble() < EliteBonusEmberChance) embers.Add(RollEmberColor(rng));
        if (rng.NextDouble() < EliteBonusEmberChance) embers.Add(RollEmberColor(rng));

        if (rng.NextDouble() < EliteShardChance)
        {
            ledger.Add(RollShardType(rng), 1);
        }

        return embers;
    }

    // Resource: unchanged 30 Wisp, no Ember by default -- plus a new ResourceBonusEmberChance
    // chance of exactly 1 bonus Ember. rng is now required (Resource previously took none) since
    // this roll needs one.
    public static List<AnimaColor> GrantResourceNode(PersistentLedger ledger, Random rng, RunLedger? runLedger = null)
    {
        ledger.Add(ResourceType.Wisp, ApplyWispCharm(ResourceNodeWisp, runLedger));
        return rng.NextDouble() < ResourceBonusEmberChance ? [RollEmberColor(rng)] : [];
    }

    // Boss grants a guaranteed shard fragment too, but only ONE of the two types (50/50, not
    // both): the design doc's own "AND/OR" phrasing left this open. Elite already offers a chance
    // at exactly one of {EchoShard, VesselShard} -- making Boss "the same roll, but guaranteed"
    // keeps the two node types' reward shape consistent, and avoids a single Boss clear handing
    // out full progress toward BOTH Shard economies at once (EchoShardCost is 5 -- a guaranteed
    // double-drop would blow past the intended multi-Delve pacing for whichever type never gets
    // spent). Flag to the user if "both" was actually intended. No Ember involved at all.
    public static void GrantBossWin(PersistentLedger ledger, Random rng, RunLedger? runLedger = null)
    {
        ledger.Add(ResourceType.Wisp, ApplyWispCharm(BossWinWisp, runLedger));
        ledger.Add(ResourceType.Vessel, 1);
        ledger.Add(RollShardType(rng), 1);
    }

    private static int ApplyWispCharm(int baseWisp, RunLedger? runLedger)
    {
        if (runLedger == null || !runLedger.Artifacts.Any(a => a.Name == "Wisp Charm")) return baseWisp;
        return (int)Math.Round(baseWisp * WispCharmMultiplier);
    }

    private static ResourceType RollShardType(Random rng) =>
        rng.NextDouble() < 0.5 ? ResourceType.EchoShard : ResourceType.VesselShard;

    // Marked Coin's on-pickup bonus roll. Not specified anywhere, so weighted deliberately toward
    // the common resources (Wisp/Ember) and away from the two Shard types -- a single (possibly
    // early/cheap) pickup shouldn't meaningfully shortcut the Shard scarcity the rest of the
    // reward system is built around (Elite is a 25% chance at ONE Shard; Boss guarantees exactly
    // one). Flag to the user if a different pool/weighting was actually intended.
    public const int MarkedCoinWispBonus = 40;

    // Each pool entry returns the dropped Ember color, if the branch it hit was Ember -- null for
    // every other branch, since those grant straight into the ledger like before. Matches
    // Artifact.OnPickup's own Func<PersistentLedger, Random, AnimaColor?> shape so ArtifactService.
    // Grant can pass a Marked-Coin-dropped Ember straight into the same pickup-choice flow as a
    // node-dropped one.
    private static readonly (Func<PersistentLedger, Random, AnimaColor?> Grant, double Weight)[] MarkedCoinPool =
    [
        ((ledger, _) => { ledger.Add(ResourceType.Wisp, MarkedCoinWispBonus); return null; }, 0.50),
        ((_, rng) => RollEmberColor(rng), 0.35),
        ((ledger, _) => { ledger.Add(ResourceType.EchoShard, 1); return null; }, 0.075),
        ((ledger, _) => { ledger.Add(ResourceType.VesselShard, 1); return null; }, 0.075),
    ];

    public static AnimaColor? GrantMarkedCoinBonus(PersistentLedger ledger, Random rng)
    {
        var roll = rng.NextDouble();
        var cumulative = 0.0;
        foreach (var (grant, weight) in MarkedCoinPool)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                return grant(ledger, rng);
            }
        }

        return MarkedCoinPool[^1].Grant(ledger, rng); // floating-point safety net -- unreachable in practice, weights sum to 1.0
    }
}
