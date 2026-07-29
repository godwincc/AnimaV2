extends Control

## Combat screen -- PHASE 1 (static layout + real-data rendering only, no player interaction).
## See CLAUDE.md's "Combat Screen Design" section for the locked layout this follows.
##
## Same child-panel/shared-connection architecture as resource_node.gd/treasure_node.gd/
## reforge_node.gd/shop_node.gd -- instanced into delve.gd's NodeEncounterOverlay, start(hub,
## node_type) receives delve.gd's already-open HubConnection via loose .call(), same as those four.
## node_type ("Combat"/"Elite"/"Boss") is passed in explicitly by delve.gd rather than read off the
## wire -- CombatStatus (confirmed by reading GameHubModels.cs directly) carries NO node-type field
## at all, so the ONLY way this screen knows which background tier to render is the same way every
## other node screen already knows what kind of node it's rendering: the caller (delve.gd) already
## knows CurrentNode.Type from the DelveStatus that routed here in the first place.
##
## RPC NAMES/SHAPES CONFIRMED AGAINST REAL CODE (GameHub.cs/GameHubModels.cs), NOT CLAUDE.md PROSE --
## CLAUDE.md's own Combat System section names StartCombat/SubmitAction/GetLegalTargets/
## GetCombatState, which this pass confirmed ARE the real public RPC names (unlike Shop/Reforge,
## where CLAUDE.md's prose had drifted from the real surface). Real wire shapes used this pass:
## - StartCombat() / GetCombatState() -> CombatStatus(RoundNumber, SharedEnergy, PlayerTeam
##   [CombatantSummary], EnemyTeam [CombatantSummary], Hand [HandCardSummary], DrawPileCount,
##   DiscardPileCount, TurnOrder [CombatTurnEntry], TurnIndex, CurrentActorAnimaId, Outcome,
##   EventLog [string], VictoryReward?, BossHatchPreview?, DefeatSummary?).
## - CombatantSummary(Side, Index, Name, CurrentHp, MaxHp, Position, Alive, Statuses[keyword string],
##   Shield[int]). NO AnimaId field on this shape (confirmed) -- see PLAYER-SIDE SPRITE MAPPING below
##   for the real gap this creates and how this pass works around it (still open as of Phase 4 --
##   confirmed irrelevant to targeting, which uses Side/Index instead). Shield is a Phase 4 addition
##   (GameHubModels.cs/GameHub.cs) -- Statuses stayed keyword-only for every OTHER status, Shield got
##   its own numeric field specifically because the hit-feedback diff needs to detect "Shield rose",
##   not just "the Shield keyword is present."
## - HandCardSummary(HandIndex, OwnerAnimaId, SkillName, Category, Color, EnergyCost, TargetType,
##   Description). Description (NEW, Combat Phase 5) closed the TODO #23(b) gap -- server now
##   synthesizes it via the same BuildSkillDescription helper AnimaPartSummary/ReforgeSkillOption
##   already use. The old "<Category> · <EnergyCost> Energy" placeholder caption is gone; hovering a
##   hand card now shows the real description in the docked message area (see the PHASE 5 comment
##   near _build_hand_card below for the hover-revert mechanism).
##
## PLAYER-SIDE SPRITE MAPPING (real wire-shape gap, worked around not silently ignored):
## CombatantSummary has no AnimaId, so there is no direct, guaranteed-correct way to map a Player-side
## combatant back to that Anima's Color (needed to pick its Aspect sprite) by index alone --
## CombatStatus.PlayerTeam's index order mirrors DelveRun.Team's order, which is StartDelve's
## TeamAnimaIds order (whatever order the player's LAST SetTeam call submitted), which is NOT
## guaranteed to match GetRoster()'s own order (Session.Roster.Animas' insertion order, confirmed by
## reading GameHub.cs) -- these two orderings can legitimately differ. This screen instead matches by
## NAME: it fetches the real team roster via GetRoster() (filtered to InTeam entries, same call every
## other screen already makes) and looks up each CombatantSummary.Name against that dict to find the
## real Color. This works correctly for any real team (Anima names are player-chosen and not
## normally duplicated within one 3-member team) but is NOT a strict identity guarantee -- flagged
## for Phase 2: if this ever becomes a real problem, the fix is adding AnimaId to CombatantSummary
## directly, mirroring HandCardSummary's own OwnerAnimaId field.
##
## ENEMY-SIDE SPRITE MAPPING: Enemy.Name (confirmed via SampleEnemies.cs) is one of exactly 5 real,
## fixed strings ("Quillfang", "Grovehide", "The Sentinel", "Leech Mother", "Warden of the Hollow"),
## matched directly via ENEMY_SPRITES below against the 5 real imported sprites in
## client/assets/enemies/ (TODO #16, finally wired up this pass). A future 6th enemy with no matching
## entry falls back to a plain darkened tile (same "graceful fallback" spirit as HYBRID_FALLBACK_TINTS
## already uses for Vulcan/Mirage on the player side), not a hard error.
##
## PHASE 2 additions (turn-gated hand, card ownership, Pass wiring): see the "PHASE 2" comment block
## right above _render_hand/_build_hand_card below for the full detail. Short version: hand cards now
## show which Anima they belong to (HandCardSummary.OwnerAnimaId, matched against the team roster --
## this field ALREADY existed on the wire, confirmed by re-reading GameHubModels.cs, so no server
## change was needed here, unlike the Description-field gap which still stands), only the current
## turn's Anima has clickable-ready cards (two distinct dim treatments: "not your turn" vs "not
## enough Energy"), and the Pass button is now real (calls SubmitAction with HandIndex=null).
##
## PHASE 3 (real targeting, Confirm/Cancel, real card play) -- audit findings first, then what was
## built, per CLAUDE.md's "Combat Screen -- Phase 3" section (full detail there):
## - SubmitActionRequest.Target is a CombatantRef(Side, Index) -- the EXACT SAME identity scheme
##   CombatantSummary already carries (Side/Index fields), confirmed by reading GameHubModels.cs'
##   own comment on CombatantRef. So targeting needs NO AnimaId at all -- the long-open "CombatantSummary
##   has no AnimaId" gap (still open, see top-of-file PLAYER-SIDE SPRITE MAPPING comment) turned out to
##   be irrelevant to targeting; it only ever mattered for arena sprite-color lookup, a separate concern.
## - HandCardSummary.TargetType (already on the wire, confirmed) is the real skill's raw TargetType enum
##   name as a string ("SelfTarget", "Enemy", "LowestHpEnemy", "Ally", "LowestHpAlly", "ChosenEnemy",
##   "ChosenAny", "AllEnemies", "AllAllies") -- so target-type IS directly exposed per card; no need to
##   infer it by calling GetLegalTargets first and reasoning about the result shape.
## - GetLegalTargets(handIndex) (confirmed via CombatEngine.GetLegalTargets, read directly, not
##   assumed): SelfTarget returns exactly [actor's own CombatantRef] (NOT empty); AllEnemies/AllAllies
##   return [] (genuinely empty, no explicit target possible); every other TargetType returns a REAL
##   list of legal targets -- even when it algorithmically resolves to a single option (e.g. Enemy/
##   LowestHpEnemy against one living enemy), it's still returned as a one-element list specifically so
##   the client's highlight-then-click flow works uniformly. So "skip straight to Confirm" is ONLY
##   correct for TargetType == "SelfTarget" (checked via the wire field directly, not by comparing list
##   length) and for the genuinely-empty AoE case -- every other case, including an incidentally-single-
##   option list, goes through the real highlight-and-click step, per CombatEngine's own comment.
## - REAL DISCREPANCY FOUND, worked around correctly: SubmitActionRequest's own doc comment claims
##   "Target ... null if that set came back empty (SelfTarget/AllAllies/AllEnemies skills need no
##   explicit target)" -- but SubmitAction's actual validation (`legalTargets.Count > 0 && (explicitTarget
##   == null || !legalTargets.Contains(explicitTarget))`) means a SelfTarget skill's non-empty
##   [actor]-only list DOES require an explicit, non-null Target matching the actor, contradicting that
##   comment. This client always sends the resolved CombatantRef explicitly for SelfTarget (never null),
##   and only sends null when GetLegalTargets genuinely returned an empty list (true AoE).
## Implementation: clicking an eligible hand card calls GetLegalTargets, then either jumps straight to
## a Confirm prompt (SelfTarget/AoE) or enters a CHOOSING_TARGET phase that outlines only the legal
## arena cards (amber border, not a full recolor) and disables everything else. Clicking a highlighted
## target (or having skipped straight there) shows "Play [skill][ on [target]]? Confirm / Cancel" in the
## message area (Cancel is also shown during target-choosing, as a deliberate small escape-hatch beyond
## the literal ask, so a misclick can't strand the player -- see CLAUDE.md). Confirm calls SubmitAction
## with the real HandIndex/Target and re-renders the whole screen from the returned CombatStatus, same
## re-render path Pass already uses. No hit-feedback animation yet (Phase 4).
##
## PHASE 5 (Match Result: Victory/Defeat, real win/loss flow) -- see match_result.gd's own
## top-of-file comment for the full shared-component contract (one component, gated by mode, reused
## by BOTH this screen's Victory/Defeat and delve.gd's Retreat). _check_for_match_result() (called
## after every SubmitAction/Pass response) detects the exact response that first reaches a terminal
## CombatStatus.Outcome and opens it as a child overlay (MatchResultOverlay), same child-panel
## pattern this codebase already uses everywhere else (AugmentOverlay, NodeEncounterOverlay). Two
## exit signals distinguish where the CALLER (delve.gd) should end up: `continue_to_map` (Combat/
## Elite Victory -- node already cleared server-side, just refresh and stay on the map) vs.
## `delve_ended` (Boss Victory once its Delve Complete summary is dismissed, or a Defeat -- the
## Delve itself already ended server-side, so delve.gd navigates to hub.tscn instead).
##
## DEV-ONLY "Back to Map" button (bottom of screen, NOT part of the locked Combat Screen Design):
## still needed for a mid-fight escape hatch (Phase 5 only handles the fight's real conclusion, not
## backing out of an IN-PROGRESS one) -- pressing it only tears down THIS client-side panel (emits
## `resolved`, same signal convention every other node screen uses) without any server RPC, so the
## real ActiveCombat state is untouched server-side (GetCombatState's own resume support already
## covers reconnecting into it later). Keep until a real "concede/flee mid-combat" design exists.

