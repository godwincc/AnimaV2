using Anima.Core.Economy;
using Anima.Core.Enums;
using Anima.Core.Map;
using Anima.Core.Models;
using AnimaUnit = Anima.Core.Models.Anima;

namespace Anima.Core.Run;

// The connective state a Delve needs between node visits -- a real, confirmed gap: Combat,
// Weaving, and Rewards all work correctly in isolation, but nothing tracked "where this run
// currently stands" as the player moves node to node. The locked map design, the HP-attrition
// pillar, the Artifact cap, and DelveEndService (Defeat/Retreat) all assumed this state existed;
// none of them actually had it. Single active instance per Delve -- created via Start when a Delve
// begins, discarded when it ends (Boss Victory/Delve Complete, Defeat, or Retreat). In-memory only,
// no save/resume across app restarts, same caveat as PersistentLedger/SanctumRoster.
//
// Deliberately does NOT duplicate state that already lives elsewhere. Two things on the original
// scope list already have a real home:
//   - "Currently-held Artifacts" already lives on RunLedger.Artifacts (ArtifactService's 3-cap
//     logic already reads a RunLedger directly).
//   - "Wisp earned this run so far" is already what DelveEndService computes, by diffing
//     PersistentLedger's current balance against RunLedger.WispAtDelveStart.
// Giving DelveRun its own separate Artifacts list or running Wisp counter would fork these into two
// independently-mutable copies that must be kept in sync by hand -- a real bug risk for no benefit.
// Instead DelveRun holds REFERENCES to the RunLedger/PersistentLedger it was started with, plus the
// genuinely new state (map position, cleared nodes, team), and exposes CurrentArtifacts/
// WispEarnedSoFar as passthrough reads so callers have one object to look at.
//
// Team HP "carried across nodes" needs no separate tracking at all: Team holds the SAME Anima
// instances used to build a Combat node's CombatState.PlayerTeam, and Anima.CurrentHp already
// mutates in place during a fight -- as long as a node's CombatState is built from THIS list (not a
// deep copy of it), HP attrition falls out for free with no explicit "hand off HP" step. Flagged
// because it's worth stating plainly: nothing currently deep-copies an Anima instance anywhere in
// the codebase (Skill.Clone() clones a Skill, not the Anima that owns it), so this holds today --
// but it would silently break if a future caller ever did `new Anima { ... }` from an existing
// team member instead of reusing the reference, so any real Combat-node wiring MUST pass
// DelveRun.Team itself (or a shallow copy of the list, which is fine) into CombatState.PlayerTeam,
// never a rebuilt/cloned team.
public sealed class DelveRun
{
    public required DungeonMap Map { get; init; }
    public required List<AnimaUnit> Team { get; init; }
    public required RunLedger RunLedger { get; init; }
    public required PersistentLedger PersistentLedger { get; init; }

    // Null until the player picks a starting node -- see AvailableNodes.
    public MapNode? CurrentNode { get; set; }

    // Reference-equality set is safe here: MapGenerator's GetOrCreate never creates two MapNode
    // instances for the same (floor, col) within one DungeonMap, so every edge that touches a given
    // node points at the exact same object -- no custom Equals/GetHashCode needed on MapNode itself
    // just for this.
    public HashSet<MapNode> ClearedNodes { get; } = new();

    // Caller-driven, not automatic: the node just entered isn't "cleared" until its own outcome
    // actually resolves (reward granted, fight won, shop exited, etc.) -- deliberately decoupled
    // from TryMoveTo so a Boss loss (which never moves anywhere) can't leave a half-cleared node.
    public void MarkCurrentNodeCleared()
    {
        if (CurrentNode != null) ClearedNodes.Add(CurrentNode);
    }

    // The nodes selectable right now: any zero-Previous node on Floor 1 (index 0) before the player
    // has moved at all, otherwise CurrentNode's own Next edges. Boss needs no special case -- every
    // Floor 15 node is already wired straight to Boss by MapGenerator.Generate, so it shows up here
    // naturally once CurrentNode reaches Floor 15. "Elite is skippable" (per Map Generation) is
    // likewise just an emergent property of the graph usually offering more than one Next option,
    // not a separate mechanic this needs to implement.
    public IReadOnlyList<MapNode> AvailableNodes =>
        CurrentNode?.Next ?? Map.Floors[0].Where(n => n.Previous.Count == 0).ToList();

