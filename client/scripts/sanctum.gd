extends Control

## The real Sanctum screen (replaces the placeholder_screen.gd content on sanctum.tscn). Matches
## assets/screens/sanctum_retrofitted.html: header ("Sanctum" + roster-count subtitle) + a grid of
## roster cards (portrait, name, color+Gen, parts list with skill-type icons, Weave Count bar).
## Active team members (InTeam == true) get the amber 2px border + "In team" badge; everyone else
## gets the default hairline border. Two deliberate deviations from the mock (per this task):
## 5-column grid instead of 3 (the mock's 3-col ratio was just a placeholder, not a locked spec),
## and real per-color portrait PNGs (assets/animas/) for ALL 6 colors including Vulcan/Mirage --
## unlike Hub's team row, there is no hybrid color-block fallback needed here since real (if
## temporary/placeholder-quality) art exists for every color.
##
## Whole card is the click target -- navigates to the real Anima Profile screen (anima_profile.tscn),
## passing the clicked Anima's Id via the NavState autoload (see that script's own comment for why:
## change_scene_to_file() has no way to pass an argument directly).
##
## No sort/filter yet -- flagged as an open item in CLAUDE.md's Open Design Threads, not blocking
## this pass (the roster is small enough for now).

const SERVER_WS_URL := "ws://localhost:5143/hubs/game"
const LOGIN_SCENE := "res://scenes/login.tscn"
const HUB_SCENE := "res://scenes/hub.tscn"
const PROFILE_SCENE := "res://scenes/anima_profile.tscn"

# Warm sanctuary/workshop theme + card styling, matching hub.gd's own palette exactly.
const COLOR_GRADIENT_TOP := "4a3a2e"
const COLOR_GRADIENT_MID := "2b2018"
const COLOR_GRADIENT_BOTTOM := "1a130e"
const COLOR_CARD_BG := "1e1610" # rgba(30,22,16,*) in the mock
const COLOR_CARD_BORDER := "c9b89e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_TEXT_CREAM_DIM := "e8cf9a"
const COLOR_TEXT_MUTED := "a89680"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_ERROR := "e2554a"

const GRID_COLUMNS := 5

# Weaving System's real cap (Anima.Core.Weaving.WeavingService.MaxWeaveCount) -- confirmed by
# reading the code, not assumed, since the task called this out explicitly.
const MAX_WEAVE_COUNT := 5

# Skill-type icon color coding, identical to hub.gd's ICON_COLORS -- same locked Icon Conventions
# (sword=Attack, heart=Heal, shield=Shield-granting Buff, bolt=other Buff, diamond=passive Crest).
const ICON_COLORS := {
	"sword": "e0736a",
	"shield": "7aa8d8",
	"heart": "6cb87c",
	"bolt": "e8b95a",
	"diamond": "e8a03a",
}

const PART_ORDER := ["Head", "Frame", "Tail", "Crest"]

# Real per-color Anima portraits -- all 6 colors, including Vulcan/Mirage. Those two are currently
# temporary copies of other colors' art (the user will swap in real generated art later), but they
# get wired up identically to the 4 true colors -- no hybrid fallback special-case on this screen
# (unlike Hub's team row, which still falls back to a flat color block for hybrids since it only
# has the 4 true-color Aspect sprites).
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
@onready var _status_label: Label = $Margin/Content/StatusLabel
@onready var _back_button: Button = $Margin/Content/HeaderRow/BackButton
@onready var _title_label: Label = $Margin/Content/HeaderRow/TitleLabel
@onready var _count_label: Label = $Margin/Content/HeaderRow/CountLabel
@onready var _roster_grid: GridContainer = $Margin/Content/RosterScroll/RosterGrid

var _hub: HubConnection


func _ready() -> void:
	_apply_theme()
	_back_button.pressed.connect(func(): get_tree().change_scene_to_file(HUB_SCENE))

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
	await _load_roster()


func _load_roster() -> void:
	var roster: Variant = await _hub.invoke("GetRoster", [])
	_render_header(roster)
	_render_roster(roster)


func _render_header(roster: Variant) -> void:
	_title_label.text = "Sanctum"
	var count: int = roster.size() if roster is Array else 0
	_count_label.text = "%d anima%s" % [count, "" if count == 1 else "s"]


func _render_roster(roster: Variant) -> void:
	# free(), not queue_free() -- queue_free() defers removal to end-of-frame, so a second
	# _render_roster call in quick succession would still see the stale children when rebuilding,
	# leaving duplicates behind. Same lesson Hub's _render_team already learned (see hub.gd).
	for child in _roster_grid.get_children():
		child.free()

	if not (roster is Array) or roster.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No animas yet."
		empty_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		_roster_grid.add_child(empty_label)
		return

	for a: Variant in roster:
		if a is Dictionary:
			_roster_grid.add_child(_build_card(a))


