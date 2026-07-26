extends Control

## The real Treasure node screen -- matches assets/screens/treasure_room.html exactly (purple/
## magenta radial bg, glow+artifact-icon centerpiece, "A forgotten chest" flavor text, a highlighted
## card revealing the real rolled Artifact's icon/name/description, single Claim button). Reached
## from delve.gd, per Treasure node arrival.
##
## Same "child panel of delve.tscn, shares its HubConnection" architecture as resource_node.gd --
## see that file's own top-of-file comment for why a real top-level scene change would break the
## in-progress DelveRun.
##
## Resolution timing: ClaimTreasureNode() is called the moment start() runs, same reasoning as
## resource_node.gd -- there's no separate "peek the reward" endpoint, and the mock's own revealed-
## artifact card needs the REAL rolled Artifact, which doesn't exist until the call happens. The
## centerpiece icon is therefore built AFTER resolution (it mirrors whichever Artifact was actually
## rolled, per the mock's own repeated-icon treatment), not eagerly in _ready() the way Resource's
## fixed "sparkle" icon can be.
##
## Artifact-cap outcome (locked design: skipped/lost, not retried) is shown as a short inline
## message in place of the card, per this task's own instruction -- not a separate screen.

signal resolved

const COLOR_GRADIENT_TOP := "4a2e3e"
const COLOR_GRADIENT_MID := "2b1a2b"
const COLOR_GRADIENT_BOTTOM := "170a17"
const COLOR_CARD_BG := "1e141c"
const COLOR_CARD_BORDER := "d6b8d6"
const COLOR_TEXT_CREAM := "f0d8f0"
const COLOR_TEXT_MUTED := "a890a8"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_GLOW := "c778d6"
const COLOR_BUTTON_TEXT := "f0e4f0"
const COLOR_ERROR := "e2554a"

const GRADIENT_BUTTON_A := "a35fc4"
const GRADIENT_BUTTON_B := "7a3fa3"

# Same 12-entry icon mapping as collection.gd/delve.gd -- neither icon kind nor display order
# exists on the wire (ClaimTreasureNode only returns Name/Description), so this is the client's own
# static table, duplicated per this codebase's existing per-screen convention.
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
const FALLBACK_ICON := "gift" # cap-reached / error: no specific Artifact to represent

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")
const GLOW_CIRCLE_SCRIPT := preload("res://scripts/glow_circle.gd")

@onready var _background: TextureRect = $Background
@onready var _centerpiece_wrap: Control = $CenterContainer/Card/Margin/Content/CenterpieceWrap
@onready var _title_label: Label = $CenterContainer/Card/Margin/Content/TitleLabel
@onready var _subtitle_label: Label = $CenterContainer/Card/Margin/Content/SubtitleLabel
@onready var _artifact_card: PanelContainer = $CenterContainer/Card/Margin/Content/ArtifactCard
@onready var _artifact_icon_slot: PanelContainer = $CenterContainer/Card/Margin/Content/ArtifactCard/ArtifactMargin/ArtifactContent/ArtifactIconSlot
@onready var _artifact_icon_center: Control = $CenterContainer/Card/Margin/Content/ArtifactCard/ArtifactMargin/ArtifactContent/ArtifactIconSlot/ArtifactIconCenter
@onready var _artifact_name_label: Label = $CenterContainer/Card/Margin/Content/ArtifactCard/ArtifactMargin/ArtifactContent/ArtifactNameLabel
@onready var _artifact_desc_label: Label = $CenterContainer/Card/Margin/Content/ArtifactCard/ArtifactMargin/ArtifactContent/ArtifactDescLabel
@onready var _cap_message_label: Label = $CenterContainer/Card/Margin/Content/CapMessageLabel
@onready var _status_label: Label = $CenterContainer/Card/Margin/Content/StatusLabel
@onready var _button_slot: Control = $CenterContainer/Card/Margin/Content/ButtonSlot

var _hub: HubConnection


func _ready() -> void:
	_apply_theme()

	var glow := Control.new()
	glow.set_script(GLOW_CIRCLE_SCRIPT)
	glow.set_anchors_preset(Control.PRESET_FULL_RECT)
	glow.set("glow_color", Color(COLOR_GLOW))
	glow.set("glow_radius", 65.0)
	_centerpiece_wrap.add_child(glow)

	_artifact_card.visible = false
	_cap_message_label.visible = false
	_status_label.text = ""


