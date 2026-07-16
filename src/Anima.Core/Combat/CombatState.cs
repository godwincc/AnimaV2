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
}
