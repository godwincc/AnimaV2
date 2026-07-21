namespace Anima.Server.Hubs;

// Part is a string (not the Anima.Core.Enums.Part the server has internally) because these are
// wire DTOs -- consumers (client, tests) shouldn't need a reference to Anima.Core just to read a
// hub response. Category is likewise Skill.Category.ToString(); the client maps it (plus the
// Part name, for the Crest-is-always-diamond rule) to the sword/heart/shield/bolt/diamond icon
// set per CLAUDE.md's Skill-type icon set.
public record AnimaPartSummary(string Part, string SkillName, string Category);

public record AnimaSummary(
    string Id,
    string Name,
    string Color,
    int Gen,
    int WeaveCount,
    int CurrentHp,
    int MaxHp,
    bool InTeam,
    IReadOnlyList<AnimaPartSummary> Parts);

public record LedgerSnapshot(Dictionary<string, int> Balances);

// Discovered mirrors AccountArtifactStatEntity's "row exists" convention -- real as of this
// session for anything ever granted by ClaimTreasureNode. DelvesWonWith is still always 0: that
// write path needs Boss-victory resolution (Phase 5), not wired up yet.
public record ArtifactSummary(string Name, string Description, bool Discovered, int DelvesWonWith);

public record NodeRef(int FloorIndex, int Column, string? Type);

public record DelveStatus(NodeRef? CurrentNode, IReadOnlyList<NodeRef> AvailableNodes, int WispEarnedSoFar);

public record StartDelveRequest(string[] TeamAnimaIds);

// ---- Delve traversal / Resource / Treasure ----

// FloorIndex/Column identify the target the same way DelveStatus.AvailableNodes already reports
// it -- MoveToNode just needs to find that exact node again in DelveRun.AvailableNodes, never a
// raw MapNode reference across the wire.
public record MoveToNodeRequest(int FloorIndex, int Column);

// PendingEmberColors is the account's FULL current queue (front-to-back), not just what this
// call added -- lets the client drive "resolve the next one" in a loop without a separate getter.
public record CollectResourceResult(int WispGranted, IReadOnlyList<string> PendingEmberColors);

// ArtifactName/Description are null exactly when CapReached is true (the node still gets marked
// cleared either way -- see ArtifactService's own "intentional punish for a wasted node" comment).
public record ClaimTreasureResult(string? ArtifactName, string? ArtifactDescription, bool CapReached, IReadOnlyList<string> PendingEmberColors);

// Part is a string for the same reason AnimaPartSummary.Part is -- AugmentType likewise (parsed
// server-side via Enum.TryParse, with a clear HubException on a bad value rather than a raw
// FormatException).
public record AugmentPendingEmberRequest(string AnimaId, string Part, string AugmentType);

// ---- Shop ----

// Color is null for a slot that's already been bought this visit. EmberPrice/ArtifactPrice/
// RestWispCost are the ACTUAL current cost (Ember Core's discount already applied if held), not
// the base constant -- the client shouldn't need to re-derive that discount itself.
public record ShopEmberSlot(int Index, string? Color);

public record ShopStockSnapshot(
    IReadOnlyList<ShopEmberSlot> EmberSlots,
    string? ArtifactName,
    string? ArtifactDescription,
    int EmberPrice,
    int ArtifactPrice,
    int RestWispCost);

public record RestAtShopResult(int WispSpent);

public record BuyWaresEmberRequest(int SlotIndex);

public record BuyWaresArtifactResult(string ArtifactName, string ArtifactDescription, IReadOnlyList<string> PendingEmberColors);

// ---- Weaving ----

public record AttemptWeaveRequest(string ParentAId, string ParentBId, bool SpendEchoShards);

public record SkillSummary(string Name, string Category, string Color);

public record PartGenomeSummary(string Part, SkillSummary Dominant, SkillSummary R1, SkillSummary R2);

// One resolved Weave outcome's full genome, unnamed -- what the Anima Reveal screen shows before
// naming (Color/Threads section), for either the Primary or (if EchoTriggered) the Twin.
public record WeaveGenomePreview(string Color, bool HybridTriggered, IReadOnlyList<PartGenomeSummary> Parts);

// Returned by AttemptWeave once the roll (and its Wisp/Echo Shard cost) has already committed --
// WispCost is informational (the spend already happened), not a re-confirmable quote. Twin is
// null unless Echo triggered, in which case ConfirmWeave requires a name for both.
public record WeaveRevealSnapshot(int WispCost, bool EchoTriggered, WeaveGenomePreview Primary, WeaveGenomePreview? Twin);

public record ConfirmWeaveRequest(string PrimaryName, string? TwinName);

public record WeaveConfirmResult(AnimaSummary Primary, AnimaSummary? Twin);

// Anima Profile's own dedicated read: R1/R2 per part (the "Show hidden" toggle) plus resolved
// Parent/Echo-Twin names, closing both Profile-facing gaps flagged in the Phase 1 report. Not
// folded into AnimaSummary/GetRoster -- Sanctum's grid never needs hidden Threads, only Profile
// does, so this stays a separate, deliberately un-batched call.
public record AnimaDetail(
    string Id,
    string Name,
    string Color,
    int Gen,
    int WeaveCount,
    int CurrentHp,
    int MaxHp,
    bool InTeam,
    IReadOnlyList<PartGenomeSummary> Parts,
    string? ParentAId,
    string? ParentAName,
    string? ParentBId,
    string? ParentBName,
    string? EchoTwinId,
    string? EchoTwinName);