## Called by delve.gd right after instancing this scene -- see this file's own top-of-file comment.
func start(hub: HubConnection) -> void:
	_hub = hub
	_resolve()


func _resolve() -> void:
	var result: Variant = await _hub.invoke("ClaimTreasureNode", [])
	if not (result is Dictionary):
		_add_centerpiece_icon(FALLBACK_ICON)
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not open this chest -- check your connection."
		_show_button()
		return

	var cap_reached := bool(result.get("capReached", false))
	if cap_reached:
		_add_centerpiece_icon(FALLBACK_ICON)
		_cap_message_label.visible = true
		_show_button()
		return

	var artifact_name := str(result.get("artifactName", ""))
	var artifact_description := str(result.get("artifactDescription", ""))
	var icon_kind: String = ARTIFACT_ICONS.get(artifact_name, FALLBACK_ICON)

	_add_centerpiece_icon(icon_kind)

	var card_icon := Control.new()
	card_icon.set_script(ICON_GLYPH_SCRIPT)
	card_icon.set("icon_kind", icon_kind)
	card_icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	card_icon.set("icon_size", 22.0)
	_artifact_icon_center.add_child(card_icon)

	_artifact_name_label.text = artifact_name
	_artifact_desc_label.text = artifact_description
	_artifact_card.visible = true
	_show_button()


func _add_centerpiece_icon(icon_kind: String) -> void:
	var icon := Control.new()
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", icon_kind)
	icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	icon.set("icon_size", 52.0)
	icon.position = Vector2(70, 70) - Vector2(26, 26)
	_centerpiece_wrap.add_child(icon)


func _show_button() -> void:
	for child in _button_slot.get_children():
		child.free()
	_button_slot.add_child(_build_gradient_button("Claim", GRADIENT_BUTTON_A, GRADIENT_BUTTON_B, COLOR_BUTTON_TEXT, _on_claim_pressed))


func _on_claim_pressed() -> void:
	resolved.emit()


## Same gradient-button technique as resource_node.gd's own copy (purple here, amber there) -- see
## that file's own comment for why this is a TextureRect+transparent-Button pair rather than a
## StyleBoxFlat (no native gradient fill in Godot 4).
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


func _apply_theme() -> void:
	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color(COLOR_GRADIENT_TOP), Color(COLOR_GRADIENT_MID), Color(COLOR_GRADIENT_BOTTOM)])
	gradient.offsets = PackedFloat32Array([0.0, 0.5, 1.0])

	var gradient_texture := GradientTexture2D.new()
	gradient_texture.gradient = gradient
	gradient_texture.fill = GradientTexture2D.FILL_RADIAL
	gradient_texture.width = 512
	gradient_texture.height = 512
	gradient_texture.fill_from = Vector2(0.5, 0.3)
	gradient_texture.fill_to = Vector2(1.0, 0.3)
	_background.texture = gradient_texture

	_title_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_title_label.add_theme_font_size_override("font_size", 15)
	_subtitle_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_subtitle_label.add_theme_font_size_override("font_size", 12)
	_status_label.add_theme_font_size_override("font_size", 12)

	_cap_message_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_cap_message_label.add_theme_font_size_override("font_size", 12)

	# Screen background IS the golden/magenta radial gradient (per this task's own "Golden radial
	# background"/"Purple/magenta radial background" instruction, matching CLAUDE.md's Resource/
	# Treasure LOCKED design text) -- Card exists only to center/pad content, so it stays transparent
	# rather than drawing a second, visually competing panel on top of the full-bleed background.
	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(0, 0, 0, 0)
	$CenterContainer/Card.add_theme_stylebox_override("panel", card_style)

	var artifact_style := StyleBoxFlat.new()
	artifact_style.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	artifact_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	artifact_style.set_border_width_all(1)
	artifact_style.set_corner_radius_all(12)
	_artifact_card.add_theme_stylebox_override("panel", artifact_style)

	# The icon's own small dark box (mock: 44x44, rgba(0,0,0,0.3), radius 8), distinct from the
	# outer ArtifactCard panel above it.
	var icon_slot_style := StyleBoxFlat.new()
	icon_slot_style.bg_color = Color(0, 0, 0, 0.3)
	icon_slot_style.set_corner_radius_all(8)
	_artifact_icon_slot.add_theme_stylebox_override("panel", icon_slot_style)

	_artifact_name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_artifact_name_label.add_theme_font_size_override("font_size", 13)
	_artifact_desc_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_artifact_desc_label.add_theme_font_size_override("font_size", 11)
