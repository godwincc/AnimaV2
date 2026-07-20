namespace Anima.Server.Hubs;

public record AnimaSummary(string Id, string Name, string Color, int Gen, int WeaveCount, int CurrentHp, int MaxHp);

public record LedgerSnapshot(Dictionary<string, int> Balances);

public record NodeRef(int FloorIndex, int Column, string? Type);

public record DelveStatus(NodeRef? CurrentNode, IReadOnlyList<NodeRef> AvailableNodes, int WispEarnedSoFar);

public record StartDelveRequest(string[] TeamAnimaIds);
