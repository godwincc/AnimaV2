using Anima.Core.Data;
using Anima.Core.Models;
using AnimaUnit = Anima.Core.Models.Anima;

namespace Anima.Core.Weaving;

// Builds a breeding-ready genome for a sample/founder Primitive. There's no real lineage/pedigree
// data yet (Weaving's own data model is still 0% coded per CLAUDE.md), so this is a deliberate
// placeholder: R1/R2 for each slot are filled from the OTHER two same-color Primitives'
// matching-slot skills (e.g. Crimson Head: Ember's own Slash as Dominant, Reaper's Rend and
// Marksman's Snipe filling R1/R2) -- "this founder is a blend of its own color's known kit
// diversity." This keeps every founder genome trivially IsFullyPure (all Dominants already match
// the Anima's own color) without inventing any specific numeric/design data. Flag to the user if
// real founder-genome data exists elsewhere and should replace this.
public static class GenomeFactory
{
    public static AnimaGenome CreateFounderGenome(AnimaUnit anima)
    {
        var sameColorSiblings = PrimitiveRoster.All
            .Select(entry => entry.Factory())
            .Where(sibling => sibling.Color == anima.Color && sibling.Id != anima.Id)
            .ToList();

        if (sameColorSiblings.Count < 2)
        {
            throw new InvalidOperationException(
                $"Need at least 2 other same-color Primitives to fill R1/R2 for {anima.Id} ({anima.Color}), found {sameColorSiblings.Count}.");
        }

        PartGenome BuildPart(Func<AnimaUnit, Skill> select) =>
            new(select(anima), select(sameColorSiblings[0]), select(sameColorSiblings[1]));

        return new AnimaGenome
        {
            Color = anima.Color,
            Head = BuildPart(a => a.Head),
            Frame = BuildPart(a => a.Frame),
            Tail = BuildPart(a => a.Tail),
            Crest = BuildPart(a => a.Crest),
        };
    }
}
