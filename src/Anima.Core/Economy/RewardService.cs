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
public static class RewardService
{
    public const int ResourceNodeWisp = 30;
    public const int CombatWinWisp = 50;
    public const int EliteWinWisp = 120;
    public const int BossWinWisp = 300;

    public const int CombatWinEmberCount = 2;
    public const int EliteWinEmberCount = 3;

    // Elite's shard-fragment chance. Not specified anywhere, so picked deliberately: 25% echoes
    // Ember's own per-color drop granularity (a quarter chance) while staying clearly below the
    // Boss's guaranteed drop, so Elite still meaningfully under-delivers relative to Boss despite
    // being repeatable multiple times in a single Delve. Flag to the user if a different rate was
    // actually intended.
    public const double EliteShardChance = 0.25;

    private static readonly AnimaColor[] EmberColors =
        [AnimaColor.Crimson, AnimaColor.Onyx, AnimaColor.Verdant, AnimaColor.Azure];

    public static ResourceType EmberFor(AnimaColor color) => color switch
    {
        AnimaColor.Crimson => ResourceType.EmberCrimson,
        AnimaColor.Onyx => ResourceType.EmberOnyx,
        AnimaColor.Verdant => ResourceType.EmberVerdant,
        AnimaColor.Azure => ResourceType.EmberAzure,
        _ => throw new ArgumentOutOfRangeException(nameof(color), color,
            "Ember only comes in the 4 base colors -- Vulcan/Mirage are hybrid-only outcomes, never a directly rollable base color."),
    };

    public static void GrantCombatWin(PersistentLedger ledger, Random rng)
    {
        ledger.Add(ResourceType.Wisp, CombatWinWisp);
        GrantRandomColorEmber(ledger, CombatWinEmberCount, rng);
    }

    public static void GrantEliteWin(PersistentLedger ledger, Random rng)
    {
        ledger.Add(ResourceType.Wisp, EliteWinWisp);
        GrantRandomColorEmber(ledger, EliteWinEmberCount, rng);

        if (rng.NextDouble() < EliteShardChance)
        {
            ledger.Add(RollShardType(rng), 1);
        }
    }

    public static void GrantResourceNode(PersistentLedger ledger)
    {
        ledger.Add(ResourceType.Wisp, ResourceNodeWisp);
    }

    // Boss grants a guaranteed shard fragment too, but only ONE of the two types (50/50, not
    // both): the design doc's own "AND/OR" phrasing left this open. Elite already offers a chance
    // at exactly one of {EchoShard, VesselShard} -- making Boss "the same roll, but guaranteed"
    // keeps the two node types' reward shape consistent, and avoids a single Boss clear handing
    // out full progress toward BOTH Shard economies at once (EchoShardCost is 5 -- a guaranteed
    // double-drop would blow past the intended multi-Delve pacing for whichever type never gets
    // spent). Flag to the user if "both" was actually intended.
    public static void GrantBossWin(PersistentLedger ledger, Random rng)
    {
        ledger.Add(ResourceType.Wisp, BossWinWisp);
        ledger.Add(ResourceType.Vessel, 1);
        ledger.Add(RollShardType(rng), 1);
    }

    private static ResourceType RollShardType(Random rng) =>
        rng.NextDouble() < 0.5 ? ResourceType.EchoShard : ResourceType.VesselShard;

    private static void GrantRandomColorEmber(PersistentLedger ledger, int count, Random rng)
    {
        for (var i = 0; i < count; i++)
        {
            var color = EmberColors[rng.Next(EmberColors.Length)];
            ledger.Add(EmberFor(color), 1);
        }
    }
}
