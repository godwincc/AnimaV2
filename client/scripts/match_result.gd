extends Control

## The shared Match Result component (Combat Phase 5) -- CLAUDE.md's "Match Result & Retreat
## System" calls for "one shared component, gated by isBoss," not three/four separate scenes. This
## is that component: a single self-contained Control (same child-panel/shared-connection
## architecture as augment_picker.gd -- see that file's own top-of-file comment for the pattern)
## instanced by TWO different callers:
## - combat.gd, into a new MatchResultOverlay, whenever CombatStatus.Outcome goes terminal
##   (Victory/Defeat) after a SubmitAction/Pass response -- modes "victory_combat"/"victory_elite"/
##   "victory_boss"/"defeat".
## - delve.gd, into its existing NodeEncounterOverlay, after a real RetreatFromDelve() call --
##   mode "retreat", reusing the exact same "summary" step Defeat uses (same DelveEndSummary wire
##   shape, per CLAUDE.md's own "Retreat... Result screen reuses Defeat's layout" instruction).
##
## OWNERSHIP CONTRACT: start(hub, mode, params) is the full entry point. This component takes NO
## Combat-specific or Delve-specific data beyond `params` (a plain Dictionary, shape depends on
## `mode` -- see each mode's own comment below) -- it makes exactly two RPC calls of its own
## (ConfirmBossHatch for the Boss-hatch naming step; nothing else), and otherwise only reads what
## the caller already handed it. It has NO opinion about what happens after the player is done --
## it emits `continue_to_map` (Combat/Elite Victory only -- caller tears this panel down and
## resumes the Delve map, node already cleared server-side) or `return_to_hub` (Boss Victory once
## the Delve Complete summary has been shown; Defeat; Retreat -- caller navigates to hub.tscn).
## Same "no opinion about what happens after Confirm" philosophy anima_reveal_panel.gd already
## established.
##
## MODES / PARAMS SHAPES (all sourced directly from real wire DTOs, confirmed via GameHubModels.cs):
## - "victory_combat" / "victory_elite": {wispGranted:int, vesselShardGranted:bool,
##   pendingEmberColors:Array[String]} -- mirrors CombatVictoryReward minus EchoShardGranted
##   (always false for a non-Boss Victory, per that record's own comment).
## - "victory_boss": {wispGranted:int, echoShardGranted:bool, pendingEmberColors:Array[String],
##   bossHatchPreview:Dictionary (WeaveGenomePreview-shaped), playerTeam:Array[Dictionary]
##   (CombatantSummary-shaped, for the HP strip), bossName:String}.
## - "defeat" / "retreat": a DelveEndSummary-shaped Dictionary directly (wispEarnedThisRun,
##   wispKept, wispForfeited, floorIndexReached, nodesCleared) -- passed as-is, no wrapping, since
##   both SubmitAction's DefeatSummary field and RetreatFromDelve's return value already are one.
##
## EMBER SEQUENCE: pendingEmberColors is "the account's FULL current queue" (per CombatVictoryReward
## own comment) at the moment of grant -- a FIFO. This component resolves it locally, one color at a
## time via the shared augment_picker.tscn (exact same (hub, ember_color) signature Shop's own Ember
## purchase flow already uses, confirmed directly reusable with zero changes), advancing its own
## local index on each `resolved`. No extra RPC is needed to know "is there another one queued" --
## augment_picker.gd/ConvertPendingEmberToWisp/AugmentPendingEmber always resolve the FRONT of the
## real server-side queue first (confirmed via GameHub.cs), so replaying this same captured
## front-to-back list locally stays correct as long as nothing else concurrently queues/resolves an
## Ember mid-sequence -- true here (single player, one modal flow at a time).

signal continue_to_map
signal return_to_hub

# Standard warm sanctuary/workshop palette -- Match Result has no locked palette of its own (same
# "no distinct palette" precedent Reforge/Augment's own visual identity already set).
const COLOR_GRADIENT_TOP := "4a3a2e"
const COLOR_GRADIENT_MID := "2b2018"
const COLOR_GRADIENT_BOTTOM := "1a130e"
const COLOR_CARD_BG := "1e1610"
const COLOR_CARD_BORDER := "c9b89e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_TEXT_CREAM_DIM := "e8cf9a"
const COLOR_TEXT_MUTED := "a89680"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_ERROR := "e2554a"
const COLOR_SUCCESS := "7ac47a"
const COLOR_BUTTON_TEXT := "2b2413"
const COLOR_HP_GREEN := "6cb87c"
const COLOR_HP_AMBER := "e8b95a"
const COLOR_HP_RED := "e2554a"

