using Anima.Core.Enums;
using Anima.Core.Models;
using AnimaUnit = Anima.Core.Models.Anima;

namespace Anima.Core.Weaving;

public static class WeavingService
{
    private const double DominantWeight = 0.375;
    private const double R1Weight = 0.09375;
    private const double R2Weight = 0.03125;
    private const double MutationChance = 0.10;
    private const double HybridTriggerChance = 0.33;
    private const double EchoTriggerChance = 0.05;

    // Locked curve: cost of a parent's 1st through 5th Weave use, indexed by that parent's
    // WeaveCount going into the attempt (0 -> 1st use's cost, ... 4 -> 5th use's cost). A parent
    // whose WeaveCount has reached this array's length has exhausted its 5 uses.
    private static readonly int[] WeaveCostCurve = [50, 100, 175, 275, 400];
    public const int MaxWeaveCount = 5;

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

    // Full attempt orchestration: eligibility (lineage + WeaveCount) checked and, if failed,
    // rejected before anything is rolled or charged. On success, runs one normal Weave, then --
    // unless forceEcho already committed to it -- rolls the 5% spontaneous Echo chance, but only
    // if that first Weave did NOT already hybrid-trigger (a separate roll from the 33% hybrid
    // check, gated behind it so a single attempt can't land both a hybrid AND a twin). If Echo
    // triggers (spontaneous or forced), a second, fully independent Weave is rolled for the twin
    // -- genuinely independent, not a copy of the first. Both parents' WeaveCount is incremented
    // by exactly 1 on success, regardless of whether Echo produced a twin.
    public static WeaveAttemptResult AttemptWeave(
        AnimaUnit parentA, AnimaUnit parentB,
        AnimaGenome genomeA, AnimaGenome genomeB,
        Random rng, bool forceEcho = false)
    {
        if (IsDirectParentChild(parentA, parentB))
            return WeaveAttemptResult.Rejected(WeaveRejectionReason.DirectParentChild);

        if (AreFullSiblings(parentA, parentB))
            return WeaveAttemptResult.Rejected(WeaveRejectionReason.FullSiblings);

        if (parentA.WeaveCount >= MaxWeaveCount || parentB.WeaveCount >= MaxWeaveCount)
            return WeaveAttemptResult.Rejected(WeaveRejectionReason.WeaveCountExhausted);

        var wispCost = GetWeaveCost(parentA.WeaveCount) + GetWeaveCost(parentB.WeaveCount);

        var primary = Weave(genomeA, genomeB, rng);

        WeavingResult? twin = null;
        var echoTriggered = forceEcho || (!primary.HybridTriggered && rng.NextDouble() < EchoTriggerChance);
        if (echoTriggered)
        {
            twin = Weave(genomeA, genomeB, rng);
        }

        parentA.WeaveCount++;
        parentB.WeaveCount++;

        return new WeaveAttemptResult(true, WeaveRejectionReason.None, wispCost, echoTriggered, primary, twin);
    }

    // A candidate is ineligible if it's a direct parent of the other (either of the other's
    // ParentAId/ParentBId matches its own Id). Founders (both Ids null) trivially never match.
    public static bool IsDirectParentChild(AnimaUnit a, AnimaUnit b) =>
        a.Id == b.ParentAId || a.Id == b.ParentBId || b.Id == a.ParentAId || b.Id == a.ParentBId;

    // Full siblings only -- both candidates must have BOTH parent slots populated with the exact
    // same pair of Ids (order-independent). Two founders (both null,null) are NOT siblings of each
    // other -- they simply have no recorded parentage.
    public static bool AreFullSiblings(AnimaUnit a, AnimaUnit b)
    {
        if (a.ParentAId is null || a.ParentBId is null || b.ParentAId is null || b.ParentBId is null)
            return false;

        return (a.ParentAId == b.ParentAId && a.ParentBId == b.ParentBId)
            || (a.ParentAId == b.ParentBId && a.ParentBId == b.ParentAId);
    }

    // Wisp cost of a single parent's Weave use at its current WeaveCount (0-indexed: 0 -> 1st
    // use). Caller is responsible for actually charging/deducting Wisp -- no run-economy/Wisp-
    // ledger type exists yet, same scope note as ReforgeService's Wisp-cost handling.
    public static int GetWeaveCost(int currentWeaveCount)
    {
        if (currentWeaveCount < 0 || currentWeaveCount >= WeaveCostCurve.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentWeaveCount), currentWeaveCount,
                $"WeaveCount must be in [0, {WeaveCostCurve.Length}) to have a defined cost.");
        }

        return WeaveCostCurve[currentWeaveCount];
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
