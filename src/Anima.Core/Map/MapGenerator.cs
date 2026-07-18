namespace Anima.Core.Map;

// Slay-the-Spire-style node map generator. See CLAUDE.md's "Run structure" TODO for the design
// this implements; this class only builds the graph + assigns node types, nothing downstream
// (Wisp costs, node effects, etc.) is wired up yet.
public static class MapGenerator
{
    private const int PathChainCount = 6;

    // Floor numbers are 1-indexed in the design ("Floor 1"/"Floor 9"/"Floor 15"); FloorIndex is
    // 0-indexed internally. EliteShopMinFloorIndex = 5 means Floor 6 (index 5) is the first
    // allowed floor for Elite/Shop - "can't appear below Floor 6".
    private const int EliteShopMinFloorIndex = 5;
    private const int Floor9Index = 8;   // fixed Treasure
    private const int Floor14Index = 13; // Shop banned here specifically
    private const int Floor15Index = 14; // fixed Shop; Boss connects from every node here

    // Reforge's 5% comes out of Combat's prior 40% share; Elite/Resource/Treasure/Shop unchanged.
    private static readonly (MapNodeType Type, double Weight)[] TypeOdds =
    [
        (MapNodeType.Combat, 0.35),
        (MapNodeType.Elite, 0.15),
        (MapNodeType.Resource, 0.15),
        (MapNodeType.Treasure, 0.15),
        (MapNodeType.Shop, 0.15),
        (MapNodeType.Reforge, 0.05),
    ];

    // Elite, Shop, and Reforge can't be directly connected to a *different* member of this group
    // (Elite-Shop, Elite-Reforge, Shop-Reforge all banned; Elite-Elite/Shop-Shop/Reforge-Reforge
    // are fine - the original rule only ever banned cross-type pairing).
    private static readonly HashSet<MapNodeType> NoAdjacentGroup = [MapNodeType.Elite, MapNodeType.Shop, MapNodeType.Reforge];

    private static readonly int[] ColumnDeltas = [-1, 0, 1];

    public static DungeonMap Generate(Random? random = null)
    {
        var rng = random ?? new Random();
        var grid = new MapNode?[DungeonMap.FloorCount, DungeonMap.Width];

        MapNode GetOrCreate(int floor, int col) => grid[floor, col] ??= new MapNode(floor, col);

        void AddEdge(MapNode from, MapNode to)
        {
            if (from.Next.Contains(to)) return;
            from.Next.Add(to);
            to.Previous.Add(from);
        }

        // One crossing-check list per floor transition (transition i connects floor i -> i+1).
        var edgesByTransition = new List<(int Lower, int Upper)>[DungeonMap.FloorCount - 1];
        for (var i = 0; i < edgesByTransition.Length; i++) edgesByTransition[i] = [];

        bool WouldCross(int transition, int lower, int upper)
        {
            foreach (var (existingLower, existingUpper) in edgesByTransition[transition])
            {
                var crosses = (lower - existingLower) * (upper - existingUpper) < 0;
                if (crosses) return true;
            }
            return false;
        }

        void BuildPath(int startCol)
        {
            var col = startCol;
            for (var floor = 0; floor < DungeonMap.FloorCount - 1; floor++)
            {
                var candidates = new List<int>();
                foreach (var delta in ColumnDeltas)
                {
                    var next = col + delta;
                    if (next < 0 || next >= DungeonMap.Width) continue;
                    if (WouldCross(floor, col, next)) continue;
                    candidates.Add(next);
                }
                // (col, col) never crosses an existing edge (equal lower always ties, never a
                // strict inversion), so this is unreachable in practice - kept as a safety net.
                if (candidates.Count == 0) candidates.Add(col);

                var chosen = candidates[rng.Next(candidates.Count)];
                AddEdge(GetOrCreate(floor, col), GetOrCreate(floor + 1, chosen));
                edgesByTransition[floor].Add((col, chosen));
                col = chosen;
            }
        }

        var startCols = new int[PathChainCount];
        startCols[0] = rng.Next(DungeonMap.Width);
        do
        {
            startCols[1] = rng.Next(DungeonMap.Width);
        } while (startCols[1] == startCols[0]);
        for (var i = 2; i < PathChainCount; i++) startCols[i] = rng.Next(DungeonMap.Width);

        foreach (var startCol in startCols) BuildPath(startCol);

        var boss = new MapNode(Floor15Index + 1, -1) { Type = MapNodeType.Boss };

        var map = new DungeonMap { Boss = boss };
        for (var floor = 0; floor < DungeonMap.FloorCount; floor++)
        {
            var nodes = new List<MapNode>();
            for (var col = 0; col < DungeonMap.Width; col++)
            {
                if (grid[floor, col] is { } node) nodes.Add(node);
            }
            map.Floors.Add(nodes);
        }

        foreach (var node in map.Floors[Floor15Index]) AddEdge(node, boss);

        AssignTypes(map, rng);

        return map;
    }

