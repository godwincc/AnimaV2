using Anima.Core.Enums;

namespace Anima.Core.Models;

public class Anima : ICombatant
{
    public required string Id { get; set; }

    // Player-facing, renameable (see the Anima Profile screen's own rename icon) -- distinct from
    // Id, which is the stable machine identity ParentAId/ParentBId and sibling-restriction checks
    // key off. Every pre-existing SampleAnimas factory sets this equal to its own Id (their
    // "hardcoded names, no prompt" per CLAUDE.md); AnimaMaterializationService is what actually
    // sources this from the player's naming-prompt input for Weave/Boss-hatch Animas.
    public required string Name { get; set; }

    // Founders (starter trio, every other Primitive, Boss-hatch) are always Gen 1 -- no parents to
    // derive from. Weave-produced Animas are max(parentA.Gen, parentB.Gen) + 1, computed by
    // AnimaMaterializationService.Create, not here (this class has no knowledge of its own
    // lineage's Gen values, only WeavingResult/parent lookups do).
    public required int Gen { get; set; }

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

    public string DisplayName => Name;
    public int MaxHp => BaseStats.MaxHp;
    public int Speed => BaseStats.Speed;
    public int Defense => BaseStats.Defense;
}
