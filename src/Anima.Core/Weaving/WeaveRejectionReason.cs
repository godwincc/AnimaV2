namespace Anima.Core.Weaving;

// Why AttemptWeave refused to run at all -- checked (and, if triggered, returned) before anything
// is rolled or any Wisp cost is charged.
public enum WeaveRejectionReason
{
    None,
    DirectParentChild,
    FullSiblings,
    WeaveCountExhausted,
    InsufficientWisp,
    InsufficientEchoShards,
}
