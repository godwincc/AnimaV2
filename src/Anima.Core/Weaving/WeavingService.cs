using Anima.Core.Enums;
using Anima.Core.Models;

namespace Anima.Core.Weaving;

public static class WeavingService
{
    private const double DominantWeight = 0.375;
    private const double R1Weight = 0.09375;
    private const double R2Weight = 0.03125;
    private const double MutationChance = 0.10;
    private const double HybridTriggerChance = 0.33;

    // The two locked hybrid pairings, both directions (parent order doesn't matter).
    private static readonly Dictionary<(AnimaColor, AnimaColor), AnimaColor> HybridPairings = new()
    {
        [(AnimaColor.Onyx, AnimaColor.Crimson)] = AnimaColor.Vulcan,
        [(AnimaColor.Crimson, AnimaColor.Onyx)] = AnimaColor.Vulcan,
        [(AnimaColor.Verdant, AnimaColor.Azure)] = AnimaColor.Mirage,
        [(AnimaColor.Azure, AnimaColor.Verdant)] = AnimaColor.Mirage,
    };

    // Mirrors CLAUDE.md's locked Colors table (including the two hybrids) -- an offspring's base
    // stats are entirely determined by whichever color it resolves to.
    public static readonly IReadOnlyDictionary<AnimaColor, Stats> ColorStats = new Dictionary<AnimaColor, Stats>
    {
        [AnimaColor.Crimson] = new() { MaxHp = 100, Defense = 7, Speed = 10, DamageMultiplier = 1.3, SpiritMultiplier = 0.7 },
        [AnimaColor.Onyx] = new() { MaxHp = 130, Defense = 13, Speed = 7, DamageMultiplier = 1.0, SpiritMultiplier = 0.8 },
        [AnimaColor.Verdant] = new() { MaxHp = 100, Defense = 10, Speed = 10, DamageMultiplier = 0.7, SpiritMultiplier = 1.3 },
        [AnimaColor.Azure] = new() { MaxHp = 70, Defense = 10, Speed = 13, DamageMultiplier = 1.0, SpiritMultiplier = 1.0 },
        [AnimaColor.Vulcan] = new() { MaxHp = 143, Defense = 10, Speed = 6, DamageMultiplier = 1.3, SpiritMultiplier = 0.7 },
        [AnimaColor.Mirage] = new() { MaxHp = 60, Defense = 10, Speed = 13, DamageMultiplier = 0.7, SpiritMultiplier = 1.4 },
    };

    public static WeavingResult Weave(AnimaGenome parentA, AnimaGenome parentB, Random rng)
    {
        var hybridColor = TryTriggerHybrid(parentA, parentB, rng);
        var color = hybridColor ?? ResolveColor(parentA.Color, parentB.Color, rng);

        var head = ResolvePart(Part.Head, parentA.Head, parentB.Head, rng);
        var frame = ResolvePart(Part.Frame, parentA.Frame, parentB.Frame, rng);
        var tail = ResolvePart(Part.Tail, parentA.Tail, parentB.Tail, rng);
        var crest = ResolvePart(Part.Crest, parentA.Crest, parentB.Crest, rng);

        var genome = new AnimaGenome
        {
            Color = color,
            Head = new PartGenome(head.Dominant, head.R1, head.R2),
            Frame = new PartGenome(frame.Dominant, frame.R1, frame.R2),
            Tail = new PartGenome(tail.Dominant, tail.R1, tail.R2),
            Crest = new PartGenome(crest.Dominant, crest.R1, crest.R2),
        };

        return new WeavingResult(genome, ColorStats[color], hybridColor is not null, [head, frame, tail, crest]);
    }

    // Checked before normal color resolution, overriding it if triggered. Per-part resolution
    // below is NOT skipped for a hybrid offspring -- it runs the identical weighted-roll logic,
    // just tagged with the hybrid's own locked stats. This is a deliberate placeholder (per the
    // brief): hybrid-specific part/breeding behavior isn't designed yet, so reusing the normal
    // roll is the least-invented option available. Flag to the user if that needs real design.
    private static AnimaColor? TryTriggerHybrid(AnimaGenome parentA, AnimaGenome parentB, Random rng)
    {
        if (!parentA.IsFullyPure || !parentB.IsFullyPure) return null;
        if (!HybridPairings.TryGetValue((parentA.Color, parentB.Color), out var hybridColor)) return null;
        return rng.NextDouble() < HybridTriggerChance ? hybridColor : null;
    }

    private static AnimaColor ResolveColor(AnimaColor a, AnimaColor b, Random rng)
    {
        if (a == b) return a;
        return rng.NextDouble() < 0.5 ? a : b;
    }

    private static PartResolution ResolvePart(Part part, PartGenome parentA, PartGenome parentB, Random rng)
    {
        var candidates = new List<(GeneSource Source, Skill Skill, double Weight)>
        {
            (GeneSource.ParentADominant, parentA.Dominant, DominantWeight),
            (GeneSource.ParentAR1, parentA.R1, R1Weight),
            (GeneSource.ParentAR2, parentA.R2, R2Weight),
            (GeneSource.ParentBDominant, parentB.Dominant, DominantWeight),
            (GeneSource.ParentBR1, parentB.R1, R1Weight),
            (GeneSource.ParentBR2, parentB.R2, R2Weight),
        };

        // Draw 3 without replacement: the winner becomes the offspring's new Dominant (what
        // actually manifests); the next two fill its own hidden R1/R2, each independently subject
        // to the 10% Mutation chance below (the Dominant draw itself never is).
        var draws = WeightedSampleWithoutReplacement(candidates, 3, rng);

        var dominant = draws[0];
        var r1 = ApplyMutation(draws[1], part, rng);
        var r2 = ApplyMutation(draws[2], part, rng);

        return new PartResolution(
            part,
            dominant.Skill.Clone(), dominant.Source,
            r1.Skill, r1.Source,
            r2.Skill, r2.Source);
    }

    private static (GeneSource Source, Skill Skill) ApplyMutation(
        (GeneSource Source, Skill Skill, double Weight) draw, Part part, Random rng)
    {
        if (rng.NextDouble() < MutationChance)
        {
            return (GeneSource.Mutation, SkillPool.RollRandom(part, rng));
        }
        return (draw.Source, draw.Skill.Clone());
    }

    // Weighted sampling without replacement: each draw is weighted by the ORIGINAL weights of
    // whatever candidates remain (renormalized by construction, since a plain weighted pick
    // already only considers the remaining pool's own weights).
    private static List<(GeneSource Source, Skill Skill, double Weight)> WeightedSampleWithoutReplacement(
        List<(GeneSource Source, Skill Skill, double Weight)> candidates, int count, Random rng)
    {
        var pool = new List<(GeneSource Source, Skill Skill, double Weight)>(candidates);
        var result = new List<(GeneSource Source, Skill Skill, double Weight)>();

        for (var i = 0; i < count; i++)
        {
            var total = pool.Sum(c => c.Weight);
            var roll = rng.NextDouble() * total;
            var cumulative = 0.0;
            var pickedIndex = pool.Count - 1;
            for (var j = 0; j < pool.Count; j++)
            {
                cumulative += pool[j].Weight;
                if (roll <= cumulative)
                {
                    pickedIndex = j;
                    break;
                }
            }
            result.Add(pool[pickedIndex]);
            pool.RemoveAt(pickedIndex);
        }

        return result;
    }
}
