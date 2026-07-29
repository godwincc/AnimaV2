using Anima.Core.Data;
using Anima.Core.Enums;
using Anima.Core.Models;

namespace Anima.Core.Weaving;

// One rolled starter-trio slot: which archetype was picked (the naming step's default, e.g.
// "Lotus" -- NOT the color name) plus its full genome. ArchetypeName has to travel alongside the
// genome because AnimaGenome itself has no notion of "archetype," only Color + per-part Dominant/
// R1/R2 skills -- the archetype identity would otherwise be lost the moment the genome is built.
public sealed record StarterAnimaRoll(string ArchetypeName, AnimaGenome Genome);

// Replaces the old hardcoded Ember/Boulder/Sprout starter trio with a real roll, run once per new
// account (see Server / Accounts / Auth: AuthService.RegisterAsync). Each of the 3 slots is
// genuinely random (which archetype, and R1/R2) but every slot's Dominant kit is a real, complete,
// already-designed archetype -- never an arbitrary skill-by-skill mix -- so a new player's very
// first 3 Anima always feel like intentional, coherent kits.
public static class StarterAnimaService
{
    // Presentation order: Crimson/Onyx/Verdant. Azure is deliberately excluded -- locked by the
    // task brief (3 starter slots, not 4 colors), same kind of "correct-on-paper, doesn't need to
    // cover everything" call as Reforge's 0%-then-5% Reforge entry or EarlyFloorTypeOdds' inert
    // Shop share elsewhere in this codebase.
    private static readonly AnimaColor[] StarterColors = [AnimaColor.Crimson, AnimaColor.Onyx, AnimaColor.Verdant];

    public static IReadOnlyList<StarterAnimaRoll> RollStarterTrio(Random rng) =>
        StarterColors.Select(color => RollOne(color, rng)).ToList();

    private static StarterAnimaRoll RollOne(AnimaColor color, Random rng)
    {
        // One of that color's 3 archetypes, uniform -- PrimitiveRoster.All is the same "12
        // Archetypes across all 4 colors" table Reforge/SkillPool/the old hardcoded trio all
        // already read from (per the task brief: "the same table the old hardcoded trio and the
        // skill-list doc reference"), so this is a filter over existing data, not a new mapping.
        var archetypes = PrimitiveRoster.All
            .Select(entry => (entry.Name, Anima: entry.Factory()))
            .Where(entry => entry.Anima.Color == color)
            .ToList();
        var (archetypeName, archetype) = archetypes[rng.Next(archetypes.Count)];

        // Dominant is fully deterministic once the archetype is picked -- that archetype's real,
        // already-designed 4-skill set, cloned straight off the sample instance. No randomization
        // happens here; only R1/R2 (below) are rolled.
        PartGenome RollPart(Part part, Skill dominant) =>
            new(dominant.Clone(), BossHatchService.RollOneSkill(part, color, rng), BossHatchService.RollOneSkill(part, color, rng));

        var genome = new AnimaGenome
        {
            Color = color,
            Head = RollPart(Part.Head, archetype.Head),
            Frame = RollPart(Part.Frame, archetype.Frame),
            Tail = RollPart(Part.Tail, archetype.Tail),
            Crest = RollPart(Part.Crest, archetype.Crest),
        };

        // Every Dominant here is that same archetype's own skill, whose Color is always the
        // archetype's single body Color (== this genome's Color, by construction) -- so
        // IsFullyPure is always true for a starter roll, not merely probable. Confirmed
        // structurally identical to a Weave/Boss-hatch genome: same AnimaGenome/PartGenome shape,
        // real R1/R2 on every part, no special-casing needed anywhere downstream (GenomeFactory,
        // AnimaMaterializationService, IsFullyPure) -- same standard BossHatchService's own doc
        // comment holds itself to.
        return new StarterAnimaRoll(archetypeName, genome);
    }
}
