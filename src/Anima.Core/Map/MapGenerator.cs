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

    // Map Odds Rebalance (LOCKED): Reforge set to 0% -- Option A, its old 5% spread evenly across
    // the other 5 types (35->36, 15->16 x4). Reforge stays fully real/implemented code (RollOffer,
    // Accept, targeting flow); this is a probability change only, re-enabled later by restoring a
    // nonzero weight here, nothing else needs to change.
    //
    // Reforge MUST stay the LAST entry in this array for its 0.0 weight to be truly unreachable,
    // not just "practically never" -- see AssignRandomType's weighted-roll loop: `roll` is always
    // strictly < the cumulative total (Random.NextDouble() never returns 1.0), so the entry whose
    // cumulative first reaches/exceeds `roll` always wins BEFORE the loop ever reaches a trailing
    // 0-weight entry (adding 0 can't move the cumulative past what the previous real entry already
    // covered). Moving a 0-weight entry to any position OTHER than last, or adding a 6th type after
    // it, would break this guarantee and make that entry reachable again by whatever weight follows
    // it in iteration order.
    private static readonly (MapNodeType Type, double Weight)[] TypeOdds =
    [
        (MapNodeType.Combat, 0.36),
        (MapNodeType.Elite, 0.16),
        (MapNodeType.Resource, 0.16),
        (MapNodeType.Treasure, 0.16),
        (MapNodeType.Shop, 0.16),
        (MapNodeType.Reforge, 0.00),
    ];

    // Guaranteed Elite + Early-Game Elite Exclusion (LOCKED, STS-inspired). Used in place of
    // TypeOdds for Floors 1-5 (floorIndex 0-4): Elite is omitted entirely (not a 0-weight entry --
    // there's no adjacent-zero-weight invariant to maintain here since nothing needs it to be
    // last), its 16% split evenly 4 ways onto Combat/Resource/Treasure/Shop (+4% each: 36->40,
    // 16->20 x3). Reforge is likewise omitted (it's 0% everywhere already, see TypeOdds' own
    // comment) rather than duplicated here.
    //
    // FLAGGED: Shop's own +4% share here never actually manifests on a real map. Shop is ALSO
    // independently banned on Floors 1-5 by the pre-existing EliteShopMinFloorIndex check below (a
    // joint Elite+Shop floor gate from an earlier session, unrelated to and untouched by this
    // one) -- so `excluded` still filters Shop out of `allowed` regardless of its weight here. The
    // real, observable Floors-1-5 distribution ends up Combat/Resource/Treasure only, renormalized
    // among just those three. This table is still written the way it's written (naming Shop
    // explicitly at its "fair" redistributed weight) because that's what was actually asked for,
    // and because it costs nothing to be technically correct even where it's practically moot.
    private static readonly (MapNodeType Type, double Weight)[] EarlyFloorTypeOdds =
    [
        (MapNodeType.Combat, 0.40),
        (MapNodeType.Resource, 0.20),
        (MapNodeType.Treasure, 0.20),
        (MapNodeType.Shop, 0.20),
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

        // Guaranteed Elite (LOCKED, STS-inspired): before the normal weighted roll runs anywhere,
        // force exactly one randomly-chosen eligible node to Elite. This is a FLOOR, not a cap --
        // the normal roll (Elite still in TypeOdds at 16%) proceeds as usual for every other node
        // afterward, so a map can end up with more than 1 Elite, just never fewer than 1.
        //
        // Eligible = Floor 6 through Floor 14 (floorIndex 5-13), excluding Floor 9 (floorIndex 8,
        // already fixed Treasure). Floor 15/Boss are outside this range already, per the brief.
        //
        // FLAGGED DEVIATION from the literal brief: Floor 14 (floorIndex 13) is ALSO excluded here,
        // on top of Floor 9 -- every Floor 14 node has a Next edge straight into Floor 15 (entirely
        // fixed Shop, see the AddEdge loop above), and Elite+Shop direct adjacency is already banned
        // by NoAdjacentGroup (this is the SAME rule that already makes Floor 14 unable to roll Elite
        // under the normal weighted path today). Forcing an Elite onto Floor 14 would create a
        // guaranteed, unavoidable rule violation the 500-seed batch validator would catch every
        // time. This node-picking step bypasses AssignRandomType entirely (direct assignment, no
        // exclusion-checking) precisely because it's meant to override the normal odds -- but it
        // still can't be allowed to violate a structural adjacency rule, so Floor 14 has to come out
        // of the candidate pool rather than being included as asked.
        //
        // No interaction with the Reforge-0%-safety invariant documented on TypeOdds above: this is
        // a direct `.Type =` assignment on one node, never touching TypeOdds, `allowed`, or the
        // weighted-roll loop at all -- it can only ever produce Elite, never Reforge, and doesn't
        // change array order or contents.
        var guaranteedEliteCandidates = new List<MapNode>();
        for (var floor = EliteShopMinFloorIndex; floor < Floor14Index; floor++)
        {
            if (floor == Floor9Index) continue;
            guaranteedEliteCandidates.AddRange(map.Floors[floor]);
        }
        // Always non-empty in practice: every one of the 6 path chains touches every floor
        // (BuildPath iterates floor 0 through FloorCount-2, i.e. every floor gets at least one
        // node), so Floors 6-13 always have candidates.
        var guaranteedElite = guaranteedEliteCandidates[rng.Next(guaranteedEliteCandidates.Count)];
        guaranteedElite.Type = MapNodeType.Elite;

        for (var floor = 1; floor < DungeonMap.FloorCount; floor++)
        {
            if (floor == Floor9Index || floor == Floor15Index) continue;
            foreach (var node in map.Floors[floor])
            {
                if (node == guaranteedElite) continue; // already force-assigned above
                node.Type = AssignRandomType(node, floor, rng);
            }
        }
    }

    private static MapNodeType AssignRandomType(MapNode node, int floorIndex, Random rng)
    {
        // Floors 1-5 (floorIndex 0-4) use EarlyFloorTypeOdds, which excludes Elite structurally
        // (it's simply not one of the 4 entries) rather than via the `excluded` set below -- see
        // that table's own comment. Shop's floor-6 minimum below is a separate, still-active rule
        // and still needs its own exclusion regardless of which table is in play.
        var baseOdds = floorIndex <= EliteShopMinFloorIndex - 1 ? EarlyFloorTypeOdds : TypeOdds;

        var excluded = new HashSet<MapNodeType>();

        if (floorIndex <= EliteShopMinFloorIndex - 1) excluded.Add(MapNodeType.Shop);
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

        var allowed = baseOdds.Where(t => !excluded.Contains(t.Type)).ToList();
        if (allowed.Count == 0)
        {
            // Soft constraints (adjacency group + sibling duplication) exhausted every option -
            // fall back to only the hard, floor-position bans so we always produce a valid pick.
            // Elite needs no entry here for early floors -- baseOdds (EarlyFloorTypeOdds) already
            // excludes it structurally, same as the primary path above. Shop's floor-6 minimum is
            // a hard floor-position rule (not a soft adjacency/sibling one), so it still needs
            // reinstating here for both the early-floor and Floor-14 cases.
            var hardBans = new HashSet<MapNodeType>();
            if (floorIndex <= EliteShopMinFloorIndex - 1) hardBans.Add(MapNodeType.Shop);
            if (floorIndex == Floor14Index) hardBans.Add(MapNodeType.Shop);
            allowed = baseOdds.Where(t => !hardBans.Contains(t.Type)).ToList();
        }

        var total = allowed.Sum(t => t.Weight);
        var roll = rng.NextDouble() * total;
        var cumulative = 0.0;
        foreach (var (type, weight) in allowed)
        {
            cumulative += weight;
            if (roll <= cumulative) return type;
        }
        // Unreachable in practice -- `total` and this loop's final `cumulative` are computed via
        // the identical sequential floating-point sum over the same `allowed` order, so they're
        // bit-identical, and roll < total strictly (NextDouble() never returns 1.0). Kept as a
        // safety net regardless. See TypeOdds' own comment: this is also why Reforge's 0.0 weight
        // must stay LAST in the array -- if this fallback ever did fire with Reforge last, it would
        // return Reforge, the one outcome the 0% odds are meant to rule out entirely.
        return allowed[^1].Type;
    }
}
