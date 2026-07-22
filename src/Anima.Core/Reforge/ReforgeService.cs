namespace Anima.Core.Reforge;

using Anima.Core.Economy;
using Anima.Core.Enums;
using Anima.Core.Models;
using Anima.Core.Run;
using AnimaUnit = Anima.Core.Models.Anima;

// REDESIGNED -- replaces the old RollOffer-then-Accept random-reveal flow entirely. There is no
// random part roll anymore: the player picks an Aspect (Head/Frame/Tail -- Crest is out of scope,
// see below), browses every valid skill for it across all 4 colors, picks one deterministically,
// then picks which of their 3 team Anima to apply it to. Accept commits a run-scoped override on
// DelveRun (see DelveRun.SetReforgeOverride) -- the target Anima's own Head/Frame/Tail fields are
// never touched, so the swap can never leak past this Delve into the permanent, persisted genome.
public static class ReforgeService
{
    public const int SameColorAcceptCost = 40;
    public const int DifferentColorAcceptCost = 80;

    // The browse list for a given Aspect: every skill in ReforgePartPool.All valid for that Part
    // (cross-color, one per Archetype -- the real Part<->Archetype mapping already established by
    // SkillPool/PrimitiveRoster, reused here rather than inventing a second one), excluding
    // whatever the target currently has in that slot. "Currently has" respects any Reforge
    // override already Accepted for (target, part) earlier this same Delve (via
    // DelveRun.GetEffectiveSkill) so re-browsing after an Accept doesn't just re-offer the skill
    // you already picked. Crest is deliberately never offered -- it contributes no deck cards
    // (Models.Anima.DeckSkills), so it's out of Reforge's scope entirely, not merely unimplemented.
    public static IReadOnlyList<ReforgeCandidate> GetBrowseOptions(Part part, AnimaUnit target, DelveRun? run = null)
    {
        if (part == Part.Crest)
        {
            throw new InvalidOperationException("Reforge does not offer Crest -- it contributes no deck cards.");
        }

        var currentSkill = run?.GetEffectiveSkill(target, part) ?? GetBaseSkill(target, part);

        return ReforgePartPool.All
            .Where(c => c.Skill.Part == part && c.Skill.Name != currentSkill.Name)
            .ToList();
    }

    private static Skill GetBaseSkill(AnimaUnit anima, Part part) => part switch
    {
        Part.Head => anima.Head,
        Part.Frame => anima.Frame,
        Part.Tail => anima.Tail,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Reforge only supports Head/Frame/Tail."),
    };

    // 40 Wisp if the picked skill's color matches the target's own body color, 80 otherwise.
    // BUGFIX: hybrid Anima (Vulcan/Mirage) ALWAYS cost 80, regardless of which color is picked --
    // a hybrid has no single true body color to match against, so "same-color" can never be true
    // for one. (An earlier version of this method treated a pick matching either of the hybrid's
    // two component colors as a same-color match, which was wrong -- corrected here.)
    public static int GetAcceptCost(Skill skill, AnimaUnit target) =>
        IsColorMatch(skill.Color, target.Color) ? SameColorAcceptCost : DifferentColorAcceptCost;

    private static bool IsColorMatch(AnimaColor? skillColor, AnimaColor targetColor)
    {
        if (skillColor is null) return false;
        if (targetColor is AnimaColor.Vulcan or AnimaColor.Mirage) return false;

        return skillColor == targetColor;
    }

    // Checks affordability and deducts the cost (discounted by Ember Core, if owned -- see
    // ArtifactService.ApplyEmberCoreDiscount) from ledger BEFORE recording anything -- returns
    // false (leaving ledger/run untouched) if the player can't afford it. On success, records the
    // swap as a run-scoped override on `run` (DelveRun.SetReforgeOverride) -- NEVER mutates
    // `target` directly. Any Augment on the replaced skill is simply not carried over: the
    // override is a fresh clone of the picked skill, independent of whatever instance the target
    // used to have equipped.
    public static bool Accept(DelveRun run, AnimaUnit target, Part part, Skill chosenSkill, PersistentLedger ledger, RunLedger? runLedger = null)
    {
        var cost = ArtifactService.ApplyEmberCoreDiscount(GetAcceptCost(chosenSkill, target), runLedger);
        if (!ledger.TrySpend(ResourceType.Wisp, cost)) return false;

        run.SetReforgeOverride(target, part, chosenSkill.Clone());
        return true;
    }
}
