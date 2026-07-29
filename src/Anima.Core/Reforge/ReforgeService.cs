namespace Anima.Core.Reforge;

using Anima.Core.Economy;
using Anima.Core.Enums;
using Anima.Core.Models;
using Anima.Core.Run;
using AnimaUnit = Anima.Core.Models.Anima;

// REORDERED THIS SESSION -- flips the browse flow from Part-first to color-first: the player picks
// a color (Crimson/Onyx/Verdant/Azure -- called "Aspect" in the UI; see the terminology note below)
// FIRST, then browses every Head/Frame/Tail skill for that color, THEN picks which of their 3 team
// Anima to apply it to (Part is now fully determined by whichever skill they picked, so there's no
// longer a separate "pick a slot" step at all). Still no random roll anywhere in the flow. Accept
// commits a run-scoped override on DelveRun (see DelveRun.SetReforgeOverride) -- the target Anima's
// own Head/Frame/Tail fields are never touched, so the swap can never leak past this Delve into the
// permanent, persisted genome. Crest is still out of scope entirely (see GetBrowseOptionsByColor).
//
// TERMINOLOGY NOTE (flagged per the task, not a silent rename): "Aspect" now means the picked COLOR
// in the UI. The old design's first step was "pick an Aspect: Head, Frame, or Tail" -- that meaning
// is fully retired, not kept alongside the new one. Every comment/name in this file that used to
// mean the old thing has been updated to say "Part" explicitly, so nothing here still says "Aspect"
// meaning Head/Frame/Tail now that the UI says "Aspect" meaning color.
public static class ReforgeService
{
    public const int SameColorAcceptCost = 40;
    public const int DifferentColorAcceptCost = 80;

    // Step 1 of the reordered flow: every skill in ReforgePartPool.All for the picked color, across
    // all 3 Parts (Head/Frame/Tail -- Crest is still excluded at the pool level, see
    // ReforgePartPool's own comment, since it contributes no deck cards). Deliberately UNFILTERED --
    // no target-owned-skill exclusion here, since the target Anima isn't picked until the NEXT step
    // now. That exclusion moved to GetValidTargets/IsNoOpForTarget below.
    public static IReadOnlyList<ReforgeCandidate> GetBrowseOptionsByColor(AnimaColor color) =>
        ReforgePartPool.All.Where(c => c.Skill.Color == color).ToList();

    // Step 2's filter: is this specific skill a no-op for this specific Anima -- i.e. does the
    // Anima already have this exact skill equipped in that Part (whether from its own real genome,
    // or an already-Accepted Reforge override earlier this same Delve)? Used to skip/disable a
    // team Anima as an invalid target for a given pick, now that the old "exclude the target's
    // current skill" filter can no longer run at browse time (the target isn't known yet then).
    public static bool IsNoOpForTarget(AnimaUnit target, Skill skill, DelveRun? run)
    {
        var part = skill.Part ?? throw new ArgumentException("Reforge skills must have a Part.", nameof(skill));
        var currentSkill = run?.GetEffectiveSkill(target, part) ?? GetBaseSkill(target, part);
        return currentSkill.Name == skill.Name;
    }

    // The subset of `team` that are valid targets for `skill` -- i.e. everyone EXCEPT whoever it
    // would be a no-op for. Step 2 of the reordered flow: the client renders the 3 team Anima with
    // no-op ones skipped/disabled rather than offering a pointless swap.
    public static IReadOnlyList<AnimaUnit> GetValidTargets(IEnumerable<AnimaUnit> team, Skill skill, DelveRun? run) =>
        team.Where(a => !IsNoOpForTarget(a, skill, run)).ToList();

    private static Skill GetBaseSkill(AnimaUnit anima, Part part) => part switch
    {
        Part.Head => anima.Head,
        Part.Frame => anima.Frame,
        Part.Tail => anima.Tail,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Reforge only supports Head/Frame/Tail."),
    };

