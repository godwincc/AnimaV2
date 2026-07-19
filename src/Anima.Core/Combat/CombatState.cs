namespace Anima.Core.Combat;

using Anima.Core.Models;

public class CombatState
{
    public required List<Anima> PlayerTeam { get; set; }
    public required List<Enemy> EnemyTeam { get; set; }
    public int SharedEnergy { get; set; } = 3;
    public int RoundNumber { get; set; } = 1;
    public List<Skill> DrawPile { get; set; } = new();
    public List<Skill> Hand { get; set; } = new();
    public List<Skill> DiscardPile { get; set; } = new();

    // Twin Flame Artifact: set the first time it saves a player Anima from a lethal hit this
    // combat. Never reset mid-combat -- a fresh CombatState is constructed per combat, so this
    // naturally resets between fights with no extra plumbing needed.
    public bool TwinFlameUsed { get; set; }
}
