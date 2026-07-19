namespace Anima.Core.Weaving;

// Outcome of one call to WeavingService.AttemptWeave. On rejection, Success is false,
// RejectionReason explains why, and nothing else is populated (no roll happened, no Wisp was
// spent, no WeaveCount was touched). On success, Primary is always the first independently-rolled
// offspring; Twin is only populated when Echo triggered (spontaneous or forced), and is a second,
// fully independent Weave -- not a copy of Primary.
public sealed record WeaveAttemptResult(
    bool Success,
    WeaveRejectionReason RejectionReason,
    int WispCost,
    bool EchoTriggered,
    WeavingResult? Primary,
    WeavingResult? Twin)
{
    public static WeaveAttemptResult Rejected(WeaveRejectionReason reason) =>
        new(false, reason, 0, false, null, null);
}
