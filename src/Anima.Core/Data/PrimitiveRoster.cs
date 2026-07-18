namespace Anima.Core.Data;

using AnimaUnit = Anima.Core.Models.Anima;

// The 12 real Primitives (3 per color, one per Archetype) -- shared by anything that needs "the
// full roster across all 4 colors" (Reforge's cross-color part pool, Weaving's mutation pool,
// founder-genome construction, etc.). Bastion is deliberately excluded: it's a hybrid test build,
// not a real Primitive (see its own comment in SampleAnimas.cs). Not the final roster source once
// Weaving/breeding data is real, but it's the only one that exists right now.
public static class PrimitiveRoster
{
    public static IReadOnlyList<(string Name, Func<AnimaUnit> Factory)> All { get; } =
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
}
