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
}
