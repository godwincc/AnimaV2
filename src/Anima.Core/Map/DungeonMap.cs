namespace Anima.Core.Map;

public sealed class DungeonMap
{
    public const int Width = 7;
    public const int FloorCount = 15;

    // Floors[0] = Floor 1 ... Floors[14] = Floor 15. Each inner list holds only nodes that
    // ended up with at least one connection (untouched grid slots are never materialized, so
    // there is nothing to prune after the fact).
    public List<List<MapNode>> Floors { get; } = [];

    public required MapNode Boss { get; init; }
}
