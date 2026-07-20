using Anima.Core.Data;
using Anima.Core.Enums;
using Anima.Core.Models;

namespace Anima.Core.Weaving;

// Full cross-color skill pool Mutation rolls from -- one list per Part, built from the same 12
// real Primitives Reforge draws from (see PrimitiveRoster), but covering Crest too: Reforge only
// ever swaps Head/Frame/Tail, while Weaving's part slots include Crest.
public static class SkillPool
{
    public static IReadOnlyDictionary<Part, IReadOnlyList<Skill>> ByPart { get; } = Build();

    public static Skill RollRandom(Part part, Random rng)
    {
        var candidates = ByPart[part];
        return candidates[rng.Next(candidates.Count)].Clone();
    }

    // Color-filtered variant -- added for BossHatchService, which needs "1 random skill from color
    // X's pool for part P" with no second parent to weight against. ByPart's skills already each
    // carry their own Color (every Primitive's own 4 parts all share that Primitive's single body
    // Color -- see PrimitiveRoster), so this is a filter over existing data, not a new pool: 3
    // candidates per (Part, Color) pair (one per Archetype), never empty for any of the 4 base
    // colors. Hybrid colors (Vulcan/Mirage) have no Primitives and so no candidates -- callers must
    // only pass a base color, which BossHatchService's own roll already guarantees.
    public static Skill RollRandom(Part part, AnimaColor color, Random rng)
    {
        var candidates = ByPart[part].Where(s => s.Color == color).ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"No {color} skills exist in the {part} pool.");
        }
        return candidates[rng.Next(candidates.Count)].Clone();
    }

    private static IReadOnlyDictionary<Part, IReadOnlyList<Skill>> Build()
    {
        var head = new List<Skill>();
        var frame = new List<Skill>();
        var tail = new List<Skill>();
        var crest = new List<Skill>();

        foreach (var (_, factory) in PrimitiveRoster.All)
        {
            var anima = factory();
            head.Add(anima.Head);
            frame.Add(anima.Frame);
            tail.Add(anima.Tail);
            crest.Add(anima.Crest);
        }

        return new Dictionary<Part, IReadOnlyList<Skill>>
        {
            [Part.Head] = head,
            [Part.Frame] = frame,
            [Part.Tail] = tail,
            [Part.Crest] = crest,
        };
    }
}
