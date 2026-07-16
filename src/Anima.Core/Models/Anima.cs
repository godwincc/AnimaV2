using Anima.Core.Enums;

namespace Anima.Core.Models;

public class Anima : ICombatant
{
    public required string Id { get; set; }
    public required AnimaColor Color { get; set; }
    public required Stats BaseStats { get; set; }
    public required Skill Head { get; set; }
    public required Skill Frame { get; set; }
    public required Skill Tail { get; set; }
    public required Skill Crest { get; set; } // always active, not in the deck

    // Combat runtime state:
    public int CurrentHp { get; set; }
    public int Position { get; set; } // 1, 2, or 3
    public List<StatusEffectInstance> ActiveStatuses { get; set; } = new();

    public Skill[] DeckSkills => new[] { Head, Frame, Tail }; // Crest excluded from deck

    public string DisplayName => Id;
    public int Speed => BaseStats.Speed;
    public int Defense => BaseStats.Defense;
}
