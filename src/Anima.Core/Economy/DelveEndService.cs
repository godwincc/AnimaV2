namespace Anima.Core.Economy;

// Resolves the two non-Boss-Victory ways a Delve can end: Defeat (wipe) and Retreat (voluntary
// exit). Both share the same "isolate Wisp earned THIS run, apply a keep fraction, claw back the
// rest" shape -- see RunLedger.WispAtDelveStart's own comment for why the diff is computed that
// way instead of tracking earned Wisp incrementally at every grant call site. Boss Victory has no
// penalty to resolve (its "Delve Complete" state is a pure reward continuation, not covered here).
//
// Artifacts (RunLedger.Artifacts) are never touched by either method -- they're already run-only
// state that a caller simply stops carrying forward once a Delve ends (a fresh RunLedger backs the
// next Delve), so there's nothing to claw back or clear. Echo/Vessel Shards are always kept in
// full, no penalty ever, per the locked design -- also nothing to do here, since they're granted
// straight into PersistentLedger with no run-scoped tracking to begin with.
public static class DelveEndService
{
    // Locked by the Match Result & Retreat System design session, Option B: a wipe keeps exactly
    // half of the Wisp earned during the run that just ended.
    public const double DefeatWispKeepFraction = 0.5;

    public readonly record struct DelveEndResult(int WispEarnedThisRun, int WispKept, int WispForfeited);

    // Defeat: keep DefeatWispKeepFraction of this-run Wisp; the rest is spent back out of the
    // ledger. Wisp already banked before the Delve started (everything below
    // runLedger.WispAtDelveStart) is untouched either way.
    public static DelveEndResult ResolveDefeat(PersistentLedger ledger, RunLedger runLedger)
    {
        var earned = Math.Max(0, ledger.GetBalance(ResourceType.Wisp) - runLedger.WispAtDelveStart);
        var kept = (int)Math.Round(earned * DefeatWispKeepFraction);
        var forfeited = earned - kept;
        if (forfeited > 0) ledger.TrySpend(ResourceType.Wisp, forfeited);
        return new DelveEndResult(earned, kept, forfeited);
    }

    // Retreat: 0% penalty -- keep 100% of this-run Wisp, ledger untouched. Deliberately better than
    // a wipe's 50% keep so Retreat reads as a genuine "cash out" choice, not a soft-fail, per the
    // locked design. Returns the same result shape as ResolveDefeat so a shared result-screen
    // component can read WispEarnedThisRun/WispKept identically regardless of which end-state
    // produced it (Retreat's WispForfeited is always 0).
    public static DelveEndResult ResolveRetreat(PersistentLedger ledger, RunLedger runLedger)
    {
        var earned = Math.Max(0, ledger.GetBalance(ResourceType.Wisp) - runLedger.WispAtDelveStart);
        return new DelveEndResult(earned, earned, 0);
    }
}
