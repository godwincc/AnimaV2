extends Control

## The real Reforge node screen -- color-first browse-and-pick flow (CLAUDE.md's locked Reforge
## design): Aspect select -> skill browse -> target Anima picker -> Accept/Decline confirm.
##
## Same child-panel/shared-connection architecture as resource_node.gd/treasure_node.gd -- see
## resource_node.gd's own top-of-file comment for why (a real scene change would orphan the
## in-progress DelveRun). start(hub) receives delve.gd's already-open HubConnection via loose
## .call(), same as those two.
##
## UNLIKE Resource/Treasure, nothing resolves on load. Reforge is browse-then-commit: the player can
## look through multiple Aspects/skills/targets before ever spending anything, and only a SUCCESSFUL
## AcceptReforge or an explicit DeclineReforge is allowed to clear the node (GameHub.cs's own
## EnsureReforgeVisited/AcceptReforge comments confirm this server-side). There is also no
## parameterless "list available Aspects" RPC to call on start() -- GetReforgeBrowseOptions requires
## a Color, and the 4 Aspects (Crimson/Onyx/Verdant/Azure only, no hybrids -- confirmed by reading
## ReforgePartPool, which is built purely from PrimitiveRoster's 4-color archetypes) are a fixed,
## client-known set. So start() makes no RPC call at all and just renders the Aspect-select step;
## the first real Reforge RPC (GetReforgeBrowseOptions) -- and therefore the server's own
## OnNodeVisited/EnsureReforgeVisited side effect -- fires only once the player picks an Aspect.
##
## Cost is server-computed, not re-derived here: GetReforgeValidTargets already returns each valid
## target's real Wisp cost (Ember Core discount included, per ReforgeTargetOption's own comment), so
## the confirm step just displays that number rather than re-implementing the 40-same-color/
## 80-cross-color rule client-side. Likewise "already equipped" exclusion is server-computed
## (ReforgeValidTargetsResult.InvalidTargetAnimaIds) -- not a client-side check against the roster.
##
## KNOWN SIMPLIFICATION (flagged, not silently assumed): the "current skill" shown at the target
## picker / confirm step reads straight off GetRoster's static parts list, which is the Anima's real
## genome skill -- it does NOT reflect an earlier-this-Delve Reforge override that hasn't been
## persisted to the roster row yet (DelveRun.GetEffectiveSkill is what the server actually checks for
## no-op exclusion, per ReforgeService.IsNoOpForTarget). No GameHub endpoint currently exposes the
## effective/overridden skill directly. In practice this only matters if the player Reforges the same
## Anima's same Part twice in one Delve visit sequence, which the node-clears-on-Accept rule already
## makes impossible within a single node visit -- kept here as a documented edge case, not a live bug.

signal resolved

const ASPECT_COLORS := ["Crimson", "Onyx", "Verdant", "Azure"]
const PART_ORDER := ["Head", "Frame", "Tail"]

# Aspect-select grid: fixed column count, GridContainer wraps into additional rows on its own as
# entries grow -- deliberately NOT hardcoded to a 2x2 assumption. A 5th color ("Amber") is a real,
# documented-but-deferred possibility (Anima_Design_Doc.md's own Open Design Threads) -- adding it
# to ASPECT_COLORS alone would be enough, no layout code would need to change.
const ASPECT_GRID_COLUMNS := 2
const ASPECT_CARD_SIZE := Vector2(130, 96)

# Docked message-area pattern (CLAUDE.md's Combat Screen Design section, generalized to Delve's own
# sidebar/skill-chip hovers): a single line in normal document flow, never a floating popup --
# floating tooltips positioned near this panel's own edges (it's a compact side panel, same risk
# Combat's own top-right Artifacts row and Delve's sidebar already hit) overflow the viewport.
const IDLE_INFO_MESSAGE := "Hover a skill or Vessel for details."

# Standard warm sanctuary/workshop palette -- CLAUDE.md's Reforge visual identity is explicitly
# "no distinct Reforge palette", so this is the same gradient/card colors every other real screen
# (Hub/Delve/Sanctum) already uses, not a Resource/Treasure-style tinted variant.
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
const COLOR_BUTTON_TEXT := "2b2413"

