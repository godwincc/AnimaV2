namespace Anima.Server.Hubs;

// Part is a string (not the Anima.Core.Enums.Part the server has internally) because these are
// wire DTOs -- consumers (client, tests) shouldn't need a reference to Anima.Core just to read a
// hub response. Category is likewise Skill.Category.ToString(); the client maps it (plus the
// Part name, for the Crest-is-always-diamond rule) to the sword/heart/shield/bolt/diamond icon
// set per CLAUDE.md's Skill-type icon set. GrantsShield (NEW, Hub screen session) is what actually
// separates shield-granting Buffs (shield icon) from every other Buff (bolt icon) -- Category
// alone can't (both are "Buff"), so this rides along as its own field rather than making the
// client guess from the skill name.
// Description (NEW, Delve map screen session) is synthesized server-side from the skill's own
// mechanical fields (Category/BaseDamage/BaseHeal/BaseShield/EnergyCost) -- confirmed by reading
// Models.Skill that NO skill anywhere in this codebase carries actual flavor/effect text (Combat's
// own HandCardSummary doesn't either), so this is plain mechanically-accurate text built from real
// data, not invented copy, same "synthesize from real stored fields" precedent GetLastDelveSummary
// already set. AppliedAugments (NEW, same session) mirrors Skill.AppliedAugments (List<AugmentType>)
// as ToString() names -- Count > 0 is the Delve map Team panel's "has augments" badge signal, the
// full list is what the docked message area shows on hover/tap.
public record AnimaPartSummary(string Part, string SkillName, string Category, bool GrantsShield, string Description, IReadOnlyList<string> AppliedAugments);

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

// DelveMapNode/AllNodes (NEW, Delve map screen session) -- a real, confirmed gap: DelveStatus used
// to expose only CurrentNode/AvailableNodes, enough for the OLD "what can I do right now" callers
// (Resource/Treasure/Shop/Reforge node resolution) but not enough to render CLAUDE.md's own "map is
// LARGE/primary focus" Delve screen design, which needs the WHOLE graph (cleared-behind nodes and
// not-yet-reachable-ahead nodes, both shown dimmed, plus the connector lines between them). Cleared
// mirrors DelveRun.ClearedNodes (reference-equality set) at this node; NextRefs is the adjacency
// list connector lines are drawn from -- both real signals that already existed on MapNode/DelveRun,
// just never carried across the wire before this.
public record DelveMapNode(int FloorIndex, int Column, string? Type, bool Cleared, IReadOnlyList<NodeRef> NextRefs);

// Name/Description straight off Anima.Core.Models.Artifact -- another real gap found the same audit
// pass: nothing exposed DelveRun.CurrentArtifacts (a live passthrough to RunLedger.Artifacts) over
// GameHub at all before this, despite the Delve screen's locked design needing to show currently-held
// Artifacts inline (icon+name+full description), not just a bare count.
public record HeldArtifactSummary(string Name, string Description);

public record DelveStatus(
    NodeRef? CurrentNode,
    IReadOnlyList<NodeRef> AvailableNodes,
    int WispEarnedSoFar,
    IReadOnlyList<DelveMapNode> AllNodes,
    IReadOnlyList<HeldArtifactSummary> Artifacts);

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

// ---- Reforge ----
//
// Color-first flow (REORDERED this session, was Part-first): pick a color -> browse skills for
// that color across all 3 Parts -> pick a target Anima -> confirm (Accept/Decline). "Color" here is
// the wire/code term throughout this file (matching ShopEmberSlot.Color, SkillSummary.Color, etc.)
// -- the in-game UI's own label for this step is "Aspect", but that's a display-layer choice, not a
// wire-contract rename; see Anima.Core.Reforge.ReforgeService's own terminology note for why
// "Aspect" used to mean Head/Frame/Tail and no longer does anywhere in this codebase.

public record GetReforgeBrowseOptionsRequest(string Color);