func _build_card(anima: Dictionary) -> Control:
	var in_team: bool = bool(anima.get("inTeam", false))

	var wrapper := Control.new()
	wrapper.custom_minimum_size = Vector2(0, 230)
	wrapper.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var card_button := Button.new()
	card_button.set_anchors_preset(Control.PRESET_FULL_RECT)
	card_button.focus_mode = Control.FOCUS_NONE
	card_button.text = ""
	_style_card_button(card_button, in_team)
	card_button.pressed.connect(_on_card_pressed.bind(str(anima.get("id", ""))))
	wrapper.add_child(card_button)

	var card_margin := MarginContainer.new()
	card_margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	card_margin.mouse_filter = Control.MOUSE_FILTER_IGNORE
	card_margin.add_theme_constant_override("margin_left", 10)
	card_margin.add_theme_constant_override("margin_top", 10)
	card_margin.add_theme_constant_override("margin_right", 10)
	card_margin.add_theme_constant_override("margin_bottom", 10)
	card_button.add_child(card_margin)

	var card_content := VBoxContainer.new()
	card_content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	card_content.add_theme_constant_override("separation", 8)
	card_margin.add_child(card_content)

	card_content.add_child(_build_portrait_row(anima))
	card_content.add_child(_build_parts_list(anima))
	card_content.add_child(_build_weave_row(anima))
	card_content.add_child(_build_weave_bar(anima))

	if in_team:
		wrapper.add_child(_build_in_team_badge())

	return wrapper


func _build_portrait_row(anima: Dictionary) -> Control:
	var row := HBoxContainer.new()
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_theme_constant_override("separation", 8)

	var color_name: String = str(anima.get("color", ""))

	var portrait_wrap := AspectRatioContainer.new()
	portrait_wrap.ratio = 1.0
	portrait_wrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# Explicit custom_minimum_size -- without one, an AspectRatioContainer gets ~zero height from
	# its parent VBoxContainer and its portrait never becomes visible at all. This is the exact
	# invisible-portrait bug CLAUDE.md's Hub Screen section documents (found via a real rendered
	# screenshot, not a headless property check) -- avoided here from the start rather than
	# reintroduced and re-fixed later.
	portrait_wrap.custom_minimum_size = Vector2(56, 56)
	row.add_child(portrait_wrap)

	var portrait := TextureRect.new()
	portrait.texture = load(PORTRAIT_SPRITES.get(color_name, PORTRAIT_SPRITES["Crimson"]))
	# EXPAND_IGNORE_SIZE (not a proportional mode) -- AspectRatioContainer already owns sizing/
	# aspect for this child; Godot logs a warning if a proportional expand_mode is set inside one.
	portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	portrait.stretch_mode = TextureRect.STRETCH_SCALE
	portrait.mouse_filter = Control.MOUSE_FILTER_IGNORE
	portrait_wrap.add_child(portrait)

	var name_color_box := VBoxContainer.new()
	name_color_box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_color_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_color_box.add_theme_constant_override("separation", 1)
	row.add_child(name_color_box)

	var name_label := Label.new()
	name_label.text = str(anima.get("name", "?"))
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	name_label.add_theme_font_size_override("font_size", 14)
	name_color_box.add_child(name_label)

	var color_gen_label := Label.new()
	color_gen_label.text = "%s, Gen %d" % [color_name, int(anima.get("gen", 1))]
	color_gen_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	color_gen_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	color_gen_label.add_theme_font_size_override("font_size", 11)
	name_color_box.add_child(color_gen_label)

	return row


func _build_parts_list(anima: Dictionary) -> Control:
	var list := VBoxContainer.new()
	list.mouse_filter = Control.MOUSE_FILTER_IGNORE
	list.add_theme_constant_override("separation", 3)

	var parts_by_name: Dictionary = {}
	for p: Variant in anima.get("parts", []):
		if p is Dictionary:
			parts_by_name[str(p.get("part", ""))] = p

	for part_name: String in PART_ORDER:
		var part_data: Dictionary = parts_by_name.get(part_name, {})
		list.add_child(_build_part_row(part_name, part_data))

	return list


func _build_part_row(part_name: String, part_data: Dictionary) -> Control:
	var row := HBoxContainer.new()
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_theme_constant_override("separation", 5)

	var icon_kind := _icon_kind_for_part(part_name, part_data)
	var icon := Control.new()
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", icon_kind)
	icon.set("icon_color", Color(ICON_COLORS.get(icon_kind, "e8a03a")))
	icon.set("icon_size", 12.0)
	row.add_child(icon)

	var label := Label.new()
	label.text = str(part_data.get("skillName", "--")) if not part_data.is_empty() else "--"
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	label.add_theme_font_size_override("font_size", 11)
	row.add_child(label)

	return row


