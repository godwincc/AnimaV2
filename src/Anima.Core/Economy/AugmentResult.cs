using Anima.Core.Enums;

namespace Anima.Core.Economy;

// Outcome of one call to AugmentService.TryApplyAugment. On rejection, Success is false,
// RejectionReason explains why, and nothing else is populated -- no Wisp was spent, the caller's
// Ember should NOT be treated as consumed, the skill wasn't mutated, and AppliedAugments wasn't
// touched. On success, the Ember the caller passed in IS considered spent (nothing here tracks it
// -- see EmberService's own comment), and WispCost is the Wisp tier that was actually charged.
public sealed record AugmentResult(
    bool Success,
    AugmentRejectionReason RejectionReason,
    int WispCost,
    AugmentType? AppliedType)
{
    public static AugmentResult Rejected(AugmentRejectionReason reason) =>
        new(false, reason, 0, null);
}
