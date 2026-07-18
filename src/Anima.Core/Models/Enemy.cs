using Anima.Core.Combat;

namespace Anima.Core.Models;

public class Enemy : ICombatant
{
    public required string Name { get; set; }
    public int MaxHp { get; set; }
    public int Defense { get; set; }
    public int CurrentHp { get; set; }
    public int Position { get; set; }
    public int Speed { get; set; }
    public List<StatusEffectInstance> ActiveStatuses { get; set; } = new();
    public required List<EnemyBehaviorRule> BehaviorRules { get; set; }
    public Dictionary<string, object> AiState { get; set; } = new();

    // Universal Elite/Boss safety net — forces a fight to resolve instead of stalemating.
    // EnrageDamageMultiplier is the bonus applied the Round Enrage triggers; every Round after
    // that, the bonus itself doubles (see CombatEngine.GetEnrageMultiplier), so a stalled fight
    // gets forced closed within a few Rounds rather than dragging indefinitely.
    public int? EnrageRound { get; set; } // null = no enrage; otherwise the Round number it triggers on
    public double EnrageDamageMultiplier { get; set; } = 1.75; // +75% default, at the triggering Round
    public bool IsEnraged { get; set; } = false;
    public int? EnrageTriggeredRound { get; set; } // Round Enrage actually flipped true; null until then

    // Boss Phase transition — a one-time, flat (non-escalating) damage buff triggered by an HP
    // threshold rather than a Round number. Deliberately kept separate from Enrage/
    // EnrageTriggeredRound above: this doesn't escalate, and firing it just sets IsEnraged would
    // also pull in GetEnrageMultiplier's doubling if EnrageTriggeredRound got set alongside it,
    // which a boss's Phase 2 buff should NOT do. See Warden of the Hollow.
    public int? PhaseTwoHpThreshold { get; set; } // null = no Phase 2; else the CurrentHp value it triggers at
    public double PhaseTwoDamageMultiplier { get; set; } = 1.0; // e.g. 1.5 for Warden's +50% Reckless Fury
    public bool PhaseTwoTriggered { get; set; } = false;
    public double PermanentDamageMultiplier { get; set; } = 1.0; // applied once Phase 2 (or similar) triggers

    // Turn-start heal-the-boss passive (e.g. Warden's DPS-race add draining into her each of its
    // own turns) — checked at the top of ResolveEnemyTurn, so it fires even on a turn where the
    // add itself is stunned or has no valid attack.
    public Enemy? HealsOnTurnStartTarget { get; set; }
    public int HealsOnTurnStartAmount { get; set; }

    // Fires once, the instant this enemy's HP first reaches 0 — e.g. an add rescheduling its
    // summoner's next-summon cooldown off its own death Round, distinct from PurgeDeadAnimaCards
    // (which is Anima-only bookkeeping for the shared deck).
    public Action<CombatState>? OnDeath { get; set; }

    public string DisplayName => Name;
}

public class EnemyBehaviorRule
{
    public required Func<Enemy, CombatState, bool> Condition { get; set; }
    public required Skill Skill { get; set; }
    // Fires after the skill resolves — used for AiState bookkeeping (e.g. Sentinel's charge telegraph).
    public Action<Enemy>? OnUsed { get; set; }
    // Defensive rules (Shield-granting, retreating, etc.) are skipped once the enemy is Enraged.
    public bool IsDefensive { get; set; } = false;
}