# Icon-kind resolution, per CLAUDE.md's Skill-type icon set -- identical rule to hub.gd's own
# _icon_kind_for_part: Crest is ALWAYS diamond regardless of category (the passive-icon rule);
# Attack->sword, Heal->heart, Buff splits on GrantsShield (shield vs bolt); everything else
# (Debuff/Move/Summon) falls back to bolt, the closest generic "effect" icon.
func _icon_kind_for_part(part_name: String, part_data: Dictionary) -> String:
	if part_name == "Crest":
		return "diamond"

	var category := str(part_data.get("category", ""))
	match category:
		"Attack": return "sword"
		"Heal": return "heart"
		"Buff": return "shield" if bool(part_data.get("grantsShield", false)) else "bolt"
		_: return "bolt"


func _build_weave_row(anima: Dictionary) -> Control:
	var row := HBoxContainer.new()
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var weave_count: int = int(anima.get("weaveCount", 0))

	var weave_label := Label.new()
	weave_label.text = "Weave"
	weave_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	weave_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	weave_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	weave_label.add_theme_font_size_override("font_size", 11)
	row.add_child(weave_label)

	var count_label := Label.new()
	count_label.text = "%d/%d" % [weave_count, MAX_WEAVE_COUNT]
	count_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	count_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	count_label.add_theme_font_size_override("font_size", 11)
	row.add_child(count_label)

	return row


func _build_weave_bar(anima: Dictionary) -> Control:
	var bar := ProgressBar.new()
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bar.min_value = 0
	bar.max_value = MAX_WEAVE_COUNT
	bar.value = int(anima.get("weaveCount", 0))
	bar.show_percentage = false
	bar.custom_minimum_size = Vector2(0, 6)

	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(0, 0, 0, 0.4)
	bg.set_corner_radius_all(3)
	var fill := StyleBoxFlat.new()
	fill.bg_color = Color(COLOR_ACCENT_AMBER)
	fill.set_corner_radius_all(3)
	bar.add_theme_stylebox_override("background", bg)
	bar.add_theme_stylebox_override("fill", fill)

	return bar


func _build_in_team_badge() -> Control:
	var badge := PanelContainer.new()
	badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# Fixed offsets, not PRESET_MODE_MINSIZE -- this badge is built and returned before it (or its
	# Label child) ever enters the scene tree, so at build time its minimum size is still 0 (no
	# font/theme resolved yet), which silently baked a near-zero-width rect and clipped "In team"
	# down to "In" (found via an actual rendered screenshot, not a headless property check -- the
	# exact class of bug CLAUDE.md's Hub Screen section already warns about). A fixed pixel rect
	# anchored to the top-right corner sidesteps needing minimum-size timing at all.
	badge.anchor_left = 1.0
	badge.anchor_right = 1.0
	badge.anchor_top = 0.0
	badge.anchor_bottom = 0.0
	badge.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	badge.grow_vertical = Control.GROW_DIRECTION_END
	badge.offset_left = -72.0
	badge.offset_right = -8.0
	badge.offset_top = 8.0
	badge.offset_bottom = 28.0

	var badge_style := StyleBoxFlat.new()
	badge_style.bg_color = Color(COLOR_ACCENT_AMBER)
	badge_style.set_corner_radius_all(10)
	badge.add_theme_stylebox_override("panel", badge_style)

	var badge_label := Label.new()
	badge_label.text = "In team"
	badge_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	badge_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	badge_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	badge_label.add_theme_color_override("font_color", Color(COLOR_GRADIENT_BOTTOM))
	badge_label.add_theme_font_size_override("font_size", 11)
	badge.add_child(badge_label)

	return badge


func _on_card_pressed(anima_id: String) -> void:
	NavState.selected_anima_id = anima_id
	get_tree().change_scene_to_file(PROFILE_SCENE)


func _set_status(text: String, is_error: bool) -> void:
	_status_label.visible = text != ""
	_status_label.remove_theme_color_override("font_color")
	_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR) if is_error else Color(COLOR_TEXT_CREAM_DIM))
	_status_label.text = text


## Same connect-and-poll pattern as hub.gd/starter_reveal.gd's own _connect_hub -- see
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

	_title_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_title_label.add_theme_font_size_override("font_size", 18)
	_count_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_count_label.add_theme_font_size_override("font_size", 12)
	_status_label.add_theme_font_size_override("font_size", 13)

	_roster_grid.add_theme_constant_override("h_separation", 10)
	_roster_grid.add_theme_constant_override("v_separation", 10)


func _style_card_button(card: Button, in_team: bool) -> void:
	var normal := StyleBoxFlat.new()
	normal.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	if in_team:
		normal.border_color = Color(COLOR_ACCENT_AMBER)
		normal.set_border_width_all(2)
	else:
		normal.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
		normal.set_border_width_all(1)
	normal.set_corner_radius_all(12)

	var hover := normal.duplicate()
	hover.bg_color = Color(Color(COLOR_CARD_BG), 0.9)

	card.add_theme_stylebox_override("normal", normal)
	card.add_theme_stylebox_override("hover", hover)
	card.add_theme_stylebox_override("pressed", hover)
	card.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