signal resolved
## PHASE 5: fires instead of `resolved` when the match-result flow ends with "Return to Hub"
## (Boss Victory's Delve Complete summary, or a Defeat) -- the Delve itself has ended server-side
## (Session.ActiveDelveRun cleared, see SubmitAction's own comment), so delve.gd navigates to
## hub.tscn rather than just tearing down this panel and staying on the map.
signal delve_ended

# Warm sanctuary/workshop theme (Normal tier), identical top/mid/bottom stops to every other real
# screen. Elite/Boss tiers below are CLAUDE.md's own locked room-background palettes -- this is the
# first real Godot implementation of all three (Combat had no client screen before this pass).
const NORMAL_GRADIENT := ["4a3a2e", "2b2018", "1a130e"]
const ELITE_GRADIENT := ["5a2e3a", "341f2b", "180d12"]
const BOSS_GRADIENT := ["6b1a1a", "2b0d0d", "0a0505"]

const NORMAL_ARENA_INSET := Color(0, 0, 0, 0.42)
const ELITE_ARENA_INSET := Color(0.078, 0, 0.039, 0.48)
const BOSS_ARENA_INSET := Color(0, 0, 0, 0.6)
const BOSS_VIGNETTE_COLOR := Color(0.545, 0, 0, 0.35)

# PHASE 4: hit-feedback flash/rising-number colors, per the locked design (red=damage, green=heal,
# silver-blue=shield). Shield's silver-blue has no existing shared constant elsewhere in this file
# (COLOR_HP_GREEN/COLOR_HP_RED are reused directly for heal/damage instead of duplicating hex values).
const HIT_FLASH_SHIELD := "7fb6d9"
const HIT_NUMBER_RISE_PIXELS := 36.0
const HIT_NUMBER_DURATION := 0.9
const HIT_FLASH_DURATION := 0.35

const COLOR_CARD_BG := "1e1610"
const COLOR_CARD_BORDER := "c9b89e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_TEXT_CREAM_DIM := "e8cf9a"
const COLOR_TEXT_MUTED := "a89680"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_ERROR := "e2554a"
const COLOR_HP_GREEN := "6cb87c"
const COLOR_HP_AMBER := "e8b95a"
const COLOR_HP_RED := "e2554a"
const COLOR_ENEMY_ACCENT := "8a8175" # locked "coal-gray" turn-queue accent for enemy-side entries

const MAX_ENERGY := 9 # CLAUDE.md's Energy System: +3/Round, capped at 9 (confirmed, not 6)

# Real Aspect sprites -- identical mapping to hub.gd/delve.gd/reforge_node.gd's own copies.
const ASPECT_SPRITES := {
	"Crimson": "res://assets/aspects/crimson.png",
	"Onyx": "res://assets/aspects/onyx.png",
	"Verdant": "res://assets/aspects/verdant.png",
	"Azure": "res://assets/aspects/azure.png",
}
const HYBRID_FALLBACK_TINTS := {
	"Vulcan": "6a3a52",
	"Mirage": "3a5a6a",
}

# Real enemy sprites (TODO #16 -- wired up for the first time this pass). Names are the exact,
# fixed Enemy.Name strings confirmed via Anima.Core.Data.SampleEnemies.
const ENEMY_SPRITES := {
	"Quillfang": "res://assets/enemies/quillfang.png",
	"Grovehide": "res://assets/enemies/grovehide.png",
	"The Sentinel": "res://assets/enemies/sentinel.png",
	"Leech Mother": "res://assets/enemies/leechmother.png",
	"Warden of the Hollow": "res://assets/enemies/warden.png",
}

# Same 12-entry icon mapping as delve.gd/shop_node.gd/collection.gd's own copies (duplicated per
# this codebase's existing per-screen convention -- neither icon kind nor order exists on the wire).
const ARTIFACT_ICONS := {
	"Twin Flame": "flame",
	"Wisp Charm": "sparkle",
	"Barrier Stone": "shield",
	"Vanguard's Bell": "bell",
	"Weaver's Thread": "diagonal_lines",
	"Marked Coin": "coin_star",
	"Withering Fang": "tooth",
	"Focusing Lens": "magnifying_glass",
	"Silent Chime": "asterisk",
	"Ember Core": "sun",
	"Sapling Charm": "leaf",
	"Sifting Stone": "recycle",
}

# Same per-color hex tint convention shop_node.gd's own EMBER_COLOR_TINTS already uses (duplicated,
# not shared) -- used to tint a hand card's cost pip / art-block placeholder by the SKILL's own
# archetype color (HandCardSummary.Color), not the owning Anima's body color.
const SKILL_COLOR_TINTS := {
	"Crimson": "8a3a3a",
	"Onyx": "4a4a52",
	"Verdant": "3a6a4a",
	"Azure": "3a5a7a",
}

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")
const MATCH_RESULT_SCENE := preload("res://scenes/match_result.tscn")

