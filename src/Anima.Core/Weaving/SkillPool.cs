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