const GRADIENT_BUTTON_A := "c9a82f"
const GRADIENT_BUTTON_B := "a3841c"

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")
const AUGMENT_PICKER_SCENE := preload("res://scenes/augment_picker.tscn")
const ANIMA_REVEAL_PANEL_SCENE := preload("res://scenes/anima_reveal_panel.tscn")

# Boss-hatch Anima is always Gen 1 -- not part of the wire shape, same reasoning
# anima_reveal_panel.gd's own top-of-file comment gives for why every caller supplies this locally.
const BOSS_HATCH_GEN := 1

@onready var _background: TextureRect = $Background
@onready var _card: PanelContainer = $CenterContainer/Card
@onready var _header_label: Label = $CenterContainer/Card/Margin/Content/HeaderLabel
@onready var _body: VBoxContainer = $CenterContainer/Card/Margin/Content/Body
@onready var _status_label: Label = $CenterContainer/Card/Margin/Content/StatusLabel
@onready var _button_slot: CenterContainer = $CenterContainer/Card/Margin/Content/ButtonSlot
@onready var _ember_overlay: Control = $EmberOverlay

var _hub: HubConnection
var _mode: String = ""
var _params: Dictionary = {}
var _pending_embers: Array = []
var _ember_index: int = 0
var _busy: bool = false

# Boss-only state carried between steps.
var _named_anima: Dictionary = {}
var _delve_complete: Dictionary = {}


func _ready() -> void:
	_apply_theme()


## Entry point -- see this file's own top-of-file comment for the full ownership contract.
func start(hub: HubConnection, mode: String, params: Dictionary) -> void:
	_hub = hub
	_mode = mode
	_params = params
	_pending_embers = (params.get("pendingEmberColors", []) as Array).duplicate()
	_ember_index = 0

	match mode:
		"defeat", "retreat":
			_render_summary_step()
		_:
			_render_reward_step()


# ---- Step: reward (Wisp/Shard grants + Ember sequence) -- Victory modes only ----

func _render_reward_step() -> void:
	_clear_body()
	_clear_button_slot()
	_status_label.text = ""

	match _mode:
		"victory_combat":
			_header_label.text = "Enemy Defeated"
		"victory_elite":
			_header_label.text = "Elite Defeated"
		"victory_boss":
			var boss_name: String = str(_params.get("bossName", ""))
			_header_label.text = "%s Defeated" % boss_name if boss_name != "" else "Boss Defeated"

	if _mode == "victory_boss":
		_build_team_hp_strip()

	_body.add_child(_build_reward_row("sparkle", "+%d Wisp" % int(_params.get("wispGranted", 0)), COLOR_ACCENT_AMBER))

	if _mode == "victory_elite" and bool(_params.get("vesselShardGranted", false)):
		_body.add_child(_build_reward_row("hexagon", "+1 Vessel Shard", COLOR_ACCENT_AMBER))

	if _mode == "victory_boss" and bool(_params.get("echoShardGranted", false)):
		_body.add_child(_build_reward_row("shard", "+1 Echo Shard", COLOR_ACCENT_AMBER))

	if not _pending_embers.is_empty():
		var ember_status := Label.new()
		ember_status.text = "Resolving Ember %d of %d..." % [_ember_index + 1, _pending_embers.size()]
		ember_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		ember_status.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		ember_status.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
		_body.add_child(ember_status)
		_open_next_ember()
	else:
		_on_all_embers_resolved()


func _build_reward_row(icon_kind: String, text: String, color: String) -> Control:
	var row := HBoxContainer.new()
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	row.add_theme_constant_override("separation", 8)

	var icon := Control.new()
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", icon_kind)
	icon.set("icon_color", Color(color))
	icon.custom_minimum_size = Vector2(18, 18)
	icon.set("icon_size", 18.0)
	row.add_child(icon)

	var label := Label.new()
	label.text = text
	label.add_theme_color_override("font_color", Color(color))
	label.add_theme_font_size_override("font_size", UiTheme.SIZE_LABEL)
	row.add_child(label)

	return row


