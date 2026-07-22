namespace Anima.Core.Data;

using Anima.Core.Economy;
using Anima.Core.Models;

// The full 12-Artifact set -- all Delve-scoped (run-only, no cross-Delve persistence). Each is
// checked purely by Name string -- either via a hook field (OnCombatStart/OnPickup, for effects a
// plain delegate can express) or a direct name check inside CombatEngine/RewardService/
// ArtifactService (for effects needing engine-internal state/logic a delegate can't reach, e.g.
// DrawCards' pile-recycling, the once-per-combat Twin Flame flag, or Focusing Lens's per-combat
// attack counter) -- same "no event bus, direct checks at known checkpoints" pattern the rest of
// the codebase already uses for Crest passives. Not the final balance pass, but the mechanics are
// locked.
public static class SampleArtifacts
{
    // All 12 factories, one call each -- shared by anything that needs "the full Artifact roster"
    // (ShopService's Wares-Artifact roll, the Delve simulation's Treasure cycle), same pattern as
    // PrimitiveRoster.All for Animas. Order is arbitrary, not a drop-rate weighting.
    public static IReadOnlyList<Func<Artifact>> AllFactories { get; } =
    [
        CreateTwinFlame, CreateWispCharm, CreateBarrierStone, CreateVanguardsBell, CreateWeaversThread,
        CreateMarkedCoin, CreateWitheringFang, CreateFocusingLens, CreateSilentChime, CreateEmberCore,
        CreateSaplingCharm, CreateSiftingStone,
    ];

    // REDEFINED from an earlier hook-validation placeholder (which only proved OnCombatStart
    // actually fires -- Vanguard's Bell now demonstrates that instead) to its real design-doc
    // effect: Shop prices are discounted for as long as this is owned. Checked directly by name
    // via ArtifactService.ApplyEmberCoreDiscount, wired into ReforgeService.Accept,
    // AugmentService.TryApplyAugment's Wisp tier, and EmberService.TryBuyEmber (Wares).
    public static Artifact CreateEmberCore()
    {
        return new Artifact
        {
            Name = "Ember Core",
            Description = "Reforge and Augment costs are reduced by 20% for the rest of the Delve.",
        };
    }

    // Checked directly by name at the ApplyDamage choke point in CombatEngine -- needs the
    // once-per-combat TwinFlameUsed flag on CombatState itself, which a stateless hook delegate
    // can't carry. Scoped to the player's own team only (see CombatEngine's own comment).
    public static Artifact CreateTwinFlame()
    {
        return new Artifact
        {
            Name = "Twin Flame",
            Description = "Once per combat, a hit that would reduce a player Anima to 0 HP or below instead leaves them at exactly 1 HP.",
        };
    }

    // Checked directly by name inside RewardService -- a flat +20% multiplier on any Wisp it
    // grants, for as long as this Artifact is owned. No combat/pickup hook needed.
    public static Artifact CreateWispCharm()
    {
        return new Artifact
        {
            Name = "Wisp Charm",
            Description = "+20% Wisp from all node rewards.",
        };
    }

    // Checked directly by name in CombatEngine.RoundStartPhase -- needs GrantShield/TriggerInspire's
    // own cap/share logic, which a plain hook delegate can't reach. Grants to the WHOLE owning
    // team (see CombatEngine's own comment for why whole-team over a single slot).
    public static Artifact CreateBarrierStone()
    {
        return new Artifact
        {
            Name = "Barrier Stone",
            Description = "At the start of each Round, the whole team gains 5 Shield.",
        };
    }

    // +1 starting Energy, ONCE at Round 1 -- not a recurring per-Round bonus (see CombatEngine's
    // StartCombat for the interpretation call). Via the existing OnCombatStart hook, same pattern
    // as Ember Core -- this is the hook's second (and first genuinely-invoked) use.
    public static Artifact CreateVanguardsBell()
    {
        return new Artifact
        {
            Name = "Vanguard's Bell",
            Description = "At the start of combat, gain 1 additional shared Energy (once, Round 1 only).",
            OnCombatStart = state => state.SharedEnergy += 1,
        };
    }

    // Checked directly by name via CombatEngine.HandMax -- needs TopUpHand's own draw-pile/
    // shuffle-recycle logic, which OnCombatStart's plain CombatState mutation can't express.
    public static Artifact CreateWeaversThread()
    {
        return new Artifact
        {
            Name = "Weaver's Thread",
            Description = "Hand size is increased by 1 (5 becomes 6).",
        };
    }

    // On pickup, immediately rolls a one-time bonus resource -- see
    // RewardService.GrantMarkedCoinBonus for the pool/weighting reasoning.
    public static Artifact CreateMarkedCoin()
    {
        return new Artifact
        {
            Name = "Marked Coin",
            Description = "On pickup, immediately grants one random bonus resource.",
            OnPickup = RewardService.GrantMarkedCoinBonus,
        };
    }

    // Checked directly via ArtifactService.OnNodeVisited -- a single consumable charge, removed
    // from RunLedger.Artifacts the moment it's consumed (wasted on a non-combat node, or used to
    // execute the lowest-current-HP enemy on a combat node). No hook field fits since the Run
    // layer that would call OnNodeVisited automatically doesn't exist yet.
    public static Artifact CreateWitheringFang()
    {
        return new Artifact
        {
            Name = "Withering Fang",
            Description = "Consumed on the next node visited. If that node has combat, sets the lowest-current-HP enemy to exactly 1 HP; otherwise wasted with no effect.",
        };
    }

    // Checked directly by name in CombatEngine.ResolvePlayerTurn/ResolveSingleTargetAttack --
    // needs CombatState.AttackSkillsPlayed's own per-combat counter, which a stateless hook
    // delegate can't carry (see CombatState/CombatEngine's own comments).
    public static Artifact CreateFocusingLens()
    {
        return new Artifact
        {
            Name = "Focusing Lens",
            Description = "Every 4th Attack-category skill played this combat deals double damage. The counter resets each fight.",
        };
    }

    // REDESIGNED (replaces the old "grants one chosen Anima an extra action" effect entirely --
    // see CombatEngine.TryConsumeSilentChimeCancel's own comment for the mechanic and CLAUDE.md
    // for why): fully automatic, no player action/targeting of any kind.
    public static Artifact CreateSilentChime()
    {
        return new Artifact
        {
            Name = "Silent Chime",
            Description = "Single-use per Delve. The first time an enemy would trigger Enrage, this shatters and cancels that Enrage activation entirely instead.",
        };
    }

    // Checked directly via ArtifactService.OnNodeVisited, the same "any node visit" checkpoint
    // Withering Fang uses -- see that method's own comment for why living-only (no revive) was
    // chosen for "heals the whole team."
    public static Artifact CreateSaplingCharm()
    {
        return new Artifact
        {
            Name = "Sapling Charm",
            Description = "Whenever the player enters any node, the whole team heals 10% of their max HP.",
        };
    }

    // Checked directly by name in CombatEngine.RoundStartPhase, right before TopUpHand -- needs a
    // player-choice hook (CombatEngine.ChooseSiftingStoneDiscards) rather than a plain hook field,
    // since "any number of cards, player's pick" isn't expressible as a fixed delegate mutation
    // the way e.g. Barrier Stone's flat Shield grant is.
    public static Artifact CreateSiftingStone()
    {
        return new Artifact
        {
            Name = "Sifting Stone",
            Description = "Before each Round's top-up draw, discard any number of cards from your hand -- the top-up draw replaces them along with whatever you played.",
        };
    }
}