const GRADIENT_BUTTON_A := "c9a82f"
const GRADIENT_BUTTON_B := "a3841c"

# Same per-screen-duplicated Aspect sprite/hybrid-fallback dicts as hub.gd/delve.gd's own team rows
# -- no shared asset-lookup module exists in this codebase, this is the established convention.
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

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")
const GLOW_CIRCLE_SCRIPT := preload("res://scripts/glow_circle.gd")

@onready var _background: TextureRect = $Background
@onready var _centerpiece_wrap: Control = $CenterContainer/Card/Margin/Content/CenterpieceWrap
@onready var _back_button: Button = $CenterContainer/Card/Margin/Content/HeaderRow/BackButton
@onready var _title_label: Label = $CenterContainer/Card/Margin/Content/HeaderRow/TitleLabel
@onready var _subtitle_label: Label = $CenterContainer/Card/Margin/Content/SubtitleLabel
@onready var _step_body: VBoxContainer = $CenterContainer/Card/Margin/Content/StepBody
@onready var _info_icon: Control = $CenterContainer/Card/Margin/Content/InfoBar/InfoMargin/InfoRow/InfoIcon
@onready var _info_label: Label = $CenterContainer/Card/Margin/Content/InfoBar/InfoMargin/InfoRow/InfoLabel
@onready var _status_label: Label = $CenterContainer/Card/Margin/Content/StatusLabel
@onready var _decline_button: Button = $CenterContainer/Card/Margin/Content/FooterRow/DeclineButton
@onready var _button_slot: Control = $CenterContainer/Card/Margin/Content/FooterRow/ButtonSlot

var _hub: HubConnection
var _busy: bool = false

var _step: String = "aspect" # "aspect" | "browse" | "target" | "confirm"
var _roster: Array = []
var _selected_color: String = ""
var _browse_options: Array = [] # Array[Dictionary] -- ReforgeSkillOption shape
var _selected_skill: Dictionary = {}
var _valid_target_costs: Dictionary = {} # animaId -> int Cost
var _invalid_target_ids: Array = []
var _selected_target_id: String = ""
var _selected_target_cost: int = 0
var _hovered_info_icon_kind: String = ""
var _hovered_info_text: String = ""


func _ready() -> void:
	_apply_theme()
	_back_button.pressed.connect(_on_back_pressed)
	_decline_button.pressed.connect(_on_decline_pressed)
	_update_info_bar()


## Called by delve.gd right after instancing this scene -- see this file's own top-of-file comment.
func start(hub: HubConnection) -> void:
	_hub = hub
	_enter_step("aspect")


# ---- Step navigation ----

func _enter_step(step: String) -> void:
	_step = step
	_status_label.text = ""
	_clear_hovered_info()
	_back_button.visible = step != "aspect"
	_centerpiece_wrap.visible = step == "aspect"

	match step:
		"aspect":
			_title_label.text = "Reforge"
			_subtitle_label.text = "Choose an Aspect to reshape a Vessel's Thread."
		"browse":
			_title_label.text = "%s Skills" % _selected_color
			_subtitle_label.text = "Pick a Head, Frame, or Tail skill to reforge onto a Vessel."
		"target":
			_title_label.text = "Choose a Target"
			_subtitle_label.text = "%s (%s) -- who receives it?" % [str(_selected_skill.get("skillName", "")), str(_selected_skill.get("part", ""))]
		"confirm":
			_title_label.text = "Confirm Reforge"
			_subtitle_label.text = "This overrides the Vessel's Thread for the rest of this Delve."

	_clear_button_slot()
	_render_step_body()


func _on_back_pressed() -> void:
	if _busy: return
	match _step:
		"browse": _enter_step("aspect")
		"target": _enter_step("browse")
		"confirm": _enter_step("target")


func _clear_step_body() -> void:
	for child in _step_body.get_children():
		child.free()


func _clear_button_slot() -> void:
	for child in _button_slot.get_children():
		child.free()


func _render_step_body() -> void:
	_clear_step_body()
	match _step:
		"aspect": _render_aspect_step()
		"browse": _render_browse_step()
		"target": _render_target_step()
		"confirm": _render_confirm_step()


# ---- Step 1: Aspect select ----