@onready var _background: TextureRect = $Background
@onready var _match_result_overlay: Control = $MatchResultOverlay
@onready var _card: PanelContainer = $CenterContainer/Card
@onready var _status_label: Label = $CenterContainer/Card/Margin/Content/StatusLabel
@onready var _round_label: Label = $CenterContainer/Card/Margin/Content/HudRow/RoundLabel
@onready var _wisp_icon: Control = $CenterContainer/Card/Margin/Content/HudRow/WispIcon
@onready var _wisp_label: Label = $CenterContainer/Card/Margin/Content/HudRow/WispLabel
@onready var _artifacts_row: HBoxContainer = $CenterContainer/Card/Margin/Content/HudRow/ArtifactsRow
@onready var _arena_panel: PanelContainer = $CenterContainer/Card/Margin/Content/ArenaPanel
@onready var _arena_vignette: TextureRect = $CenterContainer/Card/Margin/Content/ArenaPanel/ArenaVignette
@onready var _player_column: HBoxContainer = $CenterContainer/Card/Margin/Content/ArenaPanel/ArenaMargin/ArenaRow/PlayerColumn
@onready var _enemy_column: HBoxContainer = $CenterContainer/Card/Margin/Content/ArenaPanel/ArenaMargin/ArenaRow/EnemyColumn
@onready var _message_icon: Control = $CenterContainer/Card/Margin/Content/MessageBar/MessageMargin/MessageRow/MessageIcon
@onready var _message_label: Label = $CenterContainer/Card/Margin/Content/MessageBar/MessageMargin/MessageRow/MessageLabel
@onready var _confirm_button: Button = $CenterContainer/Card/Margin/Content/MessageBar/MessageMargin/MessageRow/ConfirmButton
@onready var _cancel_button: Button = $CenterContainer/Card/Margin/Content/MessageBar/MessageMargin/MessageRow/CancelButton
@onready var _turn_queue_list: VBoxContainer = $CenterContainer/Card/Margin/Content/LowerRow/TurnQueuePanel/TurnQueueMargin/TurnQueueContent/TurnQueueList
@onready var _energy_label: Label = $CenterContainer/Card/Margin/Content/LowerRow/RightColumn/StatusRow/EnergyLabel
@onready var _energy_pips_row: HBoxContainer = $CenterContainer/Card/Margin/Content/LowerRow/RightColumn/StatusRow/EnergyPipsRow
@onready var _deck_discard_label: Label = $CenterContainer/Card/Margin/Content/LowerRow/RightColumn/StatusRow/DeckDiscardLabel
@onready var _pass_button: Button = $CenterContainer/Card/Margin/Content/LowerRow/RightColumn/StatusRow/PassButton
@onready var _hand_row: HBoxContainer = $CenterContainer/Card/Margin/Content/LowerRow/RightColumn/HandRow
@onready var _combat_log_list: VBoxContainer = $CenterContainer/Card/Margin/Content/CombatLogPanel/CombatLogMargin/CombatLogScroll/CombatLogList
@onready var _dev_back_button: Button = $CenterContainer/Card/Margin/Content/FooterRow/DevBackButton

var _hub: HubConnection
var _node_type: String = "Combat"
var _status: Dictionary = {}
var _team_color_by_name: Dictionary = {} # {name: color}, see top-of-file comment (sprite mapping)
var _team_by_id: Dictionary = {} # {animaId: {"name":String, "color":String}}, see PHASE 2 comment (card ownership)
var _wisp_balance: int = 0
var _artifacts: Array = []
var _busy: bool = false

# ---- PHASE 3: targeting/confirm state machine ----
# PHASE_NONE: no action in progress, hand/Pass behave as Phase 2 already had them.
# PHASE_CHOOSING_TARGET: GetLegalTargets came back with a real (non-self, non-empty) choice --
# arena highlights the legal set, everything else (other hand cards, Pass) is inert.
# PHASE_CONFIRMING: either skipped straight here (SelfTarget/AoE) or a legal target was clicked --
# message area shows the real Confirm/Cancel prompt.
const PHASE_NONE := 0
const PHASE_CHOOSING_TARGET := 1
const PHASE_CONFIRMING := 2

var _phase: int = PHASE_NONE
var _phase_hand_index: int = -1
var _phase_anima_id: String = ""
var _phase_skill_name: String = ""
var _phase_target_type: String = ""
var _phase_legal_targets: Array = [] # Array of {"side":String,"index":int} from GetLegalTargets
var _phase_chosen_target: Dictionary = {} # {} = implicit null target (true AoE); else {"side":,"index":}

# PHASE 4: {"Side:Index": Control (the combatant's portrait_wrap)}, rebuilt every _render_arena()
# call -- lets hit-feedback find the right portrait to flash/float a number over, keyed the same
# Side/Index way targeting already does (see Phase 3's own audit: no AnimaId needed for this either).
var _arena_portrait_by_ref: Dictionary = {}


func _ready() -> void:
	_apply_static_theme()
	_dev_back_button.pressed.connect(_on_dev_back_pressed)
	_pass_button.pressed.connect(_on_pass_pressed)
	_confirm_button.pressed.connect(_on_confirm_pressed)
	_cancel_button.pressed.connect(_on_cancel_pressed)


## Called by delve.gd right after instancing this scene. node_type is "Combat"/"Elite"/"Boss" --
## see top-of-file comment for why this can't be read off CombatStatus itself.
func start(hub: HubConnection, node_type: String) -> void:
	_hub = hub
	_node_type = node_type
	_apply_background_tier(node_type)
	_load()


func _load() -> void:
	_status_label.text = ""

	var roster: Variant = await _hub.invoke("GetRoster", [])
	for a: Variant in (roster if roster is Array else []):
		if a is Dictionary and bool(a.get("inTeam", false)):
			var a_name := str(a.get("name", ""))
			var a_color := str(a.get("color", ""))
			_team_color_by_name[a_name] = a_color
			_team_by_id[str(a.get("id", ""))] = {"name": a_name, "color": a_color}

	var ledger: Variant = await _hub.invoke("GetLedger", [])
	if ledger is Dictionary:
		_wisp_balance = int((ledger.get("balances", {}) as Dictionary).get("Wisp", 0))

	var delve_status: Variant = await _hub.invoke("GetDelveStatus", [])
	if delve_status is Dictionary:
		_artifacts = delve_status.get("artifacts", [])

	# StartCombat is idempotent -- a still-uncleared node's repeat call just returns the existing
	# ActiveCombat state (confirmed by reading GameHub.cs), so calling it unconditionally on load is
	# safe whether this is a fresh arrival or a re-render.
	var combat_result: Variant = await _hub.invoke("StartCombat", [])
	if not (combat_result is Dictionary):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not start combat -- check your connection, or this node may already be cleared."
		return

	_status = combat_result
	_render_all()


func _render_all() -> void:
	_reset_phase()
	_render_hud()
	_render_artifacts()
	_render_arena()
	_render_message_bar()
	_render_turn_queue()
	_render_status_row()
	_render_hand()
	_render_combat_log()


## Re-renders just the parts affected by a targeting/confirm phase transition -- no new CombatStatus
## involved (nothing server-side changed yet), so _status itself is untouched.
func _refresh_interactive_ui() -> void:
	_render_arena()
	_render_hand()
	_render_message_bar()
	_render_status_row()


# ---- HUD ----

func _render_hud() -> void:
	var round_number := int(_status.get("roundNumber", 1))
	_round_label.text = "Round %d -- %s" % [round_number, _node_type]
	_wisp_label.text = str(_wisp_balance)


func _render_artifacts() -> void:
	for child in _artifacts_row.get_children():
		child.free()
	for artifact: Variant in _artifacts:
		if not (artifact is Dictionary): continue
		var name: String = str(artifact.get("name", ""))
		var icon := Control.new()
		icon.set_script(ICON_GLYPH_SCRIPT)
		icon.set("icon_kind", ARTIFACT_ICONS.get(name, "gift"))
		icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
		icon.custom_minimum_size = Vector2(20, 20)
		icon.set("icon_size", 20.0)
		_artifacts_row.add_child(icon)


# ---- Arena ----

