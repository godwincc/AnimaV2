namespace Anima.Core.Weaving;

// Which of the 6 weighted candidates (each parent's Dominant/R1/R2) won a gene roll, or Mutation
// if an R1/R2 roll's independent 10% mutation chance overrode the inherited result. Dominant
// rolls can never land on Mutation -- see WeavingService.ResolvePart.
public enum GeneSource
{
    ParentADominant,
    ParentAR1,
    ParentAR2,
    ParentBDominant,
    ParentBR1,
    ParentBR2,
    Mutation,
}
