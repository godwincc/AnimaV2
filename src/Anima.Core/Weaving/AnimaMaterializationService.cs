using Anima.Core.Models;
using AnimaUnit = Anima.Core.Models.Anima;

namespace Anima.Core.Weaving;

// The missing piece both Weaving and Boss-hatch need to wire up their Reveal-screen Confirm
// action: turns a resolved genome (a Weave's WeavingResult, or a Boss-hatch AnimaGenome) plus a
// player-entered name into a real, playable Models.Anima with its own Id, and adds it to the
// SanctumRoster. Neither path had this before -- see WeavingResult's and BossHatchService's own
// (now-resolved) doc comments.
//
// Only the Dominant of each part manifests as the materialized Anima's actual Head/Frame/Tail/
// Crest (R1/R2 stay in the genome, for THIS Anima's own future offspring to draw from) -- same
// "Dominant is what's played, R1/R2 are hidden" split PartGenome already documents.
public static class AnimaMaterializationService
{
    // Weave-produced: Gen = max(parentA.Gen, parentB.Gen) + 1 (LOCKED this session), lineage
    // recorded via ParentAId/ParentBId. Stats come straight from WeavingResult.Stats -- already
    // resolved by WeavingService.Weave (ColorStats[color], correct even for a hybrid color).
    public static AnimaUnit Create(WeavingResult result, AnimaUnit parentA, AnimaUnit parentB, string name, SanctumRoster roster)
    {
        var gen = Math.Max(parentA.Gen, parentB.Gen) + 1;
        var anima = Build(result.Genome, result.Stats, name, gen, parentA.Id, parentB.Id);
        roster.Animas.Add(anima);
        return anima;
    }

    // Boss-hatch: no parents, Gen fixed at 1 -- wild origin, same tier as the starter trio (but
    // still goes through the naming prompt, unlike the trio's hardcoded names). Stats aren't
    // carried on a bare AnimaGenome (unlike WeavingResult), so this looks them up itself via the
    // same WeavingService.ColorStats table every other color-to-stats resolution already uses --
    // safe because BossHatchService.Roll only ever produces a base body Color, always present in
    // that table.
    public static AnimaUnit Create(AnimaGenome hatchedGenome, string name, SanctumRoster roster)
    {
        var stats = WeavingService.ColorStats[hatchedGenome.Color];
        var anima = Build(hatchedGenome, stats, name, gen: 1, parentAId: null, parentBId: null);
        roster.Animas.Add(anima);
        return anima;
    }

    private static AnimaUnit Build(AnimaGenome genome, Stats stats, string name, int gen, string? parentAId, string? parentBId) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Gen = gen,
        Color = genome.Color,
        BaseStats = stats,
        Head = genome.Head.Dominant.Clone(),
        Frame = genome.Frame.Dominant.Clone(),
        Tail = genome.Tail.Dominant.Clone(),
        Crest = genome.Crest.Dominant.Clone(),
        ParentAId = parentAId,
        ParentBId = parentBId,
        CurrentHp = stats.MaxHp,
        Position = 1,
    };
}
