namespace Anima.Core.Data;

using Anima.Core.Economy;
using Anima.Core.Models;

// SAMPLE / REFERENCE DATA — validates the Artifact + OnCombatStart/OnPickup hook patterns
// end-to-end, plus a first pass at the 6 real Delve-scoped (run-only, no cross-Delve persistence)
// Artifacts. Not the final itemization set. All 6 are checked purely by Name string -- either via
// a hook field (OnCombatStart/OnPickup, for effects a plain delegate can express) or a direct
// name check inside CombatEngine/RewardService (for effects needing engine-internal state/logic
// a delegate can't reach, e.g. DrawCards' pile-recycling or the once-per-combat Twin Flame flag)
// -- same "no event bus, direct checks at known checkpoints" pattern the rest of the codebase
// already uses for Crest passives.
public static class SampleArtifacts
{
    public static Artifact CreateEmberCore()
    {
        return new Artifact
        {
            Name = "Ember Core",
            Description = "At the start of combat, gain 1 additional shared energy.",
            OnCombatStart = state => state.SharedEnergy += 1,
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

    // Checked directly by name in CombatEngine.StartCombat -- needs DrawCards' own draw-pile/
    // shuffle-recycle logic, which OnCombatStart's plain CombatState mutation can't express.
    public static Artifact CreateWeaversThread()
    {
        return new Artifact
        {
            Name = "Weaver's Thread",
            Description = "The opening hand is dealt 1 extra card (7 becomes 8).",
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
}