func _render_arena() -> void:
	for child in _player_column.get_children():
		child.free()
	for child in _enemy_column.get_children():
		child.free()
	_arena_portrait_by_ref.clear()

	var current_side := ""
	var current_index := -1
	var turn_order: Array = _status.get("turnOrder", [])
	var turn_index := int(_status.get("turnIndex", -1))
	if turn_index >= 0 and turn_index < turn_order.size() and turn_order[turn_index] is Dictionary:
		var current_entry: Dictionary = turn_order[turn_index]
		current_side = str(current_entry.get("side", ""))
		current_index = int(current_entry.get("index", -1))

	var player_team: Array = (_status.get("playerTeam", []) as Array).duplicate()
	player_team.sort_custom(func(a: Dictionary, b: Dictionary): return int(a.get("position", 0)) > int(b.get("position", 0)))
	for summary: Variant in player_team:
		if summary is Dictionary:
			var idx := int(summary.get("index", -1))
			var is_current := current_side == "Player" and current_index == idx
			var is_legal := _is_legal_target("Player", idx)
			_player_column.add_child(_build_combatant_card(summary, true, is_current, is_legal))

	var enemy_team: Array = (_status.get("enemyTeam", []) as Array).duplicate()
	enemy_team.sort_custom(func(a: Dictionary, b: Dictionary): return int(a.get("position", 0)) < int(b.get("position", 0)))
	for summary: Variant in enemy_team:
		if summary is Dictionary:
			var idx := int(summary.get("index", -1))
			var is_current := current_side == "Enemy" and current_index == idx
			var is_legal := _is_legal_target("Enemy", idx)
			_enemy_column.add_child(_build_combatant_card(summary, false, is_current, is_legal))


## True only during PHASE_CHOOSING_TARGET, and only for entries actually present in the
## GetLegalTargets result for the card currently being played -- see this function's callers for how
## that renders as an amber outline (not a full recolor) on the arena.
func _is_legal_target(side: String, index: int) -> bool:
	if _phase != PHASE_CHOOSING_TARGET:
		return false
	for t: Variant in _phase_legal_targets:
		if t is Dictionary and str(t.get("side", "")) == side and int(t.get("index", -1)) == index:
			return true
	return false


func _build_combatant_card(summary: Dictionary, is_player_side: bool, is_current: bool, is_legal_target: bool = false) -> Control:
	var card := VBoxContainer.new()
	card.custom_minimum_size = Vector2(140, 0)
	card.add_theme_constant_override("separation", 4)

	var alive := bool(summary.get("alive", true))
	if not alive:
		card.modulate = Color(1, 1, 1, 0.4)

	var portrait_wrap := AspectRatioContainer.new()
	portrait_wrap.ratio = 1.0
	portrait_wrap.custom_minimum_size = Vector2(84, 84)
	portrait_wrap.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	card.add_child(portrait_wrap)

	# PHASE 4: registered by Side:Index (same key scheme as targeting) so hit-feedback can find
	# this exact portrait after a SubmitAction response re-renders the arena.
	_arena_portrait_by_ref["%s:%d" % [str(summary.get("side", "")), int(summary.get("index", -1))]] = portrait_wrap

	if alive:
		var texture_path := _sprite_path_for(summary, is_player_side)
		if texture_path != "":
			var portrait := TextureRect.new()
			portrait.texture = load(texture_path)
			portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			portrait.stretch_mode = TextureRect.STRETCH_SCALE
			portrait_wrap.add_child(portrait)
		else:
			var fallback := ColorRect.new()
			fallback.color = Color(_fallback_tint_for(summary, is_player_side))
			portrait_wrap.add_child(fallback)
	else:
		var dead_fill := ColorRect.new()
		dead_fill.color = Color("0a0505")
		portrait_wrap.add_child(dead_fill)

	var name_label := Label.new()
	name_label.text = str(summary.get("name", "?"))
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	name_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	card.add_child(name_label)

	var current_hp := int(summary.get("currentHp", 0))
	var max_hp: int = max(1, int(summary.get("maxHp", 1)))
	var ratio: float = float(current_hp) / float(max_hp)

	var hp_bar := ProgressBar.new()
	hp_bar.min_value = 0
	hp_bar.max_value = max_hp
	hp_bar.value = current_hp
	hp_bar.show_percentage = false
	hp_bar.custom_minimum_size = Vector2(0, 8)
	var hp_bg := StyleBoxFlat.new()
	hp_bg.bg_color = Color(0, 0, 0, 0.4)
	hp_bg.set_corner_radius_all(3)
	var hp_fill := StyleBoxFlat.new()
	hp_fill.bg_color = Color(_hp_bar_color(ratio))
	hp_fill.set_corner_radius_all(3)
	hp_bar.add_theme_stylebox_override("background", hp_bg)
	hp_bar.add_theme_stylebox_override("fill", hp_fill)
	card.add_child(hp_bar)

	var hp_label := Label.new()
	hp_label.text = "%d/%d" % [current_hp, max_hp]
	hp_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hp_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	hp_label.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
	card.add_child(hp_label)

	# Fixed-height buff/debuff row -- reserved even when empty, so the position-number line below
	# always sits at the same vertical offset regardless of how many statuses a combatant carries.
	var buff_row := HBoxContainer.new()
	buff_row.custom_minimum_size = Vector2(0, 20)
	buff_row.alignment = BoxContainer.ALIGNMENT_CENTER
	buff_row.add_theme_constant_override("separation", 3)
	for keyword: Variant in summary.get("statuses", []):
		var chip := Label.new()
		chip.text = str(keyword)
		chip.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
		chip.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
		buff_row.add_child(chip)
	card.add_child(buff_row)

	var position_label := Label.new()
	position_label.text = str(int(summary.get("position", 0)))
	position_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	if not alive:
		position_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED, 0.5))
	elif is_current:
		position_label.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
	else:
		position_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	position_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	card.add_child(position_label)

	if not is_legal_target:
		return card

	# Legal-target outline -- a border wrap, NOT a full recolor (per the locked Combat Screen Design's
	# own "outline ONLY the legal target set" language). Clickable: selecting this target moves the
	# phase straight to Confirm.
	var wrap := PanelContainer.new()
	var wrap_style := StyleBoxFlat.new()
	wrap_style.bg_color = Color(0, 0, 0, 0)
	wrap_style.border_color = Color(COLOR_ACCENT_AMBER)
	wrap_style.set_border_width_all(2)
	wrap_style.set_corner_radius_all(8)
	wrap_style.set_content_margin_all(4)
	wrap.add_theme_stylebox_override("panel", wrap_style)
	wrap.mouse_filter = Control.MOUSE_FILTER_STOP
	wrap.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	wrap.gui_input.connect(_on_arena_card_gui_input.bind(str(summary.get("side", "")), int(summary.get("index", -1))))
	wrap.add_child(card)
	return wrap


# ---- PHASE 4: hit-feedback (floating +/- numbers, portrait flash) ----
#
# Deliberately diff-based, NOT "animate whatever was clicked/targeted" -- reactive skills (Deflect,
# Ward, Retribution, Vengeance, Intercept) can redirect damage, grant shields, or trigger counter-
# effects the client never explicitly requested (confirmed real via Phase 3's own audit of
# CombatEngine). Snapshotting every combatant's HP/Shield before SubmitAction and diffing the WHOLE
# new CombatStatus against it afterward is the only way to show what actually happened, including
# to combatants the player never targeted (e.g. Retribution bouncing damage back onto the original
# attacker, or an AoE heal). Shield's numeric magnitude did not previously exist on the wire --
# CombatantSummary.Statuses was keyword-only by design ("add magnitude if/when the client needs
# it") -- so this pass added a single `Shield` int field to CombatantSummary/GameHub.cs rather than
# a general per-status magnitude map, since Shield is the only status this feature needs the number
# for.

## Captures every combatant's current HP/Shield from the CURRENTLY-RENDERED _status, keyed the same
## "Side:Index" way targeting/portrait-lookup already use. Called immediately before a SubmitAction
## request goes out (both Pass and a real card play), so the diff after the response reflects
## exactly what that one resolution changed -- not just on the acted-upon Anima, but both teams.
func _snapshot_combatants() -> Dictionary:
	var snapshot := {}
	for side_key in ["playerTeam", "enemyTeam"]:
		for entry: Variant in _status.get(side_key, []):
			if entry is Dictionary:
				var key := "%s:%d" % [str(entry.get("side", "")), int(entry.get("index", -1))]
				snapshot[key] = {"hp": int(entry.get("currentHp", 0)), "shield": int(entry.get("shield", 0))}
	return snapshot


