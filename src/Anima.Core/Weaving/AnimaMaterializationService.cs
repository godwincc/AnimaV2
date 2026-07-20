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

    // BUGFIX (found during the Phase 1 server-porting audit, predates this session): this used to
    // only carry each part's Dominant through, silently dropping R1/R2 the instant a Vessel
    // materialized. That's not just a display gap -- a materialized Anima that later becomes a
    // parent needs its OWN R1/R2 for the 6-gene weighted pool (WeavingService.ResolvePart) to work
    // at all, so every 2nd-generation Weave was silently drawing from a broken (Dominant-only)
    // pool until this fix. Now carries all three genes per part through, exactly as resolved by
    // WeavingService.Weave/BossHatchService.Roll -- see GenomeFactory.ExtractGenome for the
    // reverse direction (rebuilding an AnimaGenome from these fields for this Anima's own future
    // Weaves as a parent).
    private static AnimaUnit Build(AnimaGenome genome, Stats stats, string name, int gen, string? parentAId, string? parentBId) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        Gen = gen,
        Color = genome.Color,
        BaseStats = stats,
        Head = genome.Head.Dominant.Clone(),
        HeadR1 = genome.Head.R1.Clone(),
        HeadR2 = genome.Head.R2.Clone(),
        Frame = genome.Frame.Dominant.Clone(),
        FrameR1 = genome.Frame.R1.Clone(),
        FrameR2 = genome.Frame.R2.Clone(),
        Tail = genome.Tail.Dominant.Clone(),
        TailR1 = genome.Tail.R1.Clone(),
        TailR2 = genome.Tail.R2.Clone(),
        Crest = genome.Crest.Dominant.Clone(),
        CrestR1 = genome.Crest.R1.Clone(),
        CrestR2 = genome.Crest.R2.Clone(),
        ParentAId = parentAId,
        ParentBId = parentBId,
        CurrentHp = stats.MaxHp,
        Position = 1,
    };
}