func _render_aspect_step() -> void:
	# Fixed column count -- GridContainer wraps into additional rows on its own as ASPECT_COLORS
	# grows (e.g. a future 5th "Amber" entry), no layout change needed here. Centered via a wrapping
	# CenterContainer so the grid doesn't stretch to the full card width when the column count
	# doesn't evenly fill it (matches delve.gd's own CenterContainer-around-GridContainer precedent).
	var center := CenterContainer.new()
	_step_body.add_child(center)

	var grid := GridContainer.new()
	grid.columns = ASPECT_GRID_COLUMNS
	grid.add_theme_constant_override("h_separation", 10)
	grid.add_theme_constant_override("v_separation", 10)
	center.add_child(grid)

	for color_name: String in ASPECT_COLORS:
		grid.add_child(_build_aspect_card(color_name))


func _build_aspect_card(color_name: String) -> Control:
	# Fixed width AND height (not just height) -- every card is identical regardless of label
	# length ("Crimson" vs "Onyx" vs "Verdant" vs "Azure" are different string widths), so columns
	# never end up visually uneven.
	var card := _make_clickable_panel(ASPECT_CARD_SIZE, _on_aspect_pressed.bind(color_name))

	var content := VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 4)
	_panel_inner(card).add_child(content)

	var portrait_wrap := AspectRatioContainer.new()
	portrait_wrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	portrait_wrap.ratio = 1.0
	portrait_wrap.custom_minimum_size = Vector2(0, 56)
	content.add_child(portrait_wrap)
	_add_portrait(portrait_wrap, color_name)

	var label := Label.new()
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.text = color_name
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	content.add_child(label)

	return card


func _on_aspect_pressed(color_name: String) -> void:
	if _busy: return
	_busy = true
	_status_label.text = ""

	var options: Variant = await _hub.invoke("GetReforgeBrowseOptions", [{"color": color_name}])

	_busy = false
	if not (options is Array):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not load skills for this Aspect -- check your connection."
		return

	_selected_color = color_name
	_browse_options = options
	# Deferred, not direct -- this handler runs inside the clicked aspect card's own gui_input
	# dispatch (same "Object is locked and can't be freed" pitfall delve.gd's own map-node click
	# handler already documents), and _enter_step frees every StepBody child including this card.
	call_deferred("_enter_step", "browse")


# ---- Step 2: skill browse ----

func _render_browse_step() -> void:
	for part_name: String in PART_ORDER:
		var part_options: Array = _browse_options.filter(func(o: Variant): return o is Dictionary and str(o.get("part", "")) == part_name)
		if part_options.is_empty(): continue

		var header := Label.new()
		header.text = part_name
		header.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		header.add_theme_font_size_override("font_size", 12)
		_step_body.add_child(header)

		for option: Dictionary in part_options:
			_step_body.add_child(_build_skill_row(option))


func _build_skill_row(option: Dictionary) -> Control:
	var row := _make_clickable_panel(Vector2(0, 34), _on_skill_pressed.bind(option))
	# Hover-only (not the full hover+tap treatment _wire_hover gives standalone info elements) --
	# this row already has its own gui_input handler above that treats a click as "select this
	# skill", so a redundant tap-for-info handler on the same control would just be dead weight.
	_wire_hover_only(row, _icon_kind_for_skill(str(option.get("category", "")), bool(option.get("grantsShield", false))), str(option.get("description", "")))

	var content := HBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 8)
	_panel_inner(row).add_child(content)

	var name_label := Label.new()
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_label.text = str(option.get("skillName", "?"))
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	content.add_child(name_label)

	var archetype_label := Label.new()
	archetype_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	archetype_label.text = str(option.get("archetypeName", "?"))
	archetype_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	archetype_label.add_theme_font_size_override("font_size", 11)
	content.add_child(archetype_label)

	return row


