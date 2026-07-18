using Anima.Core.Enums;

namespace Anima.Core.Models;

public class Skill
{
    public required string Name { get; set; }
    public Part? Part { get; set; }
    public AnimaColor? Color { get; set; }
    public required SkillCategory Category { get; set; }
    public AttackRange Range { get; set; } = AttackRange.NA;
    public required TargetType Target { get; set; }
    public int EnergyCost { get; set; }
    public TriggerType Trigger { get; set; } = TriggerType.None;
    public DurationType Duration { get; set; } = DurationType.Instant;
    public int? DurationTurns { get; set; } // used only if Duration == FixedTurn
    public int BaseDamage { get; set; }
    public int BaseHeal { get; set; }
    public int BaseShield { get; set; }
    // Positional exceptions (e.g. Marked Shot's pos-3-only) handled via override fields:
    public int[]? UsableFromOverride { get; set; }
    public int[]? TargetPositionOverride { get; set; }

    // Secondary on-hit effects for Attack skills — narrow escape hatches, same pattern as the
    // override fields above, rather than a general effect pipeline:
    public string? OnHitStatusKeyword { get; set; } // e.g. Bash applying "Weak" to its target
    public int OnHitStatusMagnitude { get; set; }
    public DurationType OnHitStatusDuration { get; set; } = DurationType.Instant;
    public int? OnHitStatusDurationTurns { get; set; }
    public TargetType? SecondaryTarget { get; set; } // e.g. Smite healing LowestHpAlly alongside its Enemy attack target
    public double? SelfHealPercentOfDamage { get; set; } // e.g. Leech Mother's Draining Claw
    public double? SelfShieldPercentOfDamage { get; set; } // e.g. Guard Strike: Shield equal to damage dealt
    public bool RemovesDebuff { get; set; } // e.g. Cleanse: heal + strip one debuff from its target
    public bool RemovesBuff { get; set; } // e.g. Purge: damage + strip one buff from its target -- the offensive mirror of RemovesDebuff

    // HOT (heal-over-time) fields for Heal-category skills -- e.g. Renew. Distinct from
    // OnHitStatusKeyword above, which is Attack-only and applies to the ATTACK's own target.
    public string? HotKeyword { get; set; }
    public int HotMagnitude { get; set; }
    public int? HotDurationTurns { get; set; }

    // Bloom-style effect: an Attack skill that also refreshes an existing HOT on an ally (if one
    // is already active), without applying a fresh one if none exists -- same "refresh, don't
    // stack" spirit as Bleed's own refresh in ApplyOnHitStatus, just on the friendly side.
    public string? RefreshesHotKeyword { get; set; }
    public int? RefreshesHotDurationTurns { get; set; }

    // Dynamic-damage skill: BaseDamage is ignored and the caster's current Shield magnitude is
    // used instead (read at cast time, then the Shield is removed entirely) -- e.g. Shatter.
    public bool DamageEqualsOwnShield { get; set; }

    // Magnitude for non-Shield self-Buff skills (e.g. Retaliate/Thorns counter-damage amount).
    public int BuffMagnitude { get; set; }

    // Relative position shift, clamped to [1,3]. On a Move-category skill this shifts
    // skill.Target itself (e.g. Hook's Pull, -1). On an Attack-category skill it instead shifts
    // the ACTOR (e.g. Lunge moving itself forward) — distinct from TargetPositionOverride, which
    // is an absolute self-move (e.g. Retreat), and from TargetMoveOffset below, which shifts an
    // Attack skill's own target rather than its caster (e.g. Shove's Push, +1).
    public int? MoveOffset { get; set; }
    public int? TargetMoveOffset { get; set; }

    // Summon skills (e.g. Leech Mother's Spawn Brood) add a new combatant mid-fight — narrow
    // escape hatch, same pattern as the other side-effect fields above.
    public Func<Enemy>? SummonFactory { get; set; }

    // Random-choice summon (e.g. Warden of the Hollow's 50/50 add roll): when set, ResolveSummon
    // picks uniformly at random among these instead of using SummonFactory directly, via
    // CombatEngine's own _random -- the same "explicit, documented exception" to the "no
    // randomness in combat" rule the deck shuffle already is.
    public Func<Enemy>[]? SummonFactoryChoices { get; set; }

    // Guard-style summon: the new combatant takes position 1, pushing the caster back to the
    // next open position, instead of the summon just filling the first open slot behind them.
    public bool SummonInFront { get; set; }
}
