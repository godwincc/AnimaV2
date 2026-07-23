extends Control

## The real Anima Profile screen (new -- reached by clicking a card on Sanctum, replacing the
## "not built yet" status message that used to show). Layout, top to bottom: portrait+name (with
## inline rename + an "In team" badge if applicable), Color/Gen/Weave line, "Threads" section
## (Dominant per part, dot-accent colored by skill type, with a "Show hidden" toggle for R1/R2),
## "Lineage" section (Parent A/Parent B/Siblings/Echo Twin, each a clickable link back into this
## same scene for a different animaId), and a simplified "Delve History" section (lifetime
## Completed/Failed counters + a compact last-5 list showing only outcome and date -- see
## CLAUDE.md's Anima Profile screen-4 entry, updated this session to match).
##
## Which Anima to show is passed via the NavState autoload (selected_anima_id) rather than a scene
## parameter -- Godot's change_scene_to_file() has no way to pass one directly, and NavState is a
## tiny in-memory singleton for exactly this, same shape as AuthState.

const SERVER_WS_URL := "ws://localhost:5143/hubs/game"
const LOGIN_SCENE := "res://scenes/login.tscn"
const SANCTUM_SCENE := "res://scenes/sanctum.tscn"
const PROFILE_SCENE := "res://scenes/anima_profile.tscn"

# Warm sanctuary/workshop theme + card styling, matching sanctum.gd/hub.gd's own palette exactly.
const COLOR_GRADIENT_TOP := "4a3a2e"
const COLOR_GRADIENT_MID := "2b2018"
const COLOR_GRADIENT_BOTTOM := "1a130e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_TEXT_CREAM_DIM := "e8cf9a"
const COLOR_TEXT_MUTED := "a89680"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_ACCENT_AMBER_HOVER := "f0c060"
const COLOR_CARD_BORDER := "c9b89e"
const COLOR_ERROR := "e2554a"

const MAX_WEAVE_COUNT := 5

# Skill-type icon color coding -- identical to sanctum.gd/hub.gd's own ICON_COLORS (same locked
# Icon Conventions), even though Profile only ever uses the "dot" shape, not the full icons.
const ICON_COLORS := {
	"sword": "e0736a",
	"shield": "7aa8d8",
	"heart": "6cb87c",
	"bolt": "e8b95a",
	"diamond": "e8a03a",
}

const PART_ORDER := ["Head", "Frame", "Tail", "Crest"]

# Real per-color Anima portraits -- identical mapping to sanctum.gd, all 6 colors including
# Vulcan/Mirage, no fallback needed (see that script's own comment).
const PORTRAIT_SPRITES := {
	"Crimson": "res://assets/animas/crimson.png",
	"Onyx": "res://assets/animas/onyx.png",
	"Verdant": "res://assets/animas/verdant.png",
	"Azure": "res://assets/animas/azure.png",
	"Vulcan": "res://assets/animas/vulcan.png",
	"Mirage": "res://assets/animas/mirage.png",
}

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")

@onready var _background: TextureRect = $Background
@onready var _status_label: Label = $Margin/Scroll/Content/StatusLabel
@onready var _back_button: Button = $Margin/Scroll/Content/HeaderRow/BackButton
@onready var _portrait_wrap: AspectRatioContainer = $Margin/Scroll/Content/ProfileHeaderRow/PortraitWrap
@onready var _name_row: HBoxContainer = $Margin/Scroll/Content/ProfileHeaderRow/NameSection/NameRow
@onready var _name_click_area: HBoxContainer = $Margin/Scroll/Content/ProfileHeaderRow/NameSection/NameRow/NameClickArea
@onready var _name_label: Label = $Margin/Scroll/Content/ProfileHeaderRow/NameSection/NameRow/NameClickArea/NameLabel
@onready var _name_line_edit: LineEdit = $Margin/Scroll/Content/ProfileHeaderRow/NameSection/NameLineEdit
@onready var _color_gen_weave_label: Label = $Margin/Scroll/Content/ProfileHeaderRow/NameSection/ColorGenWeaveLabel
@onready var _show_hidden_toggle: CheckButton = $Margin/Scroll/Content/ThreadsSection/ThreadsHeaderRow/ShowHiddenToggle
@onready var _threads_list: VBoxContainer = $Margin/Scroll/Content/ThreadsSection/ThreadsList
@onready var _lineage_list: VBoxContainer = $Margin/Scroll/Content/LineageSection/LineageList
@onready var _history_counters_label: Label = $Margin/Scroll/Content/HistorySection/HistoryCountersLabel
@onready var _history_list: VBoxContainer = $Margin/Scroll/Content/HistorySection/HistoryList

var _hub: HubConnection
var _anima_id: String = ""
var _current_detail: Dictionary = {}
var _is_editing_name: bool = false


