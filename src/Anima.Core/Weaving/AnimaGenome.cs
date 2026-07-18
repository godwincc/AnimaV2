using Anima.Core.Enums;

namespace Anima.Core.Weaving;

// An Anima's full breeding-relevant genetic makeup: its color, plus a PartGenome (Dominant/R1/R2)
// for each of the 4 part slots. Distinct from Models.Anima, which only carries the manifested
// (Dominant) skills actually played in combat -- this is the hidden data Weaving needs.
public sealed class AnimaGenome
{
    public required AnimaColor Color { get; init; }
    public required PartGenome Head { get; init; }
    public required PartGenome Frame { get; init; }
    public required PartGenome Tail { get; init; }
    public required PartGenome Crest { get; init; }

    public PartGenome GetPart(Part part) => part switch
    {
        Part.Head => Head,
        Part.Frame => Frame,
        Part.Tail => Tail,
        Part.Crest => Crest,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
    };

    // The Hybrid Trigger's "fully pure" precondition: every Dominant part's own color matches
    // this genome's overall Color. R1/R2 are irrelevant to purity -- only what's manifested counts.
    public bool IsFullyPure =>
        Head.Dominant.Color == Color &&
        Frame.Dominant.Color == Color &&
        Tail.Dominant.Color == Color &&
        Crest.Dominant.Color == Color;
}
