using Anima.Core.Enums;
using Anima.Core.Models;

namespace Anima.Core.Weaving;

// Locked by the "Boss Reward Restructure + Anima Reveal Screen" design session: Boss's guaranteed
// reward is a real hatched Anima, rolled fresh with no parents/lineage involved (distinct from
// Weaving's ResolvePart, which always draws from two known parent genomes).
//
// Returns a full AnimaGenome (Dominant+R1+R2 per part), not a bespoke Dominant-only shape -- locked
// by the follow-up "Anima Materialization" session specifically so Boss-hatch Animas are fully
// Weave-eligible later with no special-casing anywhere downstream (WeavingService.ResolvePart just
// needs a PartGenome per parent; it has no idea -- or need to know -- whether that PartGenome came
// from real lineage or a Boss-hatch roll). Dominant/R1/R2 are three fully independent (Color,
// Skill) rolls, each using the identical 55/15/15/15 weighting toward the overall body Color --
// unlike Weaving's own gene draw, there's no ordering/rarity relationship between them (no
// lineage to weight one over another), so "R1/R2" here just means "the 2nd and 3rd independent
// rolls," not "less likely than Dominant."
public static class BossHatchService
{
    // Step 1: body Color, flat 25% each of the 4 base colors. Vulcan/Mirage are excluded entirely --
    // they're hybrid-only outcomes with no Primitives of their own to source a skill roll from (see
    // SkillPool.RollRandom's color-filtered overload), so there's nothing to roll even if a hybrid
    // body Color were allowed here. Locked by the design brief, not a judgment call.
    private static readonly AnimaColor[] BaseColors =
        [AnimaColor.Crimson, AnimaColor.Onyx, AnimaColor.Verdant, AnimaColor.Azure];

    private static readonly Part[] AllParts = [Part.Head, Part.Frame, Part.Tail, Part.Crest];

    // Step 2: each individual skill roll's own Color -- 55% the body's own Color, 15% each of the
    // other 3. Locked by the design brief, not a judgment call. Applied identically for Dominant
    // and the synthesized R1/R2.
    private const double PartColorMatchWeight = 0.55;
    private const double PartColorOtherWeight = 0.15;

    public static AnimaGenome Roll(Random rng)
    {
        var bodyColor = BaseColors[rng.Next(BaseColors.Length)];

        return new AnimaGenome
        {
            Color = bodyColor,
            Head = RollPart(Part.Head, bodyColor, rng),
            Frame = RollPart(Part.Frame, bodyColor, rng),
            Tail = RollPart(Part.Tail, bodyColor, rng),
            Crest = RollPart(Part.Crest, bodyColor, rng),
        };
    }

    private static PartGenome RollPart(Part part, AnimaColor bodyColor, Random rng) =>
        new(RollOneSkill(part, bodyColor, rng), RollOneSkill(part, bodyColor, rng), RollOneSkill(part, bodyColor, rng));

    private static Skill RollOneSkill(Part part, AnimaColor bodyColor, Random rng)
    {
        var skillColor = RollPartColor(bodyColor, rng);
        return SkillPool.RollRandom(part, skillColor, rng);
    }

    private static AnimaColor RollPartColor(AnimaColor bodyColor, Random rng)
    {
        var roll = rng.NextDouble();
        var cumulative = 0.0;
        foreach (var color in BaseColors)
        {
            cumulative += color == bodyColor ? PartColorMatchWeight : PartColorOtherWeight;
            if (roll < cumulative) return color;
        }
        return BaseColors[^1]; // floating-point safety net -- unreachable in practice, weights sum to 1.0
    }
}
