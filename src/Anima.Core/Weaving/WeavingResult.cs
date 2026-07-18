using Anima.Core.Models;

namespace Anima.Core.Weaving;

public sealed record WeavingResult(
    AnimaGenome Genome,
    Stats Stats,
    bool HybridTriggered,
    IReadOnlyList<PartResolution> PartResolutions);