func _on_skill_pressed(option: Dictionary) -> void:
	if _busy: return
	_busy = true
	_status_label.text = ""

	var targets_result: Variant = await _hub.invoke("GetReforgeValidTargets", [{"skillName": str(option.get("skillName", ""))}])

	if _roster.is_empty():
		var roster: Variant = await _hub.invoke("GetRoster", [])
		_roster = roster if roster is Array else []

	_busy = false
	if not (targets_result is Dictionary):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not load valid targets -- check your connection."
		return

	_selected_skill = option
	_valid_target_costs.clear()
	for t: Variant in targets_result.get("validTargets", []):
		if t is Dictionary:
			_valid_target_costs[str(t.get("animaId", ""))] = int(t.get("cost", 0))
	_invalid_target_ids = targets_result.get("invalidTargetAnimaIds", [])

	call_deferred("_enter_step", "target")


# ---- Step 3: target picker ----

func _render_target_step() -> void:
	var team: Array = _roster.filter(func(a: Variant): return a is Dictionary and bool(a.get("inTeam", false)))

	if team.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No team on this Delve."
		empty_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		_step_body.add_child(empty_label)
		return

	var any_valid := false
	for a: Dictionary in team:
		var anima_id := str(a.get("id", ""))
		if _valid_target_costs.has(anima_id): any_valid = true
		_step_body.add_child(_build_target_card(a))

	if not any_valid:
		var note := Label.new()
		note.text = "Every Vessel already has this skill in that Part -- go Back and pick a different skill."
		note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		note.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		note.add_theme_font_size_override("font_size", 11)
		_step_body.add_child(note)


func _build_target_card(anima: Dictionary) -> Control:
	var anima_id := str(anima.get("id", ""))
	var valid := _valid_target_costs.has(anima_id)
	var color_name: String = str(anima.get("color", ""))

	var card: Control
	if valid:
		card = _make_clickable_panel(Vector2(0, 44), _on_target_pressed.bind(anima_id, int(_valid_target_costs[anima_id])))
	else:
		card = _make_static_panel(Vector2(0, 44))
		card.modulate.a = 0.45

	var content := HBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 8)
	_panel_inner(card).add_child(content)

	var portrait_wrap := AspectRatioContainer.new()
	portrait_wrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	portrait_wrap.ratio = 1.0
	portrait_wrap.custom_minimum_size = Vector2(32, 32)
	portrait_wrap.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	content.add_child(portrait_wrap)
	_add_portrait(portrait_wrap, color_name)

	var text_col := VBoxContainer.new()
	text_col.mouse_filter = Control.MOUSE_FILTER_IGNORE
	text_col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	text_col.add_theme_constant_override("separation", 2)
	content.add_child(text_col)

	var name_label := Label.new()
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_label.text = "%s (%s)" % [str(anima.get("name", "?")), color_name]
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	text_col.add_child(name_label)

	var current_part := _current_skill_part(anima)
	var current_label := Label.new()
	# PASS, not IGNORE -- this label sits inside an otherwise-clickable card (see the whole-card
	# gui_input handler above); PASS lets it receive its own hover signals for the info bar while
	# still letting a click on this exact spot fall through to the card's own "select this target"
	# handler, rather than swallowing it.
	current_label.mouse_filter = Control.MOUSE_FILTER_PASS
	current_label.text = "Current: %s" % str(current_part.get("skillName", "--"))
	current_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	current_label.add_theme_font_size_override("font_size", 11)
	text_col.add_child(current_label)
	_wire_hover(current_label, _icon_kind_for_skill(str(current_part.get("category", "")), bool(current_part.get("grantsShield", false))), str(current_part.get("description", "")))

	var trailing_label := Label.new()
	trailing_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	if valid:
		trailing_label.text = "%d Wisp" % int(_valid_target_costs[anima_id])
		trailing_label.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
	else:
		trailing_label.text = "Already equipped"
		trailing_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	trailing_label.add_theme_font_size_override("font_size", 11)
	content.add_child(trailing_label)

	return card


## Returns the full AnimaPartSummary dict (skillName/category/grantsShield/description) for
## whichever Part _selected_skill targets -- not just the name -- so callers can also show the
## currently-equipped skill's own icon+effect text, not only the incoming one's.
func _current_skill_part(anima: Dictionary) -> Dictionary:
	var part_name := str(_selected_skill.get("part", ""))
	for p: Variant in anima.get("parts", []):
		if p is Dictionary and str(p.get("part", "")) == part_name:
			return p
	return {}


func _on_target_pressed(anima_id: String, cost: int) -> void:
	if _busy: return
	_selected_target_id = anima_id
	_selected_target_cost = cost
	call_deferred("_enter_step", "confirm")