## Diffs the just-rendered _status (post SubmitAction, already re-rendered via _render_all by the
## time this is called) against a snapshot taken before that same SubmitAction call, and spawns
## feedback for every combatant that actually changed -- both teams, not just the acting side.
## Shield increase takes priority over an HP change on the SAME combatant in the SAME resolution
## (per the locked design) since Guard Strike/Deflect-style self-shields commonly ride alongside a
## Shield gain being the more "interesting" of the two to surface.
##
## REAL BUG FOUND AND FIXED, confirmed live: _render_all() (called by the caller just before this)
## frees and rebuilds every arena portrait from scratch every time. Reading a freshly-created
## Control's global_position in the SAME frame it was added returns a stale/unlaid-out value --
## Godot's Container layout pass runs during the engine's own process step, not synchronously inside
## add_child(). Confirmed live: a floating number spawned near the arena's top-left origin instead
## of over the actually-changed combatant's real portrait. Same "await one frame so layout settles"
## idiom already used elsewhere in this codebase (delve.gd/hub.gd/sanctum.gd/etc.) fixes it -- await
## get_tree().process_frame before reading any portrait's global_position.
func _play_hit_feedback(before: Dictionary) -> void:
	await get_tree().process_frame

	for side_key in ["playerTeam", "enemyTeam"]:
		for entry: Variant in _status.get(side_key, []):
			if not (entry is Dictionary): continue
			var key := "%s:%d" % [str(entry.get("side", "")), int(entry.get("index", -1))]
			if not before.has(key): continue # newly-appeared combatant (e.g. Summon) -- nothing to diff against

			var prev: Dictionary = before[key]
			var hp_delta := int(entry.get("currentHp", 0)) - int(prev.get("hp", 0))
			var shield_delta := int(entry.get("shield", 0)) - int(prev.get("shield", 0))

			if shield_delta > 0:
				_spawn_hit_feedback(key, "+%d" % shield_delta, Color(HIT_FLASH_SHIELD))
			elif hp_delta > 0:
				_spawn_hit_feedback(key, "+%d" % hp_delta, Color(COLOR_HP_GREEN))
			elif hp_delta < 0:
				_spawn_hit_feedback(key, "%d" % hp_delta, Color(COLOR_HP_RED))


## Spawns one rising/fading number + a brief portrait flash over the given combatant's portrait.
## The number is a `top_level` Control (bypasses its parent's layout/clipping entirely, positioned
## via plain global coordinates) so it can float freely above the portrait's own
## AspectRatioContainer without fighting that container's single-child layout assumptions.
func _spawn_hit_feedback(ref_key: String, text: String, color: Color) -> void:
	var portrait: Control = _arena_portrait_by_ref.get(ref_key)
	if portrait == null: return # dead/removed combatant with nothing rendered to anchor onto

	var flash_tween := create_tween()
	portrait.modulate = color
	flash_tween.tween_property(portrait, "modulate", Color(1, 1, 1, 1), HIT_FLASH_DURATION)

	var label := Label.new()
	label.text = text
	label.add_theme_color_override("font_color", color)
	label.add_theme_font_size_override("font_size", UiTheme.SIZE_SUBHEADER)
	label.top_level = true
	label.z_index = 10
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(label)
	label.global_position = portrait.global_position + Vector2(portrait.size.x * 0.5 - 10.0, -6.0)

	var number_tween := create_tween()
	number_tween.set_parallel(true)
	number_tween.tween_property(label, "position:y", label.position.y - HIT_NUMBER_RISE_PIXELS, HIT_NUMBER_DURATION)
	number_tween.tween_property(label, "modulate:a", 0.0, HIT_NUMBER_DURATION)
	number_tween.set_parallel(false)
	number_tween.tween_callback(label.queue_free)


func _hp_bar_color(ratio: float) -> String:
	if ratio > 0.5: return COLOR_HP_GREEN
	if ratio > 0.25: return COLOR_HP_AMBER
	return COLOR_HP_RED


func _sprite_path_for(summary: Dictionary, is_player_side: bool) -> String:
	var name: String = str(summary.get("name", ""))
	if is_player_side:
		var color: String = str(_team_color_by_name.get(name, ""))
		return ASPECT_SPRITES.get(color, "")
	return ENEMY_SPRITES.get(name, "")


func _fallback_tint_for(summary: Dictionary, is_player_side: bool) -> String:
	if is_player_side:
		var color: String = str(_team_color_by_name.get(str(summary.get("name", "")), ""))
		return HYBRID_FALLBACK_TINTS.get(color, "555555")
	return "3a352f" # generic dark fallback tile for an unmapped future enemy, see top-of-file comment


# ---- Message area ----

## PHASE 3: the message bar now also carries the targeting/confirm prompt and the Confirm/Cancel
## buttons, gated by _phase. PHASE_NONE keeps the exact Phase 1/2 idle behavior (_render_idle_message).
func _render_message_bar() -> void:
	match _phase:
		PHASE_CHOOSING_TARGET:
			_message_label.text = "Choose a target for %s." % _phase_skill_name
			_confirm_button.visible = false
			_confirm_button.disabled = false
			_cancel_button.visible = true
			_cancel_button.disabled = false
		PHASE_CONFIRMING:
			_message_label.text = "Play %s%s? " % [_phase_skill_name, _confirm_target_clause()]
			_confirm_button.visible = true
			_confirm_button.disabled = false
			_cancel_button.visible = true
			_cancel_button.disabled = false
		_:
			_confirm_button.visible = false
			_cancel_button.visible = false
			_render_idle_message()


func _render_idle_message() -> void:
	var outcome := str(_status.get("outcome", "InProgress"))
	if outcome != "InProgress":
		_message_label.text = "Combat has ended (%s)." % outcome
		return

	var current_actor_id: Variant = _status.get("currentActorAnimaId")
	if current_actor_id != null:
		var turn_order: Array = _status.get("turnOrder", [])
		var turn_index := int(_status.get("turnIndex", -1))
		var actor_name := ""
		if turn_index >= 0 and turn_index < turn_order.size() and turn_order[turn_index] is Dictionary:
			actor_name = str((turn_order[turn_index] as Dictionary).get("name", ""))
		_message_label.text = "Round %d -- %s's turn." % [int(_status.get("roundNumber", 1)), actor_name] if actor_name != "" else "Round %d in progress." % int(_status.get("roundNumber", 1))
	else:
		_message_label.text = "Round %d -- the enemy is acting." % int(_status.get("roundNumber", 1))


## Confirm prompt's "on [target]" clause. Empty _phase_chosen_target means a true AoE skill
## (AllEnemies/AllAllies -- GetLegalTargets genuinely returned []), which has no single target to name.
func _confirm_target_clause() -> String:
	if _phase_chosen_target.is_empty():
		match _phase_target_type:
			"AllEnemies": return " on all enemies"
			"AllAllies": return " on your whole team"
			_: return ""
	var side := str(_phase_chosen_target.get("side", ""))
	var index := int(_phase_chosen_target.get("index", -1))
	var target_name := _summary_name_for(side, index)
	return " on %s" % target_name if target_name != "" else ""


func _summary_name_for(side: String, index: int) -> String:
	var list: Array = _status.get("playerTeam", []) if side == "Player" else _status.get("enemyTeam", [])
	if index >= 0 and index < list.size() and list[index] is Dictionary:
		return str((list[index] as Dictionary).get("name", ""))
	return ""


# ---- Turn order queue ----

func _render_turn_queue() -> void:
	for child in _turn_queue_list.get_children():
		child.free()

	var turn_order: Array = _status.get("turnOrder", [])
	var turn_index := int(_status.get("turnIndex", -1))

	for i in range(turn_order.size()):
		var entry: Variant = turn_order[i]
		if not (entry is Dictionary): continue
		_turn_queue_list.add_child(_build_turn_entry_row(entry, i == turn_index))


