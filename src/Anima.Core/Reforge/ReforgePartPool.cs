namespace Anima.Core.Reforge;

using Anima.Core.Data;

// The "full pool of all designed Archetype parts across all 4 colors" Reforge's browse list draws
// from -- every Head/Frame/Tail (Crest excluded, since it contributes no deck cards and is out of
// Reforge's scope entirely) across the 12 real Primitives in PrimitiveRoster. This IS the real
// Part<->Archetype mapping ReforgeService.GetBrowseOptions filters by Part: PrimitiveRoster already
// ties each of the 12 Archetypes to one skill per Part (see SkillPool's identical construction),
// so no second mapping needed to be invented for the browse-list redesign.
public static class ReforgePartPool
{
    public static IReadOnlyList<ReforgeCandidate> All { get; } = Build();

    private static List<ReforgeCandidate> Build()
    {
        var pool = new List<ReforgeCandidate>();
        foreach (var (name, factory) in PrimitiveRoster.All)
        {
            var anima = factory();
            pool.Add(new ReforgeCandidate(name, anima.Head));
            pool.Add(new ReforgeCandidate(name, anima.Frame));
            pool.Add(new ReforgeCandidate(name, anima.Tail));
        }
        return pool;
    }
}