# ---- Step 4: confirm ----

func _render_confirm_step() -> void:
	var target: Dictionary = {}
	for a: Variant in _roster:
		if a is Dictionary and str(a.get("id", "")) == _selected_target_id:
			target = a
			break

	var name_label := Label.new()
	name_label.text = "%s (%s)" % [str(target.get("name", "?")), str(target.get("color", "?"))]
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	name_label.add_theme_font_size_override("font_size", 14)
	_step_body.add_child(name_label)

	var part_label := Label.new()
	part_label.text = "%s:" % str(_selected_skill.get("part", ""))
	part_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	part_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	part_label.add_theme_font_size_override("font_size", 11)
	_step_body.add_child(part_label)

	# Current -> New, each independently hoverable via the same docked info bar the browse/target
	# steps use -- lets the player compare what they're LOSING against what they're gaining before
	# confirming, not just preview the incoming skill (explicit requirement, not just symmetry).
	var change_row := HBoxContainer.new()
	change_row.alignment = BoxContainer.ALIGNMENT_CENTER
	change_row.add_theme_constant_override("separation", 6)
	_step_body.add_child(change_row)

	var current_part := _current_skill_part(target)
	change_row.add_child(_build_info_span(
		str(current_part.get("skillName", "--")),
		_icon_kind_for_skill(str(current_part.get("category", "")), bool(current_part.get("grantsShield", false))),
		str(current_part.get("description", ""))))

	var arrow_label := Label.new()
	arrow_label.text = "->"
	arrow_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	change_row.add_child(arrow_label)

	change_row.add_child(_build_info_span(
		str(_selected_skill.get("skillName", "?")),
		_icon_kind_for_skill(str(_selected_skill.get("category", "")), bool(_selected_skill.get("grantsShield", false))),
		str(_selected_skill.get("description", ""))))

	var cost_row := HBoxContainer.new()
	cost_row.alignment = BoxContainer.ALIGNMENT_CENTER
	cost_row.add_theme_constant_override("separation", 6)
	_step_body.add_child(cost_row)

	var cost_icon := Control.new()
	cost_icon.set_script(ICON_GLYPH_SCRIPT)
	cost_icon.set("icon_kind", "sparkle")
	cost_icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	cost_icon.set("icon_size", 15.0)
	cost_row.add_child(cost_icon)

	var cost_label := Label.new()
	cost_label.text = "%d Wisp" % _selected_target_cost
	cost_label.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
	cost_row.add_child(cost_label)

	_clear_button_slot()
	_button_slot.add_child(_build_gradient_button("Accept", GRADIENT_BUTTON_A, GRADIENT_BUTTON_B, COLOR_BUTTON_TEXT, _on_accept_pressed))


func _on_accept_pressed() -> void:
	if _busy: return
	_busy = true
	_status_label.text = ""

	var result: Variant = await _hub.invoke("AcceptReforge", [{"skillName": str(_selected_skill.get("skillName", "")), "animaId": _selected_target_id}])

	_busy = false
	if not (result is Dictionary):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not Reforge -- check your connection and try again."
		return

	var outcome := str(result.get("outcome", ""))
	if outcome == "Success":
		resolved.emit()
		return

	# InsufficientWisp: nothing was spent, node stays open (per GameHub.cs's own AcceptReforge
	# comment) -- show the real quoted cost/balance and let the player Back up or Decline.
	_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
	_status_label.text = "Not enough Wisp -- needed %d, have %d." % [int(result.get("cost", 0)), int(result.get("wispBalance", 0))]


# ---- Decline ----

func _on_decline_pressed() -> void:
	if _busy: return
	_busy = true
	_status_label.text = ""

	var ok := await _call_void("DeclineReforge", [])

	_busy = false
	if ok:
		resolved.emit()
	else:
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not leave the Reforge -- check your connection and try again."