func _build_turn_entry_row(entry: Dictionary, is_current: bool) -> Control:
	var side := str(entry.get("side", ""))
	var is_player_side := side == "Player"

	var row := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0, 0, 0, 0.35 if is_current else 0.0)
	style.set_corner_radius_all(4)
	style.set_content_margin_all(4)
	style.border_width_left = 3
	style.border_color = Color(COLOR_ACCENT_AMBER) if is_player_side else Color(COLOR_ENEMY_ACCENT)
	row.add_theme_stylebox_override("panel", style)

	var label := Label.new()
	label.text = str(entry.get("name", "?"))
	label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM) if is_current else Color(COLOR_TEXT_MUTED))
	row.add_child(label)

	return row


# ---- Energy / Deck / Discard / Pass ----

func _render_status_row() -> void:
	var energy := int(_status.get("sharedEnergy", 0))
	_energy_label.text = "Energy %d" % energy

	for child in _energy_pips_row.get_children():
		child.free()
	for i in range(MAX_ENERGY):
		var pip := PanelContainer.new()
		pip.custom_minimum_size = Vector2(10, 10)
		var pip_style := StyleBoxFlat.new()
		pip_style.bg_color = Color(COLOR_ACCENT_AMBER) if i < energy else Color(0, 0, 0, 0.35)
		pip_style.set_corner_radius_all(5)
		pip.add_theme_stylebox_override("panel", pip_style)
		_energy_pips_row.add_child(pip)

	var draw_count := int(_status.get("drawPileCount", 0))
	var discard_count := int(_status.get("discardPileCount", 0))
	_deck_discard_label.text = "Deck %d / Discard %d" % [draw_count, discard_count]

	# Real (Phase 2, see top-of-file comment) -- only enabled while it's actually a player Anima's
	# turn (CurrentActorAnimaId non-null), no request is already in flight, and no targeting/confirm
	# action is already mid-flow (PHASE 3 -- Pass mid-action would be a nonsensical double-decision).
	var current_actor_id: Variant = _status.get("currentActorAnimaId")
	var outcome := str(_status.get("outcome", "InProgress"))
	_pass_button.disabled = _busy or current_actor_id == null or outcome != "InProgress" or _phase != PHASE_NONE


## Passes the current player Anima's turn for real -- SubmitAction with HandIndex/Target both null,
## per SubmitActionRequest's own documented "HandIndex null = Pass" convention. Added (Phase 2) so
## turn-gating verification (does the RIGHT Anima's hand light up as turns advance) had a real way to
## advance turns at all -- Phase 1 left Pass permanently disabled since nothing could exercise it yet.
## Real card play (see _on_hand_card_gui_input/_begin_targeting below) is now wired too, as of Phase 3.
func _on_pass_pressed() -> void:
	if _busy: return
	var current_actor_id: Variant = _status.get("currentActorAnimaId")
	if current_actor_id == null: return

	_busy = true
	_pass_button.disabled = true

	var before := _snapshot_combatants()
	var result: Variant = await _hub.invoke("SubmitAction", [{"animaId": current_actor_id, "handIndex": null, "target": null}])

	_busy = false
	if not (result is Dictionary):
		_message_label.text = "Could not submit Pass -- check your connection."
		_render_status_row()
		return

	_status = result
	_render_all()
	_play_hit_feedback(before)
	_check_for_match_result()


# ---- Hand ----

func _render_hand() -> void:
	for child in _hand_row.get_children():
		child.free()

	for card_data: Variant in _status.get("hand", []):
		if card_data is Dictionary:
			_hand_row.add_child(_build_hand_card(card_data))


## PHASE 2 (turn-gated hand + card ownership): HandCardSummary.OwnerAnimaId already existed on the
## wire before this pass (confirmed by re-reading GameHubModels.cs -- unlike the missing Description
## field, this one was already there, just never rendered), so no server change was needed to show
## it. Each card now carries a small owner chip (Aspect-color swatch + name) built from _team_by_id,
## matched by real AnimaId this time -- not the name-matching workaround the arena portraits need,
## since HandCardSummary gives a real, unambiguous OwnerAnimaId.
##
## Two DISTINCT dim treatments, so "not enough Energy" and "not this Anima's turn" never look the
## same: a not-current-Anima's card gets a flat, colorless dim (modulate.a 0.4, no overlay) -- the
## SAME visual family the dead-combatant arena cards already use, since both mean "nothing to do with
## this right now". An unaffordable-but-current-Anima's card gets a lighter dim (0.55) PLUS a red "X"
## overlay (locked design's own "dimmed with a red X overlay" language) -- readable as "closer to
## usable, just short on Energy this Round" rather than "not applicable at all". If a card is BOTH
## (impossible today since only the current actor's Energy is checked, but defensively) the turn-gate
## treatment wins, since "not your turn" is the more fundamental reason it can't be played.
##
## Only a fully-eligible (current Anima's + affordable, and no other action already mid-flow) card
## gets a real click handler -- as of Phase 3, that handler is real: see _begin_targeting below for
## the GetLegalTargets -> highlight/skip -> Confirm flow.
func _build_hand_card(card_data: Dictionary) -> Control:
	var color: String = str(card_data.get("color", ""))
	var tint: String = SKILL_COLOR_TINTS.get(color, "555555")

	var owner_id: String = str(card_data.get("ownerAnimaId", ""))
	var owner: Dictionary = _team_by_id.get(owner_id, {})
	var owner_name: String = str(owner.get("name", "?"))
	var owner_color: String = str(owner.get("color", ""))

	var current_actor_id: Variant = _status.get("currentActorAnimaId")
	var is_owner_turn := current_actor_id != null and str(current_actor_id) == owner_id
	var energy_cost := int(card_data.get("energyCost", 0))
	var affordable := energy_cost <= int(_status.get("sharedEnergy", 0))
	var hand_index := int(card_data.get("handIndex", -1))
	var phase_active := _phase != PHASE_NONE
	var is_selected_card := phase_active and hand_index == _phase_hand_index
	var is_eligible := is_owner_turn and affordable and not phase_active and not _busy

	var card := _make_static_panel()
	card.custom_minimum_size = Vector2(108, 0)
	card.mouse_filter = Control.MOUSE_FILTER_STOP

	# PHASE 3: the card currently mid-targeting/confirm gets its own highlighted border (same
	# amber-outline convention the arena's legal-target cards use) instead of the ordinary dim
	# treatments below, so it reads as "this is the one in progress," not "unavailable."
	if is_selected_card:
		var selected_style := StyleBoxFlat.new()
		selected_style.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
		selected_style.border_color = Color(COLOR_ACCENT_AMBER)
		selected_style.set_border_width_all(2)
		selected_style.set_corner_radius_all(8)
		selected_style.set_content_margin_all(8)
		card.add_theme_stylebox_override("panel", selected_style)
	elif phase_active:
		card.modulate = Color(1, 1, 1, 0.5)
	elif not is_owner_turn:
		card.modulate = Color(1, 1, 1, 0.4)
	elif not affordable:
		card.modulate = Color(1, 1, 1, 0.55)

	if is_eligible:
		card.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		card.gui_input.connect(_on_hand_card_gui_input.bind(card_data))
	else:
		card.mouse_default_cursor_shape = Control.CURSOR_ARROW

	var content := VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 4)
	card.add_child(content)

	# Owner chip -- small color swatch (Aspect color, matches the arena portrait tint convention) +
	# name, so "Lotus's Bristle" reads distinctly from "Warden's Bristle" at a glance.
	var owner_row := HBoxContainer.new()
	owner_row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	owner_row.add_theme_constant_override("separation", 4)
	content.add_child(owner_row)

	var owner_swatch := PanelContainer.new()
	owner_swatch.mouse_filter = Control.MOUSE_FILTER_IGNORE
	owner_swatch.custom_minimum_size = Vector2(10, 10)
	var owner_swatch_style := StyleBoxFlat.new()
	owner_swatch_style.bg_color = Color(SKILL_COLOR_TINTS.get(owner_color, "888888"))
	owner_swatch_style.set_corner_radius_all(5)
	owner_swatch.add_theme_stylebox_override("panel", owner_swatch_style)
	owner_row.add_child(owner_swatch)

	var owner_label := Label.new()
	owner_label.text = owner_name
	owner_label.autowrap_mode = TextServer.AUTOWRAP_OFF
	owner_label.clip_text = true
	owner_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	owner_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	owner_label.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
	owner_row.add_child(owner_label)

	var pip_row := HBoxContainer.new()
	pip_row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(pip_row)

	var pip := PanelContainer.new()
	pip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	pip.custom_minimum_size = Vector2(22, 22)
	var pip_style := StyleBoxFlat.new()
	pip_style.bg_color = Color(tint)
	pip_style.set_corner_radius_all(11)
	pip.add_theme_stylebox_override("panel", pip_style)
	var pip_center := CenterContainer.new()
	pip_center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	pip.add_child(pip_center)
	var pip_label := Label.new()
	pip_label.text = str(int(card_data.get("energyCost", 0)))
	pip_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	pip_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	pip_center.add_child(pip_label)
	pip_row.add_child(pip)

	# Placeholder art block -- no real per-skill card art exists yet (deferred alongside creature
	# portraits, per CLAUDE.md's own art-direction note); a flat color-tinted block stands in.
	# REAL BUG FOUND AND FIXED (Phase 5 live verification): this ColorRect had no explicit
	# mouse_filter, so it inherited Control's own default of MOUSE_FILTER_STOP -- since it's the
	# single largest visual region of the card (48px tall, full width), it silently swallowed
	# every click that landed on it, before the event could ever reach `card`'s own gui_input
	# handler below. Confirmed live: clicking the art block never fired GetLegalTargets; clicking
	# the skill-name text just below it (Label's own default IS MOUSE_FILTER_IGNORE) did. Same fix
	# applied to `pip`/`owner_swatch` above (both PanelContainers, same STOP-by-default issue, same
	# "biggest non-interactive shape swallows the click" risk, just smaller targets).
	var art_block := ColorRect.new()
	art_block.mouse_filter = Control.MOUSE_FILTER_IGNORE
	art_block.color = Color(tint, 0.5)
	art_block.custom_minimum_size = Vector2(0, 48)
	content.add_child(art_block)

	var name_label := Label.new()
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_label.text = str(card_data.get("skillName", "?"))
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	name_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	content.add_child(name_label)

	# PHASE 5: real mechanical description, from the server's own BuildSkillDescription helper
	# (HandCardSummary.Description, closes TODO #23(b) -- see top-of-file comment).
	var description: String = str(card_data.get("description", ""))
	var effect_label := Label.new()
	effect_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	effect_label.text = description
	effect_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	effect_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	effect_label.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
	content.add_child(effect_label)

	# PHASE 5: hover shows the full description in the docked message area (same pattern
	# reforge_node.gd's skill-browse hover already established), reverting to whatever the message
	# area was previously showing on mouse-leave -- _render_message_bar() already recomputes the
	# correct idle/targeting/confirm text from current state, so "revert" is just "re-run it."
	# Wired on the whole card (not gated on is_eligible) so a dimmed/unaffordable card's effect text
	# is still discoverable on hover.
	if description != "":
		card.mouse_entered.connect(func(): _message_label.text = description)
		card.mouse_exited.connect(func(): _render_message_bar())

	# Red "X" overlay -- ONLY for the "not enough Energy, but IS this Anima's turn" state, per this
	# function's own top comment on why the two dim states must read as visually distinct. Added as a
	# second full-rect child of `card` (a PanelContainer, same overlay-stacking technique ArenaPanel/
	# ArenaVignette already use) so it draws on top of `content` without needing a separate wrapper.
	if is_owner_turn and not affordable:
		var overlay := CenterContainer.new()
		overlay.mouse_filter = Control.MOUSE_FILTER_IGNORE
		card.add_child(overlay)

		var x_label := Label.new()
		x_label.text = "✕"
		x_label.add_theme_color_override("font_color", Color(COLOR_ERROR, 0.85))
		x_label.add_theme_font_size_override("font_size", 40)
		overlay.add_child(x_label)

	return card


