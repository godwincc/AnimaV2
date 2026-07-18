namespace Anima.Core.Reforge;

using Anima.Core.Enums;
using AnimaUnit = Anima.Core.Models.Anima;

public static class ReforgeService
{
    public const int BaseAcceptCost = 40;        // roll from all 4 colors
    public const int ChooseColorAcceptCost = 80;  // roll narrowed to one chosen color

    // Rolls one random part. Pass chosenColor to narrow the pool to that color (the paid-for
    // premium option) -- still random which Archetype/skill within it. Wisp payment itself is
    // the caller's responsibility (no run-economy/Wisp-ledger type exists yet); this only prices
    // the eventual Accept via ReforgeOffer.AcceptCost.
    public static ReforgeOffer RollOffer(Random rng, AnimaColor? chosenColor = null)
    {
        var pool = chosenColor is { } color
            ? ReforgePartPool.All.Where(c => c.Skill.Color == color).ToList()
            : ReforgePartPool.All;

        if (pool.Count == 0) throw new InvalidOperationException($"No Reforge candidates available for color {chosenColor}.");

        var candidate = pool[rng.Next(pool.Count)];
        return new ReforgeOffer(candidate, chosenColor is not null);
    }

    // Swaps the offer's part onto target's matching slot. Run-only mutation -- caller reverts it
    // when the Delve ends, this never touches permanent/saved Anima data (there's nowhere to
    // persist it to yet regardless). Any Augment on the replaced skill is lost: Augments mutate a
    // Skill instance's fields in place elsewhere in the codebase, and that instance is simply
    // dropped here in favor of a fresh clone of the rolled one.
    public static void Accept(ReforgeOffer offer, AnimaUnit target)
    {
        var skill = offer.Candidate.Skill.Clone();
        switch (skill.Part)
        {
            case Part.Head:
                target.Head = skill;
                break;
            case Part.Frame:
                target.Frame = skill;
                break;
            case Part.Tail:
                target.Tail = skill;
                break;
            default:
                throw new InvalidOperationException($"Reforge only supports Head/Frame/Tail parts, got {skill.Part}.");
        }
    }
}
