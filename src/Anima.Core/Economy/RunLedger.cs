using Anima.Core.Models;

namespace Anima.Core.Economy;

// Delve-scoped state that resets when a Delve ends -- never persisted. Artifacts live here; this
// is where a future Run layer would add/remove them. Reforge's temporary part swaps deliberately
// do NOT live here either -- they live on DelveRun itself (DelveRun.SetReforgeOverride/
// GetEffectiveSkill), never mutating the target Anima instance, so they can't leak into the
// permanent genome or interfere with anything RunLedger tracks.
public sealed class RunLedger
{
    public List<Artifact> Artifacts { get; } = new();

    // Wisp balance snapshot the caller (a future Run layer) takes from PersistentLedger the moment
    // a Delve begins. DelveEndService diffs the CURRENT balance against this to isolate Wisp earned
    // DURING this run -- Defeat's 50%-keep and Retreat's 100%-keep penalties apply only to that
    // delta, never touching whatever was already banked before the Delve started. Defaults to 0,
    // which is harmless for any caller that never sets it (e.g. existing tests) since a fresh
    // RunLedger's PersistentLedger is typically also empty at that point.
    public int WispAtDelveStart { get; set; }
}