func _on_hand_card_gui_input(event: InputEvent, card_data: Dictionary) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_begin_targeting(card_data)


## PHASE 3: real targeting entry point. Calls GetLegalTargets for this card, then either jumps
## straight to Confirm (SelfTarget, or a true AoE with an empty legal-target list) or enters
## PHASE_CHOOSING_TARGET so the arena can highlight the real legal set. See top-of-file comment for
## the full audit trail behind these branches (esp. why "list has exactly 1 entry" is NOT the right
## self-target check -- TargetType == "SelfTarget" is).
func _begin_targeting(card_data: Dictionary) -> void:
	if _busy or _phase != PHASE_NONE:
		return
	var current_actor_id: Variant = _status.get("currentActorAnimaId")
	if current_actor_id == null:
		return

	var hand_index := int(card_data.get("handIndex", -1))

	_busy = true
	# Deferred, NOT called directly: this handler is running INSIDE the clicked card's own
	# gui_input emission. _render_hand() frees and rebuilds every hand card, including the one
	# currently emitting this very signal -- freeing it synchronously throws Godot's "Object is
	# locked and can't be freed" (confirmed live: this crashed on every click before deferring,
	# and silently corrupted later state -- e.g. a stale/duplicate card left in the tree caused a
	# wrong target to be submitted on a later Confirm). call_deferred runs after this signal
	# emission's call stack fully unwinds, once the node is no longer locked.
	call_deferred("_render_hand")
	call_deferred("_render_status_row")

	var targets: Variant = await _hub.invoke("GetLegalTargets", [hand_index])

	_busy = false

	if not (targets is Array):
		_message_label.text = "Could not check targets -- check your connection."
		_render_hand()
		_render_status_row()
		return

	_phase_hand_index = hand_index
	_phase_anima_id = str(current_actor_id)
	_phase_skill_name = str(card_data.get("skillName", "?"))
	_phase_target_type = str(card_data.get("targetType", ""))
	_phase_legal_targets = targets

	if targets.is_empty():
		# True AoE (AllEnemies/AllAllies) -- no explicit target exists to pick.
		_phase_chosen_target = {}
		_phase = PHASE_CONFIRMING
	elif _phase_target_type == "SelfTarget":
		# GetLegalTargets returns exactly [actor] here -- use it directly as the implicit target.
		# Per this file's own audit note, this must still be SENT explicitly on Confirm, never null.
		_phase_chosen_target = targets[0]
		_phase = PHASE_CONFIRMING
	else:
		_phase = PHASE_CHOOSING_TARGET

	_refresh_interactive_ui()


## PHASE 3: clicking a highlighted (legal) arena target during PHASE_CHOOSING_TARGET moves straight
## to the Confirm prompt. Bound at card-build time with this card's own (side, index) -- see
## _build_combatant_card's legal-target wrap.
func _on_arena_card_gui_input(event: InputEvent, side: String, index: int) -> void:
	if _phase != PHASE_CHOOSING_TARGET:
		return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		if not _is_legal_target(side, index):
			return
		_phase_chosen_target = {"side": side, "index": index}
		_phase = PHASE_CONFIRMING
		# Deferred, same reason as _begin_targeting's call above: this handler is running inside
		# the clicked (legal-target-wrap) node's own gui_input emission, and _refresh_interactive_ui
		# -> _render_arena frees and rebuilds every arena card, including this one.
		call_deferred("_refresh_interactive_ui")


## Cancel fully clears the in-progress targeting/confirm selection -- shown during BOTH
## PHASE_CHOOSING_TARGET and PHASE_CONFIRMING (a small, deliberate addition beyond the locked design's
## literal "Confirm/Cancel" wording, which only describes the confirm step -- without it, a player who
## opened targeting on the wrong card would have no way back except finishing some other action).
func _on_cancel_pressed() -> void:
	if _busy:
		return
	_reset_phase()
	_refresh_interactive_ui()