## Boss Victory's post-combat team status strip -- same HP-bar color coding as combat.gd's own
## _hp_bar_color (green >50%, amber 25-50%, red <=25%), duplicated here rather than shared per this
## codebase's established per-file small-mapping convention. playerTeam is CombatantSummary-shaped
## (Name/CurrentHp/MaxHp), captured by the caller at the moment Victory resolved.
func _build_team_hp_strip() -> void:
	var strip := HBoxContainer.new()
	strip.alignment = BoxContainer.ALIGNMENT_CENTER
	strip.add_theme_constant_override("separation", 16)

	for entry: Variant in _params.get("playerTeam", []):
		if not (entry is Dictionary): continue
		var current_hp := int(entry.get("currentHp", 0))
		var max_hp: int = max(1, int(entry.get("maxHp", 1)))
		var ratio: float = float(current_hp) / float(max_hp)

		var col := VBoxContainer.new()
		col.custom_minimum_size = Vector2(90, 0)
		col.add_theme_constant_override("separation", 2)

		var name_label := Label.new()
		name_label.text = str(entry.get("name", "?"))
		name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
		name_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
		col.add_child(name_label)

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
		col.add_child(hp_bar)

		var hp_label := Label.new()
		hp_label.text = "%d/%d" % [current_hp, max_hp]
		hp_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		hp_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		hp_label.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
		col.add_child(hp_label)

		strip.add_child(col)

	_body.add_child(strip)


func _hp_bar_color(ratio: float) -> String:
	if ratio > 0.5: return COLOR_HP_GREEN
	if ratio > 0.25: return COLOR_HP_AMBER
	return COLOR_HP_RED


func _open_next_ember() -> void:
	for child in _ember_overlay.get_children():
		child.free()

	var color: String = str(_pending_embers[_ember_index])
	var instance := AUGMENT_PICKER_SCENE.instantiate() as Control
	_ember_overlay.add_child(instance)
	_ember_overlay.visible = true
	instance.connect("resolved", _on_ember_resolved.bind(instance))
	instance.call("start", _hub, color)


func _on_ember_resolved(instance: Control) -> void:
	_ember_overlay.visible = false
	instance.queue_free()

	_ember_index += 1
	if _ember_index < _pending_embers.size():
		var ember_status := Label.new()
		ember_status.text = "Resolving Ember %d of %d..." % [_ember_index + 1, _pending_embers.size()]
		# The old status label was freed along with the rest of _body's step-specific children only
		# on a full _render_reward_step() re-entry, which doesn't happen here (only the ember overlay
		# is torn down) -- so just open the next one directly, no re-render needed.
		_open_next_ember()
	else:
		_on_all_embers_resolved()


func _on_all_embers_resolved() -> void:
	if _mode == "victory_boss":
		_render_reveal_step()
	else:
		_clear_button_slot()
		_button_slot.add_child(_build_gradient_button("Continue", _on_continue_pressed))


func _on_continue_pressed() -> void:
	continue_to_map.emit()


# ---- Step: reveal (Boss Victory only -- the guaranteed hatched Anima's mandatory naming) ----

func _render_reveal_step() -> void:
	_clear_body()
	_clear_button_slot()
	_header_label.text = "A New Vessel Stirs"

	var panel := ANIMA_REVEAL_PANEL_SCENE.instantiate() as AnimaRevealPanel
	_body.add_child(panel)
	panel.confirmed.connect(_on_boss_hatch_confirmed.bind(panel))

	var genome: Dictionary = _params.get("bossHatchPreview", {})
	# default_name="" (no archetype to suggest, per AnimaRevealPanel's own comment -- a Boss-hatch
	# Anima has no archetype the way a Starter roll does), progress_text="" (no multi-slot sequence).
	panel.render(genome, BOSS_HATCH_GEN, "", "")


func _on_boss_hatch_confirmed(name: String, panel: AnimaRevealPanel) -> void:
	if _busy: return
	_busy = true
	panel.set_busy(true)

	var result: Variant = await _hub.invoke("ConfirmBossHatch", [{"name": name}])

	_busy = false
	panel.set_busy(false)

	if not (result is Dictionary):
		panel.show_error("Could not confirm this Anima -- check your connection and try again.")
		return

	_named_anima = result.get("anima", {})
	var complete: Variant = result.get("delveComplete")
	_delve_complete = complete if complete is Dictionary else {}
	_render_complete_step()


# ---- Step: complete (Boss Victory only -- the appended Delve Complete summary) ----

func _render_complete_step() -> void:
	_clear_body()
	_clear_button_slot()
	_header_label.text = "Delve Complete"

	var named_label := Label.new()
	named_label.text = "%s joins your Sanctum." % str(_named_anima.get("name", "?"))
	named_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	named_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	named_label.add_theme_color_override("font_color", Color(COLOR_SUCCESS))
	named_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)
	_body.add_child(named_label)

	if not _delve_complete.is_empty():
		var floor_display := int(_delve_complete.get("floorIndexReached", 0)) + 1
		var anima_names: Array = _delve_complete.get("animaUsedNames", [])
		var summary_label := Label.new()
		summary_label.text = "Floor reached: %d\nNodes cleared: %d\nTeam: %s\nTotal Wisp earned: %d" % [
			floor_display,
			int(_delve_complete.get("nodesCleared", 0)),
			", ".join(anima_names),
			int(_delve_complete.get("totalWispEarnedThisRun", 0)),
		]
		summary_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		summary_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		summary_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
		summary_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
		_body.add_child(summary_label)

	_button_slot.add_child(_build_gradient_button("Return to Hub", _on_return_to_hub_pressed))