    // 40 Wisp if the picked skill's color matches the target's own body color, 80 otherwise.
    // BUGFIX (predates this session's reorder): hybrid Anima (Vulcan/Mirage) ALWAYS cost 80,
    // regardless of which color is picked -- a hybrid has no single true body color to match
    // against, so "same-color" can never be true for one. Re-verified this session (asked for
    // explicitly): this still works correctly now that color is chosen before the Anima -- the
    // check only ever needs both values at Accept time, and both are always available there
    // regardless of which order they were picked in during the flow.
    public static int GetAcceptCost(Skill skill, AnimaUnit target) =>
        IsColorMatch(skill.Color, target.Color) ? SameColorAcceptCost : DifferentColorAcceptCost;

    private static bool IsColorMatch(AnimaColor? skillColor, AnimaColor targetColor)
    {
        if (skillColor is null) return false;
        if (targetColor is AnimaColor.Vulcan or AnimaColor.Mirage) return false;

        return skillColor == targetColor;
    }

    public enum ReforgeAcceptOutcome
    {
        Success,
        InsufficientWisp,
    }

    // Distinct rejection shape for the insufficient-Wisp path (NEW this session) -- a generic
    // exception isn't enough here because the client needs the actual numbers ("needed X, have Y")
    // to show an explicit message and route back to the Reforge/Leave landing screen, not just know
    // that *something* failed. Deliberately different from the Shop/BuyWares* hub methods' existing
    // "throw HubException('Insufficient Wisp.')" convention -- asked for explicitly here, not a
    // blanket change to every other Wisp-spending flow in the codebase.
    public sealed record ReforgeAcceptResult(ReforgeAcceptOutcome Outcome, int Cost, int WispBalance)
    {
        public bool Success => Outcome == ReforgeAcceptOutcome.Success;
    }

    // Step 4 (confirm/Accept). Part is no longer a separate parameter -- it's fully derived from
    // chosenSkill.Part, since the reordered flow has no standalone "pick a slot" step anymore.
    // Checks affordability BEFORE spending or recording anything, and returns which case happened
    // rather than a bare bool, so nothing partially commits on the insufficient-Wisp path -- no
    // Wisp is deducted, no override is recorded, and no browse-choice state leaks into a later
    // Reforge attempt at the same node (this method holds no state of its own between calls; the
    // only state that could leak is DelveRun's override dictionary, which this path never touches).
    // `target` is assumed to already be a valid (non-no-op) pick -- i.e. the caller (GameHub) has
    // already filtered via GetValidTargets/IsNoOpForTarget before letting the player confirm one;
    // this method still defensively re-checks and throws if that invariant was violated, the same
    // "caller is untrusted, so this can no longer be an assumption" posture ResolvePlayerAction
    // already takes in CombatEngine.
    public static ReforgeAcceptResult Accept(DelveRun run, AnimaUnit target, Skill chosenSkill, PersistentLedger ledger, RunLedger? runLedger = null)
    {
        if (IsNoOpForTarget(target, chosenSkill, run))
        {
            throw new InvalidOperationException(
                "This skill is already equipped in that Part for this Anima -- the caller must exclude no-op targets before calling Accept (see GetValidTargets).");
        }

        var cost = ArtifactService.ApplyEmberCoreDiscount(GetAcceptCost(chosenSkill, target), runLedger);
        if (!ledger.CanAfford(ResourceType.Wisp, cost))
        {
            return new ReforgeAcceptResult(ReforgeAcceptOutcome.InsufficientWisp, cost, ledger.GetBalance(ResourceType.Wisp));
        }

        ledger.TrySpend(ResourceType.Wisp, cost);
        var part = chosenSkill.Part ?? throw new ArgumentException("Reforge skills must have a Part.", nameof(chosenSkill));
        run.SetReforgeOverride(target, part, chosenSkill.Clone());
        return new ReforgeAcceptResult(ReforgeAcceptOutcome.Success, cost, ledger.GetBalance(ResourceType.Wisp));
    }
}
