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
    // Single entry point for "give me this roster Anima's AnimaGenome so it can be a Weave
    // parent." Anima.HeadR1 being null is the reliable signal for "founder with no recorded
    // genome" (see Models.Anima's own comment) -- every Weave-produced or Boss-hatch Anima has
    // real HeadR1/HeadR2/etc. from AnimaMaterializationService.Build, and the starter trio (the
    // only Anima with none) is exactly what CreateFounderGenome's placeholder exists to cover.
    public static AnimaGenome CreateGenome(AnimaUnit anima) =>
        anima.HeadR1 is not null ? ExtractGenome(anima) : CreateFounderGenome(anima);

    // Rebuilds a real AnimaGenome from a materialized Anima's own stored Dominant/R1/R2 fields --
    // the reverse of AnimaMaterializationService.Build. Throws if any R1/R2 field is missing,
    // since a partially-populated genome would silently corrupt WeavingService's 6-gene weighted
    // pool rather than fail loudly; CreateGenome is what callers should actually use, since it
    // routes founders around this method entirely.
    public static AnimaGenome ExtractGenome(AnimaUnit anima)
    {
        if (anima.HeadR1 is null || anima.HeadR2 is null || anima.FrameR1 is null || anima.FrameR2 is null ||
            anima.TailR1 is null || anima.TailR2 is null || anima.CrestR1 is null || anima.CrestR2 is null)
        {
            throw new InvalidOperationException(
                $"Anima {anima.Id} has no complete recorded R1/R2 genome -- use CreateGenome (which falls back to CreateFounderGenome for founders) instead of calling this directly.");
        }

        return new AnimaGenome
        {
            Color = anima.Color,
            Head = new PartGenome(anima.Head, anima.HeadR1, anima.HeadR2),
            Frame = new PartGenome(anima.Frame, anima.FrameR1, anima.FrameR2),
            Tail = new PartGenome(anima.Tail, anima.TailR1, anima.TailR2),
            Crest = new PartGenome(anima.Crest, anima.CrestR1, anima.CrestR2),
        };
    }

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