## Confirm submits the real action: SubmitAction with the real HandIndex/Target, then re-renders the
## whole screen from the returned CombatStatus -- same re-render path _on_pass_pressed already uses.
## No hit-feedback animation yet (Phase 4).
func _on_confirm_pressed() -> void:
	if _busy or _phase != PHASE_CONFIRMING:
		return

	_busy = true
	_confirm_button.disabled = true
	_cancel_button.disabled = true

	var target_payload: Variant = null
	if not _phase_chosen_target.is_empty():
		target_payload = {
			"side": str(_phase_chosen_target.get("side", "")),
			"index": int(_phase_chosen_target.get("index", -1)),
		}

	var request := {"animaId": _phase_anima_id, "handIndex": _phase_hand_index, "target": target_payload}
	var before := _snapshot_combatants()
	var result: Variant = await _hub.invoke("SubmitAction", [request])

	_busy = false
	_reset_phase()

	if not (result is Dictionary):
		_refresh_interactive_ui()
		_message_label.text = "Could not submit action -- check your connection."
		return

	_status = result
	_render_all()
	_play_hit_feedback(before)
	_check_for_match_result()


func _reset_phase() -> void:
	_phase = PHASE_NONE
	_phase_hand_index = -1
	_phase_anima_id = ""
	_phase_skill_name = ""
	_phase_target_type = ""
	_phase_legal_targets = []
	_phase_chosen_target = {}


# ---- PHASE 5: Match Result (Victory/Defeat) -- see match_result.gd's own top-of-file comment for
# the full shared-component contract. Called after every SubmitAction/Pass response re-render;
# CombatStatus.Outcome is "InProgress" for every non-terminal response (StartCombat/GetCombatState/
# GetLegalTargets never carry a terminal outcome at all, confirmed by reading GameHub.cs), so this
# is a no-op except on the exact call that actually ends the fight.

func _check_for_match_result() -> void:
	var outcome := str(_status.get("outcome", "InProgress"))
	if outcome == "InProgress":
		return

	var mode := ""
	var params := {}

	if outcome == "Victory":
		var reward: Dictionary = _status.get("victoryReward", {})
		if _node_type == "Boss":
			mode = "victory_boss"
			var boss_name := ""
			var enemy_team: Array = _status.get("enemyTeam", [])
			if enemy_team.size() > 0 and enemy_team[0] is Dictionary:
				boss_name = str((enemy_team[0] as Dictionary).get("name", ""))
			params = {
				"wispGranted": int(reward.get("wispGranted", 0)),
				"echoShardGranted": bool(reward.get("echoShardGranted", false)),
				"pendingEmberColors": reward.get("pendingEmberColors", []),
				"bossHatchPreview": _status.get("bossHatchPreview", {}),
				"playerTeam": _status.get("playerTeam", []),
				"bossName": boss_name,
			}
		else:
			mode = "victory_elite" if _node_type == "Elite" else "victory_combat"
			params = {
				"wispGranted": int(reward.get("wispGranted", 0)),
				"vesselShardGranted": bool(reward.get("vesselShardGranted", false)),
				"pendingEmberColors": reward.get("pendingEmberColors", []),
			}
	elif outcome == "Defeat":
		mode = "defeat"
		params = _status.get("defeatSummary", {})
	else:
		return # defensive -- CombatOutcome is only ever InProgress/Victory/Defeat

	_open_match_result(mode, params)


func _open_match_result(mode: String, params: Dictionary) -> void:
	for child in _match_result_overlay.get_children():
		child.free()

	var instance := MATCH_RESULT_SCENE.instantiate() as Control
	_match_result_overlay.add_child(instance)
	_match_result_overlay.visible = true
	instance.connect("continue_to_map", _on_match_result_continue.bind(instance))
	instance.connect("return_to_hub", _on_match_result_return_to_hub.bind(instance))
	instance.call("start", _hub, mode, params)


## Combat/Elite Victory only -- the node is already cleared server-side (SubmitAction's own
## MarkCurrentNodeCleared, called before the reward grant), so closing this panel via the existing
## `resolved` signal and letting delve.gd refresh the map/ledger is the correct, complete "return"
## -- no separate signal needed for this case.
func _on_match_result_continue(instance: Control) -> void:
	_match_result_overlay.visible = false
	instance.queue_free()
	resolved.emit()


## Boss Victory (once its Delve Complete summary is dismissed) / Defeat -- the Delve itself has
## already ended server-side (Session.ActiveDelveRun cleared), so delve.gd needs to navigate away
## from the map entirely rather than just refresh it.
func _on_match_result_return_to_hub(instance: Control) -> void:
	_match_result_overlay.visible = false
	instance.queue_free()
	delve_ended.emit()


# ---- Combat log ----

func _render_combat_log() -> void:
	for child in _combat_log_list.get_children():
		child.free()

	var log_lines: Array = _status.get("eventLog", [])
	if log_lines.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No events logged yet."
		empty_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		empty_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
		_combat_log_list.add_child(empty_label)
		return

	for line: Variant in log_lines:
		var line_label := Label.new()
		line_label.text = str(line)
		line_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		line_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		line_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
		_combat_log_list.add_child(line_label)


# ---- DEV back button ----

func _on_dev_back_pressed() -> void:
	resolved.emit()


# ---- Shared small-widget builder (same pattern as reforge_node.gd/shop_node.gd's own copies) ----

func _make_static_panel() -> PanelContainer:
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	style.set_content_margin_all(8)
	panel.add_theme_stylebox_override("panel", style)
	return panel


# ---- Theme ----

func _apply_static_theme() -> void:
	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(Color(COLOR_CARD_BG), 0.85)
	card_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(12)
	_card.add_theme_stylebox_override("panel", card_style)

	_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
	_status_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)

	_round_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_round_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SUBHEADER)
	_wisp_icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	_wisp_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_wisp_label.add_theme_font_size_override("font_size", UiTheme.SIZE_LABEL)

	_message_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	_message_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)

	_energy_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_energy_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)
	_deck_discard_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_deck_discard_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)

	_pass_button.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_pass_button.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)

	_dev_back_button.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_dev_back_button.add_theme_color_override("font_color_hover", Color(COLOR_TEXT_CREAM))
	_dev_back_button.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)


## Background gradient + arena inset per node type -- the first real Godot implementation of
## CLAUDE.md's locked Combat/Elite/Boss room palettes (Combat had no client screen before this pass).
func _apply_background_tier(node_type: String) -> void:
	var stops := NORMAL_GRADIENT
	var arena_color := NORMAL_ARENA_INSET
	var show_vignette := false

	match node_type:
		"Elite":
			stops = ELITE_GRADIENT
			arena_color = ELITE_ARENA_INSET
		"Boss":
			stops = BOSS_GRADIENT
			arena_color = BOSS_ARENA_INSET
			show_vignette = true
		_:
			pass

	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color(stops[0]), Color(stops[1]), Color(stops[2])])
	gradient.offsets = PackedFloat32Array([0.0, 0.5, 1.0])
	var gradient_texture := GradientTexture2D.new()
	gradient_texture.gradient = gradient
	gradient_texture.fill = GradientTexture2D.FILL_RADIAL
	gradient_texture.width = 512
	gradient_texture.height = 512
	gradient_texture.fill_from = Vector2(0.3, 0.2)
	gradient_texture.fill_to = Vector2(1.0, 0.2)
	_background.texture = gradient_texture

	var arena_style := StyleBoxFlat.new()
	arena_style.bg_color = arena_color
	arena_style.set_corner_radius_all(10)
	_arena_panel.add_theme_stylebox_override("panel", arena_style)

	_arena_vignette.visible = show_vignette
	if show_vignette:
		var vignette_gradient := Gradient.new()
		vignette_gradient.colors = PackedColorArray([Color(BOSS_VIGNETTE_COLOR.r, BOSS_VIGNETTE_COLOR.g, BOSS_VIGNETTE_COLOR.b, 0.0), BOSS_VIGNETTE_COLOR])
		var vignette_texture := GradientTexture2D.new()
		vignette_texture.gradient = vignette_gradient
		vignette_texture.fill = GradientTexture2D.FILL_RADIAL
		vignette_texture.width = 512
		vignette_texture.height = 512
		vignette_texture.fill_from = Vector2(0.5, 0.5)
		vignette_texture.fill_to = Vector2(1.0, 0.5)
		_arena_vignette.texture = vignette_texture