## HubConnection.invoke() returns null both on a genuine void-Task success AND on failure (see
## HubConnection.gd's own doc comment) -- there's no other client precedent for a Task-returning-
## nothing RPC to copy (every other invoke() call in this codebase reads a Dictionary/Array result).
## Same temporary-listener technique hub.gd's/delve.gd's own _connect_hub already uses to distinguish
## "no result" from "a real error fired" via error_occurred.
func _call_void(method: String, args: Array = []) -> bool:
	var failed := false
	var on_error := func(_msg: String): failed = true
	_hub.error_occurred.connect(on_error)
	await _hub.invoke(method, args)
	if _hub.error_occurred.is_connected(on_error):
		_hub.error_occurred.disconnect(on_error)
	return not failed


# ---- Info bar (docked message-area hover pattern -- see this file's own IDLE_INFO_MESSAGE
# comment for why this isn't a floating tooltip) ----

func _update_info_bar() -> void:
	var has_info := _hovered_info_text != ""
	_info_icon.visible = has_info and _hovered_info_icon_kind != ""
	if _info_icon.visible:
		_info_icon.set("icon_kind", _hovered_info_icon_kind)
	_info_label.text = _hovered_info_text if has_info else IDLE_INFO_MESSAGE


func _set_hovered_info(icon_kind: String, text: String) -> void:
	_hovered_info_icon_kind = icon_kind
	_hovered_info_text = text
	_update_info_bar()


func _clear_hovered_info() -> void:
	_hovered_info_icon_kind = ""
	_hovered_info_text = ""
	_update_info_bar()


## Hover-only wiring for a control that already has its OWN gui_input/click handler (e.g. a
## clickable skill row) -- adding a redundant tap-for-info handler on top of that would just fire
## alongside the click's own action for no benefit, since touch users already get feedback from the
## click's own effect (selecting the row, advancing steps).
func _wire_hover_only(control: Control, icon_kind: String, text: String) -> void:
	control.mouse_entered.connect(func(): _set_hovered_info(icon_kind, text))
	control.mouse_exited.connect(func(): _clear_hovered_info())


## Full hover+tap wiring for a PURELY informational control with no click purpose of its own (e.g.
## the target picker's "Current: X" label, the confirm step's Current/New spans) -- same "tap
## mirrors hover, for touch users with no hover equivalent" pattern delve.gd's own skill
## chips/Artifact rows already use.
func _wire_hover(control: Control, icon_kind: String, text: String) -> void:
	_wire_hover_only(control, icon_kind, text)
	control.gui_input.connect(_on_info_source_gui_input.bind(icon_kind, text))


func _on_info_source_gui_input(event: InputEvent, icon_kind: String, text: String) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_set_hovered_info(icon_kind, text)


## Skill-type icon resolution -- CLAUDE.md's Skill-type icon set (sword=Attack/heart=Heal/
## shield=shield-granting Buff/bolt=other Buff), same convention delve.gd's own
## _icon_kind_for_part uses. No Crest branch needed here (unlike delve.gd's copy) -- Reforge only
## ever deals in Head/Frame/Tail skills, Crest is excluded entirely by design (see this file's own
## top-of-file comment / CLAUDE.md's Reforge section).
func _icon_kind_for_skill(category: String, grants_shield: bool) -> String:
	match category:
		"Attack": return "sword"
		"Heal": return "heart"
		"Buff": return "shield" if grants_shield else "bolt"
		_: return "bolt"


## A small hoverable text span with no click purpose of its own (the confirm step's Current/New
## skill names) -- PASS rather than STOP/IGNORE so it still receives its own hover signals without
## needing anything "behind" it to fall through to (unlike the target-picker's card-nested label,
## there's no outer clickable parent here, but PASS is harmless and keeps the convention uniform).
func _build_info_span(text: String, icon_kind: String, description: String) -> Control:
	var label := Label.new()
	label.text = text
	label.mouse_filter = Control.MOUSE_FILTER_PASS
	label.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	_wire_hover(label, icon_kind, description)
	return label


# ---- Shared small-widget builders ----

func _add_portrait(wrap: Control, color_name: String) -> void:
	if ASPECT_SPRITES.has(color_name):
		var portrait := TextureRect.new()
		portrait.mouse_filter = Control.MOUSE_FILTER_IGNORE
		portrait.texture = load(ASPECT_SPRITES[color_name])
		portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		portrait.stretch_mode = TextureRect.STRETCH_SCALE
		wrap.add_child(portrait)
	else:
		var fallback := ColorRect.new()
		fallback.mouse_filter = Control.MOUSE_FILTER_IGNORE
		fallback.color = Color(HYBRID_FALLBACK_TINTS.get(color_name, "555555"))
		wrap.add_child(fallback)


