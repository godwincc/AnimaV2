extends Control

## The real Collection screen (replaces placeholder_screen.gd's "coming soon" card). Matches
## CLAUDE.md's screen-6 spec: a top resource summary (Wisp/Echo Shard/Vessel Shard, static
## description text per CLAUDE.md's Wisp iconography rule -- Wisp gets sparkle, never a coin) and
## a full 12-Artifact list below ("Artifacts (X of 12 discovered)"), each row either the real
## icon+name+description+"Delves won with: N" (discovered) or a dimmed "???"/"Undiscovered" card
## with no icon glyph drawn (locked design -- no invented "lock" IconGlyph kind).
##
## GetArtifactCollection() already returns a real per-artifact Description string (ArtifactSummary
## on the server), so that's what's rendered -- NOT the static one-line descriptions this task's
## own brief listed, since those turned out to mismatch the real coded Twin Flame mechanic (the
## brief's "doubles a chosen buff's duration" text describes a different, unimplemented effect;
## the real one -- see SampleArtifacts.CreateTwinFlame -- saves a player Anima from a lethal hit at
## 1 HP once per combat). Server text is the single source of truth for description copy; the
## static ARTIFACT_ICONS map below supplies ONLY icon kind + display order, neither of which the
## wire DTO carries.

const SERVER_WS_URL := "ws://localhost:5143/hubs/game"
const LOGIN_SCENE := "res://scenes/login.tscn"
const HUB_SCENE := "res://scenes/hub.tscn"

# Warm sanctuary/workshop theme, matching hub.gd/sanctum.gd's own palette exactly.
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

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")

# Resource summary cards -- static label/icon/description, counts come from GetLedger(). Keys match
# Anima.Core.Economy.ResourceType's own names (the LedgerSnapshot.Balances dictionary's real keys,
# confirmed against hub.gd's own balances.get("Wisp"/"EchoShard"/"VesselShard") usage).
const RESOURCE_CARDS := [
	{"key": "Wisp", "icon": "sparkle", "label": "Wisp", "description": "Spend on Weaving, Reforge and Shop"},
	{"key": "EchoShard", "icon": "shard", "label": "Echo Shards", "description": "Guarantee an Echo twin when Weaving"},
	{"key": "VesselShard", "icon": "hexagon", "label": "Vessel Shards", "description": "Rare Elite drop, future use TBD"},
]

# Stable client-side display order (server order isn't guaranteed to match) + icon-kind mapping --
# neither field exists on ArtifactSummary, so this is the one static table the client owns.
const ARTIFACT_ORDER := [
	"Twin Flame", "Wisp Charm", "Barrier Stone", "Vanguard's Bell", "Weaver's Thread", "Marked Coin",
	"Withering Fang", "Focusing Lens", "Silent Chime", "Ember Core", "Sapling Charm", "Sifting Stone",
]

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

@onready var _background: TextureRect = $Background
@onready var _status_label: Label = $Margin/Scroll/Content/StatusLabel
@onready var _back_button: Button = $Margin/Scroll/Content/HeaderRow/BackButton
@onready var _title_label: Label = $Margin/Scroll/Content/HeaderRow/TitleLabel
@onready var _resource_row: HBoxContainer = $Margin/Scroll/Content/ResourceRow
@onready var _artifacts_header_label: Label = $Margin/Scroll/Content/ArtifactsHeaderLabel
@onready var _artifacts_list: VBoxContainer = $Margin/Scroll/Content/ArtifactsList

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

	var ledger: Variant = await _hub.invoke("GetLedger", [])
	var artifacts: Variant = await _hub.invoke("GetArtifactCollection", [])

	_render_resources(ledger)
	_render_artifacts(artifacts)


func _render_resources(ledger: Variant) -> void:
	for child in _resource_row.get_children():
		child.free()

	var balances: Dictionary = {}
	if ledger is Dictionary:
		balances = ledger.get("balances", {})

	for entry: Dictionary in RESOURCE_CARDS:
		_resource_row.add_child(_build_resource_card(entry, int(balances.get(entry["key"], 0))))


