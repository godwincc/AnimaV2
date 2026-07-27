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
## - CombatantSummary(Side, Index, Name, CurrentHp, MaxHp, Position, Alive, Statuses[keyword string]).
##   NO AnimaId field on this shape (confirmed) -- see PLAYER-SIDE SPRITE MAPPING below for the real
##   gap this creates and how this pass works around it.
## - HandCardSummary(HandIndex, OwnerAnimaId, SkillName, Category, Color, EnergyCost, TargetType).
##   NO description/effect-text field -- confirmed by reading GameHubModels.cs: unlike
##   AnimaPartSummary/ReforgeSkillOption (both of which carry a server-synthesized
##   BuildSkillDescription string), HandCardSummary was never given the same treatment. This pass
##   substitutes a plain "<Category> · <EnergyCost> Energy" caption from the fields that ARE present,
##   clearly a lesser substitute for real effect text -- flagged here for Phase 2 (add a Description
##   field to HandCardSummary, reusing the existing BuildSkillDescription helper, same as every other
##   skill-bearing DTO already does).
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
## OUT OF SCOPE THIS PASS (still Phase 3+): real card targeting (GetLegalTargets, the Confirm/Cancel
## flow, actually calling SubmitAction with a real HandIndex+Target), hit-feedback animations, hover
## tooltips on the message area, live combat-log streaming beyond whatever StartCombat's own initial
## response already carries, Enrage status-icon logic (statuses render as plain text chips for now,
## no per-keyword icon mapping).
##
## PHASE 2 additions (turn-gated hand, card ownership, Pass wiring): see the "PHASE 2" comment block
## right above _render_hand/_build_hand_card below for the full detail. Short version: hand cards now
## show which Anima they belong to (HandCardSummary.OwnerAnimaId, matched against the team roster --
## this field ALREADY existed on the wire, confirmed by re-reading GameHubModels.cs, so no server
## change was needed here, unlike the Description-field gap which still stands), only the current
## turn's Anima has clickable-ready cards (two distinct dim treatments: "not your turn" vs "not
## enough Energy"), and the Pass button is now real (calls SubmitAction with HandIndex=null) --
## added specifically so turn advancement could be verified live for this pass's own testing needs,
## not part of the original 3-item ask on its own, but a direct, minimal prerequisite for it.
## Actually PLAYING a card (real targeting + SubmitAction with a real HandIndex) is still not wired --
## an eligible card's click handler just surfaces a message-area placeholder, honestly labeled as
## such, rather than pretending a targeting flow exists.
##
## DEV-ONLY "Back to Map" button (bottom of screen, NOT part of the locked Combat Screen Design):
## this screen has no real way to leave Combat yet -- that's Phase 2's Victory/Defeat/Anima-Reveal
## flow (CLAUDE.md's Match Result & Retreat System), not built here. Without a temporary escape
## hatch, a tester could open this screen and have no way back to the map at all. Pressing it only
## tears down THIS client-side panel (emits `resolved`, same signal convention every other node
## screen uses) -- it does NOT call any server RPC, so the real ActiveCombat state is untouched
## server-side (GetCombatState's own resume support already covers reconnecting into it later).
## Strip this button out once Phase 2's real win/loss flow ships.

signal resolved

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

@onready var _background: TextureRect = $Background
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


func _ready() -> void:
	_apply_static_theme()
	_dev_back_button.pressed.connect(_on_dev_back_pressed)
	_pass_button.pressed.connect(_on_pass_pressed)


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
	_render_hud()
	_render_artifacts()
	_render_arena()
	_render_message()
	_render_turn_queue()
	_render_status_row()
	_render_hand()
	_render_combat_log()


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
			var is_current := current_side == "Player" and current_index == int(summary.get("index", -1))
			_player_column.add_child(_build_combatant_card(summary, true, is_current))

	var enemy_team: Array = (_status.get("enemyTeam", []) as Array).duplicate()
	enemy_team.sort_custom(func(a: Dictionary, b: Dictionary): return int(a.get("position", 0)) < int(b.get("position", 0)))
	for summary: Variant in enemy_team:
		if summary is Dictionary:
			var is_current := current_side == "Enemy" and current_index == int(summary.get("index", -1))
			_enemy_column.add_child(_build_combatant_card(summary, false, is_current))