    // Rejects a move to anything not in AvailableNodes -- callers can't skip the map's own graph.
    public bool TryMoveTo(MapNode node)
    {
        if (!AvailableNodes.Contains(node)) return false;
        CurrentNode = node;
        return true;
    }

    public IReadOnlyList<Artifact> CurrentArtifacts => RunLedger.Artifacts;

    public int WispEarnedSoFar => Math.Max(0, PersistentLedger.GetBalance(ResourceType.Wisp) - RunLedger.WispAtDelveStart);

    // Reforge run-scoped overrides (NEW, replaces the old "mutate the Anima directly" design) --
    // keyed by (target Anima's Id, Part), consulted only at deck-build time (see
    // CombatEngine's optional DelveRun constructor param / GetDeckSkills). Deliberately never
    // touches the underlying Anima.Head/Frame/Tail fields, so a Delve-only swap can never leak
    // into the permanent, persisted genome -- and deliberately never touches HeadR1/HeadR2 etc.
    // either, since those are Weaving/lineage-only hidden-gene data Reforge has no business
    // reading or writing. Discarded for free when this DelveRun itself is discarded (Boss
    // Victory/Delve Complete, Defeat, Retreat) -- no explicit clear needed, since a fresh
    // DelveRun.Start() always begins with an empty dictionary.
    private readonly Dictionary<(string AnimaId, Part Part), Skill> _reforgeOverrides = new();

    // Crest is deliberately never a valid key here -- it contributes no deck cards (see
    // Models.Anima.DeckSkills), so ReforgeService never offers it and this guards the same
    // invariant defensively at the one other place an override could be written.
    public void SetReforgeOverride(AnimaUnit target, Part part, Skill skill)
    {
        if (part == Part.Crest)
        {
            throw new InvalidOperationException("Reforge does not support Crest -- it contributes no deck cards.");
        }
        _reforgeOverrides[(target.Id, part)] = skill;
    }

    // The skill deck-build should actually use for (anima, part) this Delve -- the Reforge
    // override if one was Accepted this Delve, otherwise the Anima's own real Head/Frame/Tail.
    public Skill GetEffectiveSkill(AnimaUnit anima, Part part)
    {
        if (_reforgeOverrides.TryGetValue((anima.Id, part), out var overrideSkill)) return overrideSkill;

        return part switch
        {
            Part.Head => anima.Head,
            Part.Frame => anima.Frame,
            Part.Tail => anima.Tail,
            _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Reforge/deck-build only cover Head/Frame/Tail."),
        };
    }

    // Mirrors Models.Anima.DeckSkills' own Head/Frame/Tail order, but resolved through any active
    // Reforge overrides first -- this is what CombatEngine's deck-build should iterate instead of
    // anima.DeckSkills directly whenever a DelveRun is in play.
    public Skill[] GetEffectiveDeckSkills(AnimaUnit anima) =>
        new[] { GetEffectiveSkill(anima, Part.Head), GetEffectiveSkill(anima, Part.Frame), GetEffectiveSkill(anima, Part.Tail) };

    // The one real wiring gap this surfaced: RunLedger.WispAtDelveStart existed since the Match
    // Result & Retreat System session, but nothing outside tests ever actually SET it -- every real
    // Delve would have diffed against a stale 0 without this. Start is now the one call site that
    // does it correctly, the moment a Delve actually begins.
    public static DelveRun Start(DungeonMap map, List<AnimaUnit> team, PersistentLedger persistentLedger, RunLedger? runLedger = null)
    {
        runLedger ??= new RunLedger();
        runLedger.WispAtDelveStart = persistentLedger.GetBalance(ResourceType.Wisp);

        return new DelveRun
        {
            Map = map,
            Team = team,
            RunLedger = runLedger,
            PersistentLedger = persistentLedger,
        };
    }
}
