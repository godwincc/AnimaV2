using Anima.Core.Models;

namespace Anima.Core.Weaving;

// One part slot's full genetic makeup: Dominant is what actually manifests (the skill the Anima
// plays); R1/R2 are hidden/inherited genes only relevant to this Anima's own future offspring.
public sealed record PartGenome(Skill Dominant, Skill R1, Skill R2);