func _build_combatant_card(summary: Dictionary, is_player_side: bool, is_current: bool) -> Control:
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

	return card


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

func _render_message() -> void:
	var outcome := str(_status.get("outcome", "InProgress"))
	if outcome != "InProgress":
		_message_label.text = "Combat has ended (%s)." % outcome
		return

	var current_actor_id: Variant = _status.get("currentActorAnimaId")
	if current_actor_id != null:
		var player_team: Array = _status.get("playerTeam", [])
		var turn_order: Array = _status.get("turnOrder", [])
		var turn_index := int(_status.get("turnIndex", -1))
		var actor_name := ""
		if turn_index >= 0 and turn_index < turn_order.size() and turn_order[turn_index] is Dictionary:
			actor_name = str((turn_order[turn_index] as Dictionary).get("name", ""))
		_message_label.text = "Round %d -- %s's turn." % [int(_status.get("roundNumber", 1)), actor_name] if actor_name != "" else "Round %d in progress." % int(_status.get("roundNumber", 1))
	else:
		_message_label.text = "Round %d -- the enemy is acting." % int(_status.get("roundNumber", 1))


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
	# turn (CurrentActorAnimaId non-null) and no request is already in flight.
	var current_actor_id: Variant = _status.get("currentActorAnimaId")
	var outcome := str(_status.get("outcome", "InProgress"))
	_pass_button.disabled = _busy or current_actor_id == null or outcome != "InProgress"


## Passes the current player Anima's turn for real -- SubmitAction with HandIndex/Target both null,
## per SubmitActionRequest's own documented "HandIndex null = Pass" convention. Added specifically so
## this pass's own turn-gating verification (does the RIGHT Anima's hand light up as turns advance)
## has a real way to advance turns at all -- Phase 1 left Pass permanently disabled since nothing
## could exercise it yet. Still deliberately NOT real card play: an eligible hand card's click handler
## (see _on_hand_card_gui_input below) only surfaces a placeholder message, never calls SubmitAction
## with a real HandIndex/Target -- that's real targeting, still Phase 3+.
func _on_pass_pressed() -> void:
	if _busy: return
	var current_actor_id: Variant = _status.get("currentActorAnimaId")
	if current_actor_id == null: return

	_busy = true
	_pass_button.disabled = true

	var result: Variant = await _hub.invoke("SubmitAction", [{"animaId": current_actor_id, "handIndex": null, "target": null}])

	_busy = false
	if not (result is Dictionary):
		_message_label.text = "Could not submit Pass -- check your connection."
		_render_status_row()
		return

	_status = result
	_render_all()


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
## Only a fully-eligible (current Anima's + affordable) card gets a real click handler -- and even
## then, it does NOT call SubmitAction with a real HandIndex/Target (that's real targeting, still
## Phase 3+, see top-of-file comment): clicking just surfaces an honest placeholder in the message
## area rather than silently doing nothing OR pretending to submit an action that isn't implemented.
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
	var is_eligible := is_owner_turn and affordable

	var card := _make_static_panel()
	card.custom_minimum_size = Vector2(108, 0)
	card.mouse_filter = Control.MOUSE_FILTER_STOP

	if not is_owner_turn:
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
	var art_block := ColorRect.new()
	art_block.color = Color(tint, 0.5)
	art_block.custom_minimum_size = Vector2(0, 48)
	content.add_child(art_block)

	var name_label := Label.new()
	name_label.text = str(card_data.get("skillName", "?"))
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	name_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	content.add_child(name_label)

	# Substitute mechanical caption, NOT real effect text -- see top-of-file comment: HandCardSummary
	# carries no description field the way AnimaPartSummary/ReforgeSkillOption do.
	var effect_label := Label.new()
	effect_label.text = "%s · %d Energy" % [str(card_data.get("category", "")), int(card_data.get("energyCost", 0))]
	effect_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	effect_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	effect_label.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
	content.add_child(effect_label)

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
		_message_label.text = "Would play %s -- targeting/confirm not implemented yet (Phase 3)." % str(card_data.get("skillName", "?"))


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
