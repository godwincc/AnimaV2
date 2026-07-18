namespace Anima.Core.Reforge;

using Anima.Core.Data;
using AnimaUnit = Anima.Core.Models.Anima;

// The "full pool of all designed Archetype parts across all 4 colors" Reforge rolls from --
// every Head/Frame/Tail (Crest excluded) across the 12 real Primitives in SampleAnimas. Bastion
// is deliberately excluded: it's a hybrid test build, not a real Primitive (see its comment in
// SampleAnimas.cs). Not the final roster source once Weaving/breeding data exists for real, but
// it's the only roster that exists right now.
public static class ReforgePartPool
{
    public static IReadOnlyList<ReforgeCandidate> All { get; } = Build();

    private static List<ReforgeCandidate> Build()
    {
        (string Name, Func<AnimaUnit> Factory)[] archetypes =
        [
            ("Ember", SampleAnimas.CreateEmber),
            ("Reaper", SampleAnimas.CreateReaper),
            ("Marksman", SampleAnimas.CreateMarksman),
            ("Boulder", SampleAnimas.CreateBoulder),
            ("Aegis", SampleAnimas.CreateAegis),
            ("Warden", SampleAnimas.CreateWarden),
            ("Sprout", SampleAnimas.CreateSprout),
            ("Thicket", SampleAnimas.CreateThicket),
            ("Lotus", SampleAnimas.CreateLotus),
            ("Shade", SampleAnimas.CreateShade),
            ("Anchor", SampleAnimas.CreateAnchor),
            ("Veil", SampleAnimas.CreateVeil),
        ];

        var pool = new List<ReforgeCandidate>();
        foreach (var (name, factory) in archetypes)
        {
            var anima = factory();
            pool.Add(new ReforgeCandidate(name, anima.Head));
            pool.Add(new ReforgeCandidate(name, anima.Frame));
            pool.Add(new ReforgeCandidate(name, anima.Tail));
        }
        return pool;
    }
}