# ---- Step: summary (Defeat / Retreat -- reuses the same DelveEndSummary-shaped params) ----

func _render_summary_step() -> void:
	_clear_body()
	_clear_button_slot()
	_status_label.text = ""

	_header_label.text = "Delve Ended" if _mode == "defeat" else "Delve Retreated"

	var floor_display := int(_params.get("floorIndexReached", 0)) + 1
	var wisp_line := Label.new()
	wisp_line.text = "Wisp earned this run: %d -> kept: %d" % [
		int(_params.get("wispEarnedThisRun", 0)), int(_params.get("wispKept", 0))]
	wisp_line.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	wisp_line.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	wisp_line.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)
	_body.add_child(wisp_line)

	var detail_line := Label.new()
	detail_line.text = "Floor reached: %d  |  Nodes cleared: %d" % [floor_display, int(_params.get("nodesCleared", 0))]
	detail_line.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	detail_line.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	detail_line.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	_body.add_child(detail_line)

	var note_line := Label.new()
	note_line.text = "Echo Shards and Vessel Shards are kept in full. Artifacts held this run are lost."
	note_line.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	note_line.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	note_line.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	note_line.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
	_body.add_child(note_line)

	_button_slot.add_child(_build_gradient_button("Return to Hub", _on_return_to_hub_pressed))


func _on_return_to_hub_pressed() -> void:
	return_to_hub.emit()


# ---- Shared helpers ----

func _clear_body() -> void:
	for child in _body.get_children():
		child.free()


func _clear_button_slot() -> void:
	for child in _button_slot.get_children():
		child.free()


## Same TextureRect+transparent-Button gradient technique as augment_picker.gd's own copy -- see
## resource_node.gd's own comment for why (StyleBoxFlat has no gradient fill in Godot 4).
func _build_gradient_button(label_text: String, on_pressed: Callable) -> Control:
	var wrapper := Control.new()
	wrapper.custom_minimum_size = Vector2(180, 44)

	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color(GRADIENT_BUTTON_A), Color(GRADIENT_BUTTON_B)])
	var tex := GradientTexture2D.new()
	tex.gradient = gradient
	tex.fill = GradientTexture2D.FILL_LINEAR
	tex.fill_from = Vector2(0.0, 0.0)
	tex.fill_to = Vector2(1.0, 1.0)
	tex.width = 180
	tex.height = 44

	var bg := TextureRect.new()
	bg.texture = tex
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	wrapper.add_child(bg)

	var button := Button.new()
	button.set_anchors_preset(Control.PRESET_FULL_RECT)
	button.text = label_text
	button.flat = true
	button.focus_mode = Control.FOCUS_NONE
	button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	button.add_theme_color_override("font_color", Color(COLOR_BUTTON_TEXT))
	button.add_theme_color_override("font_color_hover", Color(COLOR_BUTTON_TEXT))
	button.add_theme_color_override("font_color_pressed", Color(COLOR_BUTTON_TEXT))
	button.add_theme_stylebox_override("normal", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("hover", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("pressed", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	button.pressed.connect(on_pressed)
	wrapper.add_child(button)

	return wrapper


# ---- Theme ----

func _apply_theme() -> void:
	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color(COLOR_GRADIENT_TOP), Color(COLOR_GRADIENT_MID), Color(COLOR_GRADIENT_BOTTOM)])
	gradient.offsets = PackedFloat32Array([0.0, 0.45, 1.0])

	var gradient_texture := GradientTexture2D.new()
	gradient_texture.gradient = gradient
	gradient_texture.fill = GradientTexture2D.FILL_RADIAL
	gradient_texture.width = 512
	gradient_texture.height = 512
	gradient_texture.fill_from = Vector2(0.3, 0.2)
	gradient_texture.fill_to = Vector2(0.95, 1.0)
	_background.texture = gradient_texture

	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(Color(COLOR_CARD_BG), 0.9)
	card_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(12)
	_card.add_theme_stylebox_override("panel", card_style)

	_header_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_header_label.add_theme_font_size_override("font_size", UiTheme.SIZE_HEADER)
	_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
	_status_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)