// SkillName is globally unique across all 48 real skills (confirmed by reading PrimitiveRoster/
// SkillPool, not assumed), so it alone is enough to identify a pick again in
// GetReforgeValidTargets/AcceptReforge -- no synthetic id needed. Part is Skill.Part.ToString() --
// which slot this pick would occupy is now fully determined by the skill itself, since there's no
// separate "pick a slot" step anymore in the reordered flow.
public record ReforgeSkillOption(string ArchetypeName, string SkillName, string Part, string Color);

public record ReforgeValidTargetsRequest(string SkillName);

// Cost is per-target (same-color-body-match vs. cross-color, hybrids always cross-color -- see
// ReforgeService.GetAcceptCost) and already has Ember Core's discount applied, same convention
// ShopStockSnapshot's prices already use -- the client's confirm screen needs the real charge for
// THIS target, not a value it has to re-derive.
public record ReforgeTargetOption(string AnimaId, int Cost);

// InvalidTargetAnimaIds is everyone else on the team -- everyone for whom this specific skill would
// be a no-op (already equipped in that Part, whether from the Anima's own real genome or an
// already-Accepted override earlier this Delve). The client renders all (up to) 3 team cards and
// skips/disables the invalid ones, per the task's own instruction, rather than silently omitting
// them.
public record ReforgeValidTargetsResult(IReadOnlyList<ReforgeTargetOption> ValidTargets, IReadOnlyList<string> InvalidTargetAnimaIds);

public record AcceptReforgeRequest(string SkillName, string AnimaId);

// Outcome is "Success" | "InsufficientWisp" -- a DISTINCT rejection shape (not a generic
// HubException, unlike RestAtShop/BuyWaresEmber/BuyWaresArtifact's existing "Insufficient Wisp"
// throw) specifically so the client can show "not enough Wisp -- needed X, have Y" and route back
// to the Reforge/Leave landing screen. Cost/WispBalance are populated either way (the quoted price
// and the player's actual current balance); on InsufficientWisp nothing was spent and no override
// was recorded -- see ReforgeService.Accept's own comment for why nothing partially commits here.
public record AcceptReforgeResult(string Outcome, int Cost, int WispBalance);

// ---- Weaving ----

public record AttemptWeaveRequest(string ParentAId, string ParentBId, bool SpendEchoShards);

// GrantsShield (NEW, Anima Profile session) -- same signal AnimaPartSummary already carries for
// Sanctum/Hub's icon coloring (Category alone can't distinguish a shield-granting Buff from any
// other Buff); added here too since the Threads section's dot-accent coloring needs the identical
// sword/heart/shield/bolt/diamond rule and Dominant/R1/R2 are all SkillSummary, not AnimaPartSummary.
public record SkillSummary(string Name, string Category, string Color, bool GrantsShield);

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

// ---- Starter Anima Reveal (NEW) ----
//
// One trio slot's rolled-but-not-yet-named genome. SlotNumber/TotalCount are the ORIGINAL 1-based
// position ("2 of 3") and the fixed total (always 3) -- SlotNumber stays fixed across a reconnect,
// so resuming after naming slot 1 still reads "2 of 3", not "1 of 2" (GetPendingStarterReveal only
// ever returns the REMAINING unnamed entries, never the already-confirmed ones). ArchetypeName is
// the naming textbox's pre-filled default (e.g. "Lotus"), editable -- NOT the color name.
public record StarterRevealEntry(int SlotNumber, int TotalCount, string ArchetypeName, WeaveGenomePreview Genome);

public record ConfirmStarterAnimaRequest(string Name);

// Remaining is empty once all 3 slots are named -- the client's signal to navigate to hub.tscn
// instead of showing another reveal.
public record StarterAnimaConfirmResult(AnimaSummary Anima, IReadOnlyList<StarterRevealEntry> Remaining);

