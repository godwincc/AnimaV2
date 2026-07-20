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

// Discovered mirrors AccountArtifactStatEntity's "row exists" convention; DelvesWonWith is 0 for
// anything not yet discovered. See AccountArtifactStatEntity's own comment: nothing writes these
// yet, so every account currently gets Discovered=false/DelvesWonWith=0 across the board -- an
// honest reflection of real state, not a placeholder bug.
public record ArtifactSummary(string Name, string Description, bool Discovered, int DelvesWonWith);

public record NodeRef(int FloorIndex, int Column, string? Type);

public record DelveStatus(NodeRef? CurrentNode, IReadOnlyList<NodeRef> AvailableNodes, int WispEarnedSoFar);

public record StartDelveRequest(string[] TeamAnimaIds);

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
