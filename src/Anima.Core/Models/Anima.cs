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

    // Weaving (breeding) lineage state: null for founders/un-bred starting Animas.
    public string? ParentAId { get; set; }
    public string? ParentBId { get; set; }
    public int WeaveCount { get; set; } = 0;

    // Combat runtime state:
    public int CurrentHp { get; set; }
    public int Position { get; set; } // 1, 2, or 3
    public List<StatusEffectInstance> ActiveStatuses { get; set; } = new();

    public Skill[] DeckSkills => new[] { Head, Frame, Tail }; // Crest excluded from deck

    public string DisplayName => Id;
    public int MaxHp => BaseStats.MaxHp;
    public int Speed => BaseStats.Speed;
    public int Defense => BaseStats.Defense;
}
