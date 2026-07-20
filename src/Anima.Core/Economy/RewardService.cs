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

    // Locked by the Match Result & Retreat System design session: Elite's shard is ALWAYS a
    // Vessel Shard specifically (never Echo) -- "keeps Vessel earned via combat performance, Echo
    // earned via Boss/spontaneous-Weave, no overlap." This replaces the earlier 50/50 shard-type
    // roll GrantEliteWin used before that design was locked. The follow-up "Boss Reward
    // Restructure" session later locked Boss's own shard grant to a guaranteed Echo Shard (see
    // GrantBossWin) -- between the two, there's no longer any 50/50 shard-type roll anywhere.

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
            ledger.Add(ResourceType.VesselShard, 1);
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

    // Locked by the "Boss Reward Restructure + Anima Reveal Screen" design session: Boss's guaranteed
    // reward is Wisp + a guaranteed hatched Anima + a guaranteed Echo Shard. This retires the earlier
    // 50/50 Echo/VesselShard roll (Vessel Shard is now Elite-only, see EliteShardChance above) and
    // the earlier "complete Vessel" resource-counter concept entirely (ResourceType.Vessel has been
    // removed from the enum, not just unused here).
    //
    // The hatched Anima itself is NOT resolved by this method -- Economy has no reference to
    // Weaving (the reverse dependency already exists: WeavingService uses PersistentLedger), and
    // more importantly there's nowhere to put a "hatched Anima" yet: no Anima-materialization step
    // exists anywhere in the codebase (Id/Name/Gen assignment, adding to a Sanctum roster) -- see
    // Anima.Core.Weaving.BossHatchService's own doc comment. The caller (a future Run layer) is
    // expected to call BossHatchService.Roll(rng) as a SEPARATE step alongside this one, same
    // "caller resolves each side-effect individually" pattern Ember drops already use.
    public static void GrantBossWin(PersistentLedger ledger, Random rng, RunLedger? runLedger = null)
    {
        ledger.Add(ResourceType.Wisp, ApplyWispCharm(BossWinWisp, runLedger));
        ledger.Add(ResourceType.EchoShard, 1);
    }

    private static int ApplyWispCharm(int baseWisp, RunLedger? runLedger)
    {
        if (runLedger == null || !runLedger.Artifacts.Any(a => a.Name == "Wisp Charm")) return baseWisp;
        return (int)Math.Round(baseWisp * WispCharmMultiplier);
    }


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
