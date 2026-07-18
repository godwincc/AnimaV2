namespace Anima.Core.Reforge;

// What a Reforge node shows the player before they commit: the rolled candidate, and whether the
// premium color-choice option was paid for (which changes AcceptCost). Declining costs nothing
// and is just "don't call ReforgeService.Accept" -- there's no state to unwind here.
public sealed record ReforgeOffer(ReforgeCandidate Candidate, bool ColorWasChosen)
{
    public int AcceptCost => ColorWasChosen ? ReforgeService.ChooseColorAcceptCost : ReforgeService.BaseAcceptCost;
}