func _ready() -> void:
	_apply_theme()
	_back_button.pressed.connect(func(): get_tree().change_scene_to_file(SANCTUM_SCENE))
	_name_click_area.gui_input.connect(_on_name_click_area_gui_input)
	_name_line_edit.text_submitted.connect(func(_t): _commit_rename())
	_name_line_edit.focus_exited.connect(_commit_rename)
	_show_hidden_toggle.toggled.connect(func(_pressed: bool): _render_threads())

	_anima_id = NavState.selected_anima_id
	if _anima_id == "":
		_set_status("No Anima selected -- returning to Sanctum.", true)
		get_tree().change_scene_to_file(SANCTUM_SCENE)
		return

	if not AuthState.is_authenticated():
		_set_status("No active session -- returning to Login.", true)
		get_tree().change_scene_to_file(LOGIN_SCENE)
		return

	_set_status("Connecting to GameHub...", false)
	_run()


func _run() -> void:
	var ok := await _connect_hub(AuthState.token)
	if not ok:
		_set_status("Could not connect to GameHub.", true)
		return

	_set_status("", false)
	await _load_detail()


func _load_detail() -> void:
	var detail: Variant = await _hub.invoke("GetAnimaDetail", [_anima_id])
	if not (detail is Dictionary):
		_set_status("Could not load this Anima. Check your connection and try again.", true)
		return

	_current_detail = detail
	_render_all()


func _render_all() -> void:
	_render_header()
	_render_threads()
	_render_lineage()
	_render_history()


func _render_header() -> void:
	var color_name: String = str(_current_detail.get("color", ""))
	var gen: int = int(_current_detail.get("gen", 1))
	var weave_count: int = int(_current_detail.get("weaveCount", 0))
	var in_team: bool = bool(_current_detail.get("inTeam", false))

	for child in _portrait_wrap.get_children():
		child.free()
	var portrait := TextureRect.new()
	portrait.texture = load(PORTRAIT_SPRITES.get(color_name, PORTRAIT_SPRITES["Crimson"]))
	portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	portrait.stretch_mode = TextureRect.STRETCH_SCALE
	_portrait_wrap.add_child(portrait)

	_name_label.text = str(_current_detail.get("name", "?"))

	for child in _name_row.get_children():
		if child != _name_click_area:
			child.free()
	if in_team:
		_name_row.add_child(_build_in_team_badge())

	_color_gen_weave_label.text = "Color: %s    Gen: %d    Weave: %d/%d" % [color_name, gen, weave_count, MAX_WEAVE_COUNT]


func _build_in_team_badge() -> Control:
	var badge := PanelContainer.new()
	# Plain container flow here (not anchored absolute like Sanctum's card overlay) -- this badge
	# sits inline in a normal HBoxContainer, so it sizes to its Label content correctly once in the
	# tree without needing Sanctum's fixed-offset workaround (that workaround was only needed
	# because THAT badge had to float over a fixed-size card via anchors computed before the Label
	# existed -- see sanctum.gd's own comment).
	var badge_style := StyleBoxFlat.new()
	badge_style.bg_color = Color(COLOR_ACCENT_AMBER)
	badge_style.set_corner_radius_all(10)
	badge_style.content_margin_left = 10
	badge_style.content_margin_right = 10
	badge_style.content_margin_top = 4
	badge_style.content_margin_bottom = 4
	badge.add_theme_stylebox_override("panel", badge_style)

	var badge_label := Label.new()
	badge_label.text = "In team"
	badge_label.add_theme_color_override("font_color", Color(COLOR_GRADIENT_BOTTOM))
	badge_label.add_theme_font_size_override("font_size", 12)
	badge.add_child(badge_label)

	return badge


func _on_name_click_area_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_begin_rename()


func _begin_rename() -> void:
	if _is_editing_name:
		return
	_is_editing_name = true
	_name_row.visible = false
	_name_line_edit.text = _name_label.text
	_name_line_edit.visible = true
	_name_line_edit.editable = true
	_name_line_edit.grab_focus()
	_name_line_edit.select_all()


## Shared by both Enter (text_submitted) and blur (focus_exited) -- the _is_editing_name guard,
## cleared immediately, makes this safe to call twice in a row (Enter often also triggers a
## focus-loss right after), matching the same double-fire concern login.gd's own Enter-key
## handling already flagged (see CLAUDE.md Known TODOs item 19).
func _commit_rename() -> void:
	if not _is_editing_name:
		return
	_is_editing_name = false
	_name_line_edit.visible = false
	_name_row.visible = true

	var new_name := _name_line_edit.text.strip_edges()
	if new_name == "" or new_name == _name_label.text:
		return # Blank or unchanged input silently reverts -- not every blur is a real rename attempt.

	_set_status("Renaming...", false)
	var result: Variant = await _hub.invoke("RenameAnima", [_anima_id, new_name])

	if result is Dictionary and result.has("name"):
		_current_detail["name"] = result.get("name")
		_name_label.text = str(result.get("name"))
		_set_status("", false)
	else:
		_set_status("Rename failed -- check your connection and try again.", true)


