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

// Discovered mirrors AccountArtifactStatEntity's "row exists" convention -- real as of this
// session for anything ever granted by ClaimTreasureNode. DelvesWonWith is still always 0: that
// write path needs Boss-victory resolution (Phase 5), not wired up yet.
public record ArtifactSummary(string Name, string Description, bool Discovered, int DelvesWonWith);

public record NodeRef(int FloorIndex, int Column, string? Type);

public record DelveStatus(NodeRef? CurrentNode, IReadOnlyList<NodeRef> AvailableNodes, int WispEarnedSoFar);

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
