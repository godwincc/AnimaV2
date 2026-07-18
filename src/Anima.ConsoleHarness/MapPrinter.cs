using Anima.Core.Map;

namespace Anima.ConsoleHarness;

// Debug-only ASCII rendering + rule verification for MapGenerator. Lives in the throwaway
// console harness project, not Anima.Core, since it's a visual-verification tool rather than
// game logic.
public static class MapPrinter
{
    private static readonly Dictionary<MapNodeType, char> Symbols = new()
    {
        [MapNodeType.Combat] = 'C',
        [MapNodeType.Elite] = 'E',
        [MapNodeType.Resource] = '?',
        [MapNodeType.Treasure] = 'T',
        [MapNodeType.Shop] = '$',
        [MapNodeType.Reforge] = 'R',
        [MapNodeType.Boss] = 'B',
    };

    private const string LabelPrefix = "    "; // width matches "F01 "
    private const int RowWidth = 2 * DungeonMap.Width - 1; // 13

    public static void Print(DungeonMap map)
    {
        Console.WriteLine($"{LabelPrefix}{Center("BOSS", RowWidth)}");
        Console.WriteLine(LabelPrefix + BossConnectorRow(map));

        for (var floor = DungeonMap.FloorCount - 1; floor >= 0; floor--)
        {
            Console.WriteLine($"F{floor + 1:D2} {NodeRow(map.Floors[floor])}");
            if (floor > 0) Console.WriteLine(LabelPrefix + ConnectorRow(map.Floors[floor - 1], map.Floors[floor]));
        }

        Console.WriteLine();
        Console.WriteLine("Legend: C=Combat E=Elite ?=Resource T=Treasure $=Shop R=Reforge B=Boss");
    }

    private static string NodeRow(List<MapNode> nodes)
    {
        var chars = new char[RowWidth];
        Array.Fill(chars, ' ');
        foreach (var node in nodes)
        {
            chars[2 * node.Column] = node.Type is { } type ? Symbols[type] : '.';
        }
        return new string(chars);
    }

    private static string ConnectorRow(List<MapNode> lower, List<MapNode> upper)
    {
        var chars = new char[RowWidth];
        Array.Fill(chars, ' ');
        foreach (var node in upper)
        {
            foreach (var prev in node.Previous)
            {
                if (!lower.Contains(prev)) continue;
                int pos;
                char glyph;
                if (node.Column == prev.Column) { pos = 2 * node.Column; glyph = '|'; }
                else if (node.Column > prev.Column) { pos = 2 * prev.Column + 1; glyph = '/'; }
                else { pos = 2 * node.Column + 1; glyph = '\\'; }
                chars[pos] = chars[pos] == ' ' || chars[pos] == glyph ? glyph : 'X';
            }
        }
        return new string(chars);
    }

    private static string BossConnectorRow(DungeonMap map)
    {
        var chars = new char[RowWidth];
        Array.Fill(chars, ' ');
        foreach (var prev in map.Boss.Previous) chars[2 * prev.Column] = '|';
        return new string(chars);
    }

    private static string Center(string text, int width)
    {
        var left = (width - text.Length) / 2;
        return new string(' ', Math.Max(left, 0)) + text;
    }

    public static List<string> Validate(DungeonMap map)
    {
        var violations = new List<string>();

        foreach (var node in map.Floors[0])
        {
            if (node.Type != MapNodeType.Combat) violations.Add($"Floor 1 node col {node.Column} is {node.Type}, expected Combat.");
        }
        foreach (var node in map.Floors[8])
        {
            if (node.Type != MapNodeType.Treasure) violations.Add($"Floor 9 node col {node.Column} is {node.Type}, expected Treasure.");
        }
        foreach (var node in map.Floors[14])
        {
            if (node.Type != MapNodeType.Shop) violations.Add($"Floor 15 node col {node.Column} is {node.Type}, expected Shop.");
        }

        for (var floor = 0; floor <= 4; floor++)
        {
            foreach (var node in map.Floors[floor])
            {
                if (node.Type is MapNodeType.Elite or MapNodeType.Shop)
                {
                    violations.Add($"Floor {floor + 1} node col {node.Column} is {node.Type}, banned below Floor 6.");
                }
            }
        }

        foreach (var node in map.Floors[13])
        {
            if (node.Type == MapNodeType.Shop) violations.Add($"Floor 14 node col {node.Column} is Shop, which is banned.");
        }

        var noAdjacentGroup = new HashSet<MapNodeType> { MapNodeType.Elite, MapNodeType.Shop, MapNodeType.Reforge };
        for (var floor = 0; floor < DungeonMap.FloorCount; floor++)
        {
            foreach (var node in map.Floors[floor])
            {
                if (node.Type is not { } nodeType || !noAdjacentGroup.Contains(nodeType)) continue;
                foreach (var next in node.Next)
                {
                    if (next.Type is { } nextType && nextType != nodeType && noAdjacentGroup.Contains(nextType))
                    {
                        violations.Add($"Floor {floor + 1} col {node.Column} ({nodeType}) is directly connected to Floor {next.FloorIndex + 1} col {next.Column} ({nextType}).");
                    }
                }
            }
        }

        foreach (var floor in map.Floors)
        {
            foreach (var node in floor)
            {
                // Floor 9 and Floor 15 are entirely one fixed type (Treasure/Shop) by design, so
                // any node with 2+ edges into either of them necessarily "duplicates" - that's an
                // unavoidable conflict between the fixed-floor rule and the duplicate-destination
                // rule, not a generation bug, so it's exempted here rather than flagged.
                if (node.FloorIndex + 1 == 8 || node.FloorIndex + 1 == 14) continue;

                var types = node.Next.Where(n => n.Type is not null).Select(n => n.Type!.Value).ToList();
                if (types.Count != types.Distinct().Count())
                {
                    violations.Add($"Floor {node.FloorIndex + 1} col {node.Column} has 2+ outgoing paths leading to duplicate node types.");
                }
            }
        }

        foreach (var node in map.Floors[14])
        {
            if (!map.Boss.Previous.Contains(node)) violations.Add($"Floor 15 col {node.Column} does not connect to the Boss.");
        }

        for (var floor = 0; floor < DungeonMap.FloorCount; floor++)
        {
            foreach (var node in map.Floors[floor])
            {
                if (node.Previous.Count == 0 && node.Next.Count == 0)
                {
                    violations.Add($"Floor {floor + 1} col {node.Column} is an orphan node (no connections).");
                }
            }
        }

        for (var floor = 0; floor < DungeonMap.FloorCount - 1; floor++)
        {
            var edges = new List<(int Lower, int Upper)>();
            foreach (var node in map.Floors[floor])
            {
                foreach (var next in node.Next) edges.Add((node.Column, next.Column));
            }
            for (var i = 0; i < edges.Count; i++)
            {
                for (var j = i + 1; j < edges.Count; j++)
                {
                    var crosses = (edges[i].Lower - edges[j].Lower) * (edges[i].Upper - edges[j].Upper) < 0;
                    if (crosses)
                    {
                        violations.Add($"Paths cross between Floor {floor + 1} and Floor {floor + 2}: edges {edges[i]} and {edges[j]}.");
                    }
                }
            }
        }

        return violations;
    }
}