func _render_threads() -> void:
	for child in _threads_list.get_children():
		child.free()

	var parts_by_name: Dictionary = {}
	for p: Variant in _current_detail.get("parts", []):
		if p is Dictionary:
			parts_by_name[str(p.get("part", ""))] = p

	var show_hidden := _show_hidden_toggle.button_pressed
	for part_name: String in PART_ORDER:
		var part_data: Dictionary = parts_by_name.get(part_name, {})
		_threads_list.add_child(_build_thread_row(part_name, part_data, show_hidden))


func _build_thread_row(part_name: String, part_data: Dictionary, show_hidden: bool) -> Control:
	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 2)

	var dominant: Dictionary = part_data.get("dominant", {})

	var main_row := HBoxContainer.new()
	main_row.add_theme_constant_override("separation", 6)

	var dot := Control.new()
	dot.set_script(ICON_GLYPH_SCRIPT)
	var icon_kind := _icon_kind_for_skill(part_name, dominant)
	dot.set("icon_kind", "dot")
	dot.set("icon_color", Color(ICON_COLORS.get(icon_kind, "e8a03a")))
	dot.set("icon_size", 10.0)
	main_row.add_child(dot)

	var main_label := Label.new()
	main_label.text = "%s: %s" % [part_name, str(dominant.get("name", "--"))]
	main_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	main_label.add_theme_font_size_override("font_size", 13)
	main_row.add_child(main_label)

	col.add_child(main_row)

	if show_hidden:
		var r1: Dictionary = part_data.get("r1", {})
		var r2: Dictionary = part_data.get("r2", {})
		var hidden_margin := MarginContainer.new()
		hidden_margin.add_theme_constant_override("margin_left", 18)
		var hidden_label := Label.new()
		hidden_label.text = "R1: %s   R2: %s" % [str(r1.get("name", "--")), str(r2.get("name", "--"))]
		hidden_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		hidden_label.add_theme_font_size_override("font_size", 11)
		hidden_margin.add_child(hidden_label)
		col.add_child(hidden_margin)

	return col


# Icon-kind resolution for the Threads dot-accent -- identical rule to sanctum.gd/hub.gd's own
# _icon_kind_for_part: Crest is ALWAYS diamond regardless of category; Attack->sword, Heal->heart,
# Buff splits on GrantsShield (shield vs bolt); everything else falls back to bolt.
func _icon_kind_for_skill(part_name: String, skill: Dictionary) -> String:
	if part_name == "Crest":
		return "diamond"

	var category := str(skill.get("category", ""))
	match category:
		"Attack": return "sword"
		"Heal": return "heart"
		"Buff": return "shield" if bool(skill.get("grantsShield", false)) else "bolt"
		_: return "bolt"


func _render_lineage() -> void:
	for child in _lineage_list.get_children():
		child.free()

	_lineage_list.add_child(_build_lineage_row("Parent A", _current_detail.get("parentAId"), _current_detail.get("parentAName")))
	_lineage_list.add_child(_build_lineage_row("Parent B", _current_detail.get("parentBId"), _current_detail.get("parentBName")))
	_lineage_list.add_child(_build_siblings_row(_current_detail.get("siblings", [])))
	_lineage_list.add_child(_build_lineage_row("Echo Twin", _current_detail.get("echoTwinId"), _current_detail.get("echoTwinName")))


func _build_lineage_row(label_text: String, id: Variant, link_name: Variant) -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)

	var field_label := Label.new()
	field_label.text = "%s:" % label_text
	field_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	row.add_child(field_label)

	if id == null or str(id) == "":
		row.add_child(_build_none_label())
	else:
		row.add_child(_build_link_button(str(link_name), str(id)))

	return row


func _build_siblings_row(siblings: Array) -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)

	var field_label := Label.new()
	field_label.text = "Siblings:"
	field_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	row.add_child(field_label)

	if siblings.is_empty():
		row.add_child(_build_none_label())
	else:
		for i in range(siblings.size()):
			var sib: Variant = siblings[i]
			if not (sib is Dictionary):
				continue
			row.add_child(_build_link_button(str(sib.get("name", "?")), str(sib.get("id", ""))))
			if i < siblings.size() - 1:
				var comma := Label.new()
				comma.text = ","
				comma.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
				row.add_child(comma)

	return row


func _build_none_label() -> Label:
	var label := Label.new()
	label.text = "— none —"
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	return label