    private static void AssignTypes(DungeonMap map, Random rng)
    {
        // Floors 1/9/15 are fixed, not randomly rolled, so their types are known in advance
        // regardless of processing order - assign them first so the random pass below can see
        // Floor 15 (all Shop) as a successor when it assigns Floor 14, and correctly exclude
        // Elite/Reforge there via the adjacency-group check.
        foreach (var node in map.Floors[0]) node.Type = MapNodeType.Combat;
        foreach (var node in map.Floors[Floor9Index]) node.Type = MapNodeType.Treasure;
        foreach (var node in map.Floors[Floor15Index]) node.Type = MapNodeType.Shop;

        for (var floor = 1; floor < DungeonMap.FloorCount; floor++)
        {
            if (floor == Floor9Index || floor == Floor15Index) continue;
            foreach (var node in map.Floors[floor]) node.Type = AssignRandomType(node, floor, rng);
        }
    }

    private static MapNodeType AssignRandomType(MapNode node, int floorIndex, Random rng)
    {
        var excluded = new HashSet<MapNodeType>();

        if (floorIndex <= EliteShopMinFloorIndex - 1)
        {
            excluded.Add(MapNodeType.Elite);
            excluded.Add(MapNodeType.Shop);
        }
        if (floorIndex == Floor14Index) excluded.Add(MapNodeType.Shop);

        // Checks both directions: predecessors are always already-assigned by this point (forward
        // processing order), and successors are too whenever they land on a fixed floor (1/9/15,
        // pre-assigned above) - e.g. Floor 14 must exclude Elite/Reforge because Floor 15 is
        // entirely Shop. Successors on other (not-yet-assigned) random floors are simply null and
        // skipped here; that pairing gets enforced later from the other side, when the successor
        // itself is assigned and checks back at this node as its predecessor.
        foreach (var neighbor in node.Previous.Concat(node.Next))
        {
            if (neighbor.Type is { } neighborType && NoAdjacentGroup.Contains(neighborType))
            {
                foreach (var groupType in NoAdjacentGroup)
                {
                    if (groupType != neighborType) excluded.Add(groupType);
                }
            }
        }

        // A node with 2+ outgoing paths must lead to different types - so a node's siblings
        // (other children of a shared predecessor) can't repeat a type already assigned here.
        foreach (var prev in node.Previous)
        {
            foreach (var sibling in prev.Next)
            {
                if (sibling == node) continue;
                if (sibling.Type is { } siblingType) excluded.Add(siblingType);
            }
        }

        var allowed = TypeOdds.Where(t => !excluded.Contains(t.Type)).ToList();
        if (allowed.Count == 0)
        {
            // Soft constraints (adjacency group + sibling duplication) exhausted every option -
            // fall back to only the hard, floor-position bans so we always produce a valid pick.
            var hardBans = new HashSet<MapNodeType>();
            if (floorIndex <= EliteShopMinFloorIndex - 1)
            {
                hardBans.Add(MapNodeType.Elite);
                hardBans.Add(MapNodeType.Shop);
            }
            if (floorIndex == Floor14Index) hardBans.Add(MapNodeType.Shop);
            allowed = TypeOdds.Where(t => !hardBans.Contains(t.Type)).ToList();
        }

        var total = allowed.Sum(t => t.Weight);
        var roll = rng.NextDouble() * total;
        var cumulative = 0.0;
        foreach (var (type, weight) in allowed)
        {
            cumulative += weight;
            if (roll <= cumulative) return type;
        }
        return allowed[^1].Type;
    }
}
