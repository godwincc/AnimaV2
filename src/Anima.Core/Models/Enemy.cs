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

    public string DisplayName => Name;
}

public class EnemyBehaviorRule
{
    public required Func<Enemy, CombatState, bool> Condition { get; set; }
    public required Skill Skill { get; set; }
    // Fires after the skill resolves — used for AiState bookkeeping (e.g. Sentinel's charge telegraph).
    public Action<Enemy>? OnUsed { get; set; }
}
