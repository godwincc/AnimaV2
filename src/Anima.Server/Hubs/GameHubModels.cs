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