func _build_link_button(link_text: String, target_id: String) -> Button:
	var btn := Button.new()
	btn.text = link_text
	btn.flat = true
	btn.focus_mode = Control.FOCUS_NONE
	btn.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	btn.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
	btn.add_theme_color_override("font_hover_color", Color(COLOR_ACCENT_AMBER_HOVER))
	btn.add_theme_color_override("font_pressed_color", Color(COLOR_ACCENT_AMBER_HOVER))
	btn.pressed.connect(_on_lineage_link_pressed.bind(target_id))
	return btn


func _on_lineage_link_pressed(target_id: String) -> void:
	if target_id == "":
		return
	NavState.selected_anima_id = target_id
	get_tree().change_scene_to_file(PROFILE_SCENE)


func _render_history() -> void:
	var completed: int = int(_current_detail.get("completedDelveCount", 0))
	var failed: int = int(_current_detail.get("failedDelveCount", 0))
	_history_counters_label.text = "Completed: %d    Failed: %d" % [completed, failed]

	for child in _history_list.get_children():
		child.free()

	var recent: Array = _current_detail.get("recentDelveHistory", [])
	if recent.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No delves yet."
		empty_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		_history_list.add_child(empty_label)
		return

	for entry: Variant in recent:
		if entry is Dictionary:
			_history_list.add_child(_build_history_row(entry))


func _build_history_row(entry: Dictionary) -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 10)

	var outcome_label := Label.new()
	outcome_label.text = str(entry.get("outcome", "?"))
	outcome_label.custom_minimum_size = Vector2(70, 0)
	outcome_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	row.add_child(outcome_label)

	var date_label := Label.new()
	date_label.text = _format_date(str(entry.get("timestamp", "")))
	date_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	row.add_child(date_label)

	return row


# recentDelveHistory's Timestamp comes over the wire as a JSON-serialized C# DateTime (ISO 8601,
# e.g. "2026-07-23T10:15:30.1234567Z" or without the trailing Z depending on DateTimeKind) -- the
# task wants date-only, so this just takes everything before the "T" rather than pulling in
# Godot's Time singleton for a single string split.
func _format_date(timestamp: String) -> String:
	if "T" in timestamp:
		return timestamp.split("T")[0]
	return timestamp


func _set_status(text: String, is_error: bool) -> void:
	_status_label.visible = text != ""
	_status_label.remove_theme_color_override("font_color")
	_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR) if is_error else Color(COLOR_TEXT_CREAM_DIM))
	_status_label.text = text


## Same connect-and-poll pattern as hub.gd/sanctum.gd's own _connect_hub -- see
## connectivity_test.gd's comment for why a bare `await hub.connected` isn't safe to use here.
func _connect_hub(token: String) -> bool:
	_hub = HubConnection.new()
	add_child(_hub)
	_hub.debug_mode(true)

	var fail_reason := ""
	_hub.error_occurred.connect(func(msg: String): fail_reason = msg)

	_hub.connect_to_url(SERVER_WS_URL, token)

	var elapsed := 0.0
	while not _hub.is_connected_to_hub() and elapsed < 10.0:
		await get_tree().process_frame
		elapsed += get_process_delta_time()

	if not _hub.is_connected_to_hub():
		_set_status("FAILED: hub did not connect within 10s. Last error: %s" % fail_reason, true)
		return false

	return true


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

	_status_label.add_theme_font_size_override("font_size", 13)

	_name_click_area.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	_name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_name_label.add_theme_font_size_override("font_size", 22)

	_name_line_edit.add_theme_font_size_override("font_size", 22)
	var name_edit_normal := StyleBoxFlat.new()
	name_edit_normal.bg_color = Color(0.11, 0.08, 0.06, 0.9)
	name_edit_normal.border_color = Color(Color(COLOR_CARD_BORDER), 0.35)
	name_edit_normal.set_border_width_all(1)
	name_edit_normal.set_corner_radius_all(6)
	name_edit_normal.set_content_margin_all(8)
	var name_edit_focus := name_edit_normal.duplicate()
	name_edit_focus.border_color = Color(COLOR_ACCENT_AMBER)
	_name_line_edit.add_theme_stylebox_override("normal", name_edit_normal)
	_name_line_edit.add_theme_stylebox_override("focus", name_edit_focus)
	_name_line_edit.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))

	_color_gen_weave_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_color_gen_weave_label.add_theme_font_size_override("font_size", 13)

	for title: Label in [
		$Margin/Scroll/Content/ThreadsSection/ThreadsHeaderRow/ThreadsTitle,
		$Margin/Scroll/Content/LineageSection/LineageTitle,
		$Margin/Scroll/Content/HistorySection/HistoryTitle,
	]:
		title.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
		title.add_theme_font_size_override("font_size", 15)

	_history_counters_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	_history_counters_label.add_theme_font_size_override("font_size", 12)