## A PanelContainer + MarginContainer pair with gui_input-based click handling (same "wrapper with
## mouse_filter=STOP + gui_input.connect" technique delve.gd's own _build_skill_chip/_build_artifact_row
## use for custom clickable rows) rather than a real Button -- lets the caller freely lay out
## portrait+text content inside without fighting a Button's own centered-text rendering.
func _make_clickable_panel(min_size: Vector2, on_pressed: Callable) -> PanelContainer:
	var panel := _make_static_panel(min_size)
	panel.mouse_filter = Control.MOUSE_FILTER_STOP
	panel.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	panel.gui_input.connect(func(event: InputEvent):
		if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
			on_pressed.call()
	)
	return panel


func _make_static_panel(min_size: Vector2) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = min_size

	var style := StyleBoxFlat.new()
	style.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	style.set_border_width_all(1)
	style.set_corner_radius_all(10)
	style.set_content_margin_all(8)
	panel.add_theme_stylebox_override("panel", style)

	return panel


## Returns the Control content should actually be added to (the panel itself, since
## _make_static_panel/_make_clickable_panel apply their own content margin via the StyleBox rather
## than a nested MarginContainer).
func _panel_inner(panel: PanelContainer) -> PanelContainer:
	return panel


## Same TextureRect+transparent-Button gradient technique as resource_node.gd's own copy -- see that
## file's own comment for why (StyleBoxFlat has no gradient fill in Godot 4).
func _build_gradient_button(label_text: String, color_a: String, color_b: String, text_color: String, on_pressed: Callable) -> Control:
	var wrapper := Control.new()
	wrapper.custom_minimum_size = Vector2(160, 44)

	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color(color_a), Color(color_b)])
	var tex := GradientTexture2D.new()
	tex.gradient = gradient
	tex.fill = GradientTexture2D.FILL_LINEAR
	tex.fill_from = Vector2(0.0, 0.0)
	tex.fill_to = Vector2(1.0, 1.0)
	tex.width = 160
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
	button.add_theme_color_override("font_color", Color(text_color))
	button.add_theme_color_override("font_color_hover", Color(text_color))
	button.add_theme_color_override("font_color_pressed", Color(text_color))
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
	card_style.bg_color = Color(Color(COLOR_CARD_BG), 0.85)
	card_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(12)
	$CenterContainer/Card.add_theme_stylebox_override("panel", card_style)

	_title_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_title_label.add_theme_font_size_override("font_size", 16)
	_subtitle_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_subtitle_label.add_theme_font_size_override("font_size", 12)
	_status_label.add_theme_font_size_override("font_size", 12)

	var info_style := StyleBoxFlat.new()
	info_style.bg_color = Color(Color(COLOR_CARD_BG), 0.85)
	info_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	info_style.set_border_width_all(1)
	info_style.set_corner_radius_all(10)
	$CenterContainer/Card/Margin/Content/InfoBar.add_theme_stylebox_override("panel", info_style)
	_info_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	_info_label.add_theme_font_size_override("font_size", 11)
	_info_icon.set("icon_color", Color(COLOR_ACCENT_AMBER))

	_decline_button.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_decline_button.add_theme_color_override("font_color_hover", Color(COLOR_TEXT_CREAM_DIM))

	var glow := Control.new()
	glow.set_script(GLOW_CIRCLE_SCRIPT)
	glow.set_anchors_preset(Control.PRESET_FULL_RECT)
	glow.set("glow_color", Color(COLOR_ACCENT_AMBER))
	glow.set("glow_radius", 55.0)
	_centerpiece_wrap.add_child(glow)

	var hammer := Control.new()
	hammer.set_script(ICON_GLYPH_SCRIPT)
	hammer.set("icon_kind", "hammer")
	hammer.set("icon_color", Color(COLOR_ACCENT_AMBER))
	hammer.set("icon_size", 52.0)
	hammer.position = Vector2(60, 60) - Vector2(26, 26)
	_centerpiece_wrap.add_child(hammer)