// Anima Profile's own dedicated read: R1/R2 per part (the "Show hidden" toggle) plus resolved
// Parent/Echo-Twin names, closing both Profile-facing gaps flagged in the Phase 1 report. Not
// folded into AnimaSummary/GetRoster -- Sanctum's grid never needs hidden Threads, only Profile
// does, so this stays a separate, deliberately un-batched call.
// ---- Combat (Phase 5a: core loop only -- no rewards, no Boss-hatch, see GameHub's own comment) ----

// Side is "Player" or "Enemy"; Index is the combatant's position within that side's PlayerTeam/
// EnemyTeam list -- stable for the life of one combat (both lists are append-only: ResolveSummon
// can add a new Enemy mid-fight, but nothing ever removes or reorders an entry, dead combatants
// just sit at CurrentHp 0). Enemy has no Id field the way Anima does, so this pair is the one
// identity scheme that works for both sides uniformly.
public record CombatantRef(string Side, int Index);

// Statuses is just the keyword list (e.g. ["Shield", "Weak"]) -- magnitude/duration/charges are
// deliberately omitted from this first wire shape; add them if/when the real client build shows
// it needs them (see CLAUDE.md's own "don't design for hypothetical requirements" guidance).
public record CombatantSummary(
    string Side,
    int Index,
    string Name,
    int CurrentHp,
    int MaxHp,
    int Position,
    bool Alive,
    IReadOnlyList<string> Statuses);

// OwnerAnimaId is which of the 3 team Anima this card came from (Head/Frame/Tail) -- a real gap
// found while building Phase 5a's own verification harness: CombatEngine.ResolvePlayerAction
// rejects a card that isn't in the acting Anima's own DeckSkills, so a client has no way to know
// which of Hand's cards are even legal to try for the CURRENT actor without this.
public record HandCardSummary(int HandIndex, string OwnerAnimaId, string SkillName, string Category, string Color, int EnergyCost, string TargetType);

public record CombatTurnEntry(string Side, int Index, string Name);

// Outcome is "InProgress" | "Victory" | "Defeat". CurrentActorAnimaId is null once Outcome is
// terminal (nobody's turn anymore) -- while InProgress it's always set, since
// AdvanceUntilPlayerActionNeeded only ever pauses on a living player Anima's turn.
//
// The last three fields are Phase 5b's addition, all null except on the exact SubmitAction call
// that first reaches a terminal outcome (never on StartCombat/GetCombatState/GetLegalTargets,
// and never on an InProgress result) -- kept on this one shared shape rather than a bespoke
// "terminal result" wrapper so the client doesn't need a different response type depending on
// whether the fight happened to end this call:
// - VictoryReward: set for a Combat/Elite/Boss Victory. Boss additionally sets BossHatchPreview.
// - BossHatchPreview: set only for a Boss Victory -- the just-rolled, not-yet-named genome for the
//   Anima Reveal screen; ConfirmBossHatch (mirroring ConfirmWeave) supplies the mandatory name.
// - DefeatSummary: set only for a Defeat (a wipe) -- the 50%-Wisp-kept "Delve Ended" summary.
public record CombatStatus(
    int RoundNumber,
    int SharedEnergy,
    IReadOnlyList<CombatantSummary> PlayerTeam,
    IReadOnlyList<CombatantSummary> EnemyTeam,
    IReadOnlyList<HandCardSummary> Hand,
    int DrawPileCount,
    int DiscardPileCount,
    IReadOnlyList<CombatTurnEntry> TurnOrder,
    int TurnIndex,
    string? CurrentActorAnimaId,
    string Outcome,
    IReadOnlyList<string> EventLog,
    CombatVictoryReward? VictoryReward = null,
    WeaveGenomePreview? BossHatchPreview = null,
    DelveEndSummary? DefeatSummary = null);

// HandIndex null = Pass. Target must be one of GetLegalTargets(HandIndex)'s entries, or null if
// that set came back empty (SelfTarget/AllAllies/AllEnemies skills need no explicit target).
public record SubmitActionRequest(string AnimaId, int? HandIndex, CombatantRef? Target);

// ---- Combat rewards / Delve end (Phase 5b) ----

