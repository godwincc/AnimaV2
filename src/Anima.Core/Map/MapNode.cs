namespace Anima.Core.Map;

// Column is the node's 0-6 slot within FloorIndex (0-14 = Floor 1-15). The Boss node reuses this
// type with FloorIndex 15 and Column -1 (it isn't part of the 7-wide grid).
public sealed class MapNode(int floorIndex, int column)
{
    public int FloorIndex { get; } = floorIndex;
    public int Column { get; } = column;

    // Null until MapGenerator's type-assignment pass reaches this node. Nodes are created lazily
    // as paths touch them, so a node's siblings on the same floor may not be assigned yet when
    // this node is - callers must null-check before relying on a sibling's Type.
    public MapNodeType? Type { get; set; }

    public List<MapNode> Next { get; } = [];
    public List<MapNode> Previous { get; } = [];
}
