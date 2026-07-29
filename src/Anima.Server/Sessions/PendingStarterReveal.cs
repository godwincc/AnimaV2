using Anima.Core.Weaving;

namespace Anima.Server.Sessions;

// Holds a freshly-registered account's 3 rolled starter Anima (StarterAnimaService.RollStarterTrio,
// rolled once at AuthService.RegisterAsync time) until ConfirmStarterAnima names all of them -- the
// same mandatory-naming, no-discard-path contract PendingWeave/PendingBossHatch already have. Fixed
// length 3, in StarterAnimaService's own fixed Crimson/Onyx/Verdant presentation order.
// NextUnnamedIndex tracks how many of the 3 slots have been confirmed so far (0..3) -- strictly
// sequential, no slot-index parameter on Confirm, matching the client's own "1 of 3, 2 of 3, 3 of 3"
// progress UI. Once it reaches 3, GameHub.ConfirmStarterAnima deletes the whole pending row, same
// as PendingWeave/PendingBossHatch clearing on their own final Confirm.
//
// ConfirmedAnimaIds (NEW) accumulates each slot's real materialized Anima.Id as it's confirmed --
// needed so the FINAL Confirm call can auto-assign the whole trio as the active team (a brand-new
// player should land on Hub with their starter trio already active, no manual Sanctum team-pick
// required first). Persisted alongside NextUnnamedIndex (not just held in memory) so a disconnect
// mid-sequence doesn't lose track of slots already confirmed before the auto-team-assign fires.
public sealed class PendingStarterReveal
{
    public required IReadOnlyList<StarterAnimaRoll> Rolls { get; init; }
    public int NextUnnamedIndex { get; set; }
    public List<string> ConfirmedAnimaIds { get; init; } = [];
}
