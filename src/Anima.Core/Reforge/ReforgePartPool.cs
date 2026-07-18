namespace Anima.Core.Reforge;

using Anima.Core.Data;

// The "full pool of all designed Archetype parts across all 4 colors" Reforge rolls from --
// every Head/Frame/Tail (Crest excluded) across the 12 real Primitives in PrimitiveRoster.
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
