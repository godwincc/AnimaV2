using Anima.Core.Enums;
using Anima.Core.Models;

namespace Anima.Core.Weaving;

// Diagnostic detail for one resolved part slot: the winning Dominant plus its source, and the two
// filled-in R1/R2 genes plus whichever source each of THEM resolved to (a parent+slot, or
// Mutation). Exists so callers/tests can verify the actual roll distribution, not just the final
// skill each slot ended up with.
public sealed record PartResolution(
    Part Part,
    Skill Dominant, GeneSource DominantSource,
    Skill R1, GeneSource R1Source,
    Skill R2, GeneSource R2Source);