// The economic side of a Combat/Elite/Boss Victory -- Wisp/Ember/Shard grants all share this one
// shape rather than three bespoke ones, matching DelveEndResult/ShopStockSnapshot's own "one
// shared DTO" convention. VesselShardGranted is only ever true for an Elite Victory (25% chance);
// EchoShardGranted is only ever true for a Boss Victory (guaranteed). PendingEmberColors is the
// account's FULL current queue, same convention CollectResourceResult/ClaimTreasureResult use.
public record CombatVictoryReward(int WispGranted, bool VesselShardGranted, bool EchoShardGranted, IReadOnlyList<string> PendingEmberColors);

// Wisp math mirrors Anima.Core.Economy.DelveEndService.DelveEndResult exactly (WispForfeited is
// always 0 for a Retreat, per its 100%-keep design) -- FloorIndexReached/NodesCleared add the
// map-progress half of the locked "Delve Ended"/"Delve Retreated" result-screen summary (CLAUDE.md's
// Match Result & Retreat System). FloorIndexReached is 0-indexed, same convention NodeRef.FloorIndex
// already uses; a client displaying "Floor 6" adds 1 itself, same as it already must for NodeRef.
public record DelveEndSummary(int WispEarnedThisRun, int WispKept, int WispForfeited, int FloorIndexReached, int NodesCleared);

// The mandatory naming step for a Boss Victory's guaranteed hatched Anima -- mirrors
// ConfirmWeaveRequest, but Boss-hatch only ever produces one Anima (no Echo-Twin-style pair), so
// this needs no second name field.
public record ConfirmBossHatchRequest(string Name);

// The Boss ceremony's appended "Delve Complete" summary (Phase 5c), per the locked Match Result
// design -- floors reached, Anima used, total Wisp earned this run. Deliberately NOT the same
// shape as DelveEndSummary (Defeat/Retreat): there's no keep/forfeit split here, since Victory has
// no Wisp penalty at all. FloorIndexReached is 0-indexed, same convention NodeRef/DelveEndSummary
// already use. Rides along on ConfirmBossHatch's response (see BossHatchConfirmResult) rather than
// needing its own hub method -- see ConfirmBossHatch's own comment for why.
public record DelveCompleteSummary(int FloorIndexReached, int NodesCleared, IReadOnlyList<string> AnimaUsedNames, int TotalWispEarnedThisRun);

// DelveComplete is null only if a reconnect happened between Boss Victory and this call (see
// Sessions.DelveCompleteSnapshot's own comment) -- every real, uninterrupted Boss Victory populates
// it.
public record BossHatchConfirmResult(AnimaSummary Anima, DelveCompleteSummary? DelveComplete);

// One row from DelveHistoryEntity -- the capped last-5 (newest first) per-Anima log backing Anima
// Profile's "Delve History" section. Outcome is the DelveOutcome enum's ToString() (Victory/
// Defeat/Retreat), same string-DTO convention every other wire enum in this file already uses.
public record DelveHistoryEntry(
    string Outcome,
    int FloorIndexReached,
    int CombatsWon,
    int ElitesDefeated,
    bool BossDefeated,
    IReadOnlyList<string> TeammateNames,
    int WispEarnedThisRun,
    DateTime Timestamp);

// A full-sibling link for Anima Profile's Lineage section -- resolved the same "this account's
// roster is already fully loaded in-memory" way Parent/Echo-Twin names already are (see
// GetAnimaDetail's own comment), via WeavingService.AreFullSiblings against every other roster
// Anima. Unlike Parent/Echo-Twin (each at most one), a Weave pair can produce siblings across
// multiple separate Weaves, so this is a list, not a nullable single Id/Name pair.
public record SiblingRef(string Id, string Name);

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
    string? EchoTwinName,
    IReadOnlyList<SiblingRef> Siblings,
    int CompletedDelveCount,
    int FailedDelveCount,
    IReadOnlyList<DelveHistoryEntry> RecentDelveHistory);