func _build_resource_card(entry: Dictionary, count: int) -> Control:
	var card := PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.custom_minimum_size = Vector2(0, 96)

	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	card_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(12)
	card.add_theme_stylebox_override("panel", card_style)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 14)
	margin.add_theme_constant_override("margin_top", 12)
	margin.add_theme_constant_override("margin_right", 14)
	margin.add_theme_constant_override("margin_bottom", 12)
	card.add_child(margin)

	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 4)
	margin.add_child(content)

	var top_row := HBoxContainer.new()
	top_row.add_theme_constant_override("separation", 8)
	content.add_child(top_row)

	# Explicit custom_minimum_size -- a Control with real drawn content collapses to zero size
	# without one (the invisible-portrait/flat-bar-card bug class CLAUDE.md's Hub Screen section
	# documents), so this is set up front rather than found via a screenshot and re-fixed later.
	var icon := Control.new()
	icon.custom_minimum_size = Vector2(22, 22)
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", entry["icon"])
	icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	icon.set("icon_size", 22.0)
	top_row.add_child(icon)

	var label_col := VBoxContainer.new()
	label_col.add_theme_constant_override("separation", 0)
	top_row.add_child(label_col)

	var label := Label.new()
	label.text = str(entry["label"])
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	label.add_theme_font_size_override("font_size", 12)
	label_col.add_child(label)

	var count_label := Label.new()
	count_label.text = str(count)
	count_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	count_label.add_theme_font_size_override("font_size", 22)
	label_col.add_child(count_label)

	var desc_label := Label.new()
	desc_label.text = str(entry["description"])
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	desc_label.add_theme_font_size_override("font_size", 11)
	content.add_child(desc_label)

	return card


func _render_artifacts(artifacts: Variant) -> void:
	for child in _artifacts_list.get_children():
		child.free()

	var by_name: Dictionary = {}
	if artifacts is Array:
		for entry: Variant in artifacts:
			if entry is Dictionary:
				by_name[str(entry.get("name", ""))] = entry

	var discovered_count := 0
	for entry: Variant in by_name.values():
		if bool(entry.get("discovered", false)):
			discovered_count += 1

	_artifacts_header_label.text = "Artifacts (%d of 12 discovered)" % discovered_count

	for artifact_name: String in ARTIFACT_ORDER:
		var data: Dictionary = by_name.get(artifact_name, {})
		_artifacts_list.add_child(_build_artifact_row(artifact_name, data))


func _build_artifact_row(artifact_name: String, data: Dictionary) -> Control:
	var discovered: bool = bool(data.get("discovered", false))

	var card := PanelContainer.new()
	card.custom_minimum_size = Vector2(0, 64)

	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(Color(COLOR_CARD_BG), 0.75 if discovered else 0.4)
	card_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2 if discovered else 0.08)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(10)
	card.add_theme_stylebox_override("panel", card_style)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 12)
	margin.add_theme_constant_override("margin_top", 10)
	margin.add_theme_constant_override("margin_right", 12)
	margin.add_theme_constant_override("margin_bottom", 10)
	card.add_child(margin)

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 12)
	margin.add_child(row)

	if discovered:
		var icon := Control.new()
		icon.custom_minimum_size = Vector2(26, 26)
		icon.set_script(ICON_GLYPH_SCRIPT)
		icon.set("icon_kind", ARTIFACT_ICONS.get(artifact_name, "sparkle"))
		icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
		icon.set("icon_size", 26.0)
		row.add_child(icon)
	else:
		# Muted placeholder box, no icon glyph drawn -- locked design decision (don't invent a
		# "help/lock" IconGlyph kind for this).
		var placeholder := PanelContainer.new()
		placeholder.custom_minimum_size = Vector2(26, 26)
		var placeholder_style := StyleBoxFlat.new()
		placeholder_style.bg_color = Color(Color(COLOR_TEXT_MUTED), 0.15)
		placeholder_style.set_corner_radius_all(6)
		placeholder.add_theme_stylebox_override("panel", placeholder_style)
		row.add_child(placeholder)

	var text_col := VBoxContainer.new()
	text_col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	text_col.add_theme_constant_override("separation", 2)
	row.add_child(text_col)

	var name_label := Label.new()
	name_label.text = artifact_name if discovered else "???"
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM) if discovered else Color(COLOR_TEXT_MUTED))
	name_label.add_theme_font_size_override("font_size", 14)
	text_col.add_child(name_label)

	var desc_label := Label.new()
	desc_label.text = str(data.get("description", "")) if discovered else "Undiscovered"
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	desc_label.add_theme_font_size_override("font_size", 11)
	text_col.add_child(desc_label)

	if discovered:
		var won_label := Label.new()
		won_label.text = "Delves won with: %d" % int(data.get("delvesWonWith", 0))
		won_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		won_label.add_theme_font_size_override("font_size", 11)
		won_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		won_label.custom_minimum_size = Vector2(140, 0)
		row.add_child(won_label)

	return card


func _set_status(text: String, is_error: bool) -> void:
	_status_label.visible = text != ""
	_status_label.remove_theme_color_override("font_color")
	_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR) if is_error else Color(COLOR_TEXT_CREAM_DIM))
	_status_label.text = text


## Same connect-and-poll pattern as sanctum.gd/hub.gd's own _connect_hub.
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
	_status_label.add_theme_font_size_override("font_size", 13)
	_artifacts_header_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	_artifacts_header_label.add_theme_font_size_override("font_size", 14)

	_resource_row.add_theme_constant_override("separation", 12)
	_artifacts_list.add_theme_constant_override("separation", 8)
