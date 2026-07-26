extends Control

## The real Resource node screen -- matches assets/screens/resource_room_v2.html exactly (golden
## radial bg, glow+sparkle centerpiece, "A quiet cache" flavor text, +Wisp readout, single Collect
## button). Reached from delve.gd, per Resource node arrival.
##
## ARCHITECTURE NOTE (same class of deviation as hub.gd's portal -- see delve.gd's own top-of-file
## comment): this is instanced as a CHILD panel of delve.tscn's own overlay, sharing delve.gd's
## already-open HubConnection, rather than a top-level scene reached via change_scene_to_file. A
## real scene change would tear down the connection that owns the in-progress DelveRun (ActiveDelveRun
## is in-memory, per-connection, no server-side resume -- confirmed in CLAUDE.md's own documented
## gaps) -- returning to a fresh delve.tscn afterward would either lose the run entirely or force a
## brand-new StartDelve(), wiping map position/cleared-nodes progress. Sharing the connection avoids
## that class of bug entirely. This file is still a real, independently-loadable .tscn/.gd pair
## (satisfies the letter of "build resource_node.tscn/.gd") -- it just isn't top-level-navigated.
##
## Resolution timing: CollectResourceNode() is called the moment start() runs (not deferred to the
## Collect button press) -- the server has no separate "peek the reward" endpoint, and the mockup's
## own "+30 Wisp" readout has to show the REAL granted amount, which doesn't exist until the call
## happens. The Collect button is therefore an acknowledge-and-return action, same shape as the
## Anima Reveal screen's Confirm button (reward already granted in substance, confirm is separate).

signal resolved

const COLOR_GRADIENT_TOP := "4a3e1e"
const COLOR_GRADIENT_MID := "2b2413"
const COLOR_GRADIENT_BOTTOM := "17130a"
const COLOR_CARD_BG := "1e1610"
const COLOR_CARD_BORDER := "c9b89e"
const COLOR_TEXT_CREAM := "f0e8d4"
const COLOR_TEXT_MUTED := "a8a080"
const COLOR_ACCENT_AMBER := "e8c03a"
const COLOR_GLOW := "e8c03a"
const COLOR_BUTTON_TEXT := "2b2413"
const COLOR_ERROR := "e2554a"

const GRADIENT_BUTTON_A := "c9a82f"
const GRADIENT_BUTTON_B := "a3841c"

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")
const GLOW_CIRCLE_SCRIPT := preload("res://scripts/glow_circle.gd")

@onready var _background: TextureRect = $Background
@onready var _centerpiece_wrap: Control = $CenterContainer/Card/Margin/Content/CenterpieceWrap
@onready var _title_label: Label = $CenterContainer/Card/Margin/Content/TitleLabel
@onready var _subtitle_label: Label = $CenterContainer/Card/Margin/Content/SubtitleLabel
@onready var _wisp_row: HBoxContainer = $CenterContainer/Card/Margin/Content/WispRow
@onready var _wisp_label: Label = $CenterContainer/Card/Margin/Content/WispRow/WispLabel
@onready var _status_label: Label = $CenterContainer/Card/Margin/Content/StatusLabel
@onready var _button_slot: Control = $CenterContainer/Card/Margin/Content/ButtonSlot

var _hub: HubConnection


func _ready() -> void:
	_apply_theme()
	_build_centerpiece()
	_wisp_row.visible = false
	_status_label.text = ""


## Called by delve.gd right after instancing this scene and adding it to the tree, passing its own
## already-connected HubConnection -- see this file's own top-of-file comment for why.
func start(hub: HubConnection) -> void:
	_hub = hub
	_resolve()


func _resolve() -> void:
	var result: Variant = await _hub.invoke("CollectResourceNode", [])
	if not (result is Dictionary):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not collect this cache -- check your connection."
		_show_button()
		return

	var wisp_granted := int(result.get("wispGranted", 0))
	_wisp_label.text = "+%d Wisp" % wisp_granted
	_wisp_row.visible = true
	_show_button()


func _show_button() -> void:
	for child in _button_slot.get_children():
		child.free()
	_button_slot.add_child(_build_gradient_button("Collect", GRADIENT_BUTTON_A, GRADIENT_BUTTON_B, COLOR_BUTTON_TEXT, _on_collect_pressed))


func _on_collect_pressed() -> void:
	resolved.emit()


func _build_centerpiece() -> void:
	var glow := Control.new()
	glow.set_script(GLOW_CIRCLE_SCRIPT)
	glow.set_anchors_preset(Control.PRESET_FULL_RECT)
	glow.set("glow_color", Color(COLOR_GLOW))
	glow.set("glow_radius", 60.0)
	_centerpiece_wrap.add_child(glow)

	var icon := Control.new()
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", "sparkle")
	icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	icon.set("icon_size", 56.0)
	# Centered manually (this wrapper is a plain Control, not a container) -- CenterpieceWrap's own
	# size is fixed via custom_minimum_size in the .tscn (140x140, matching the mock's own
	# height:140px centerpiece area).
	icon.position = Vector2(70, 70) - Vector2(28, 28)
	_centerpiece_wrap.add_child(icon)


## Shared gradient-button builder (amber here, purple in treasure_node.gd -- kept as two small local
## copies rather than a shared script, matching this codebase's existing per-screen small-helper
## duplication convention, e.g. sanctum.gd/hub.gd each keeping their own ICON_COLORS). A TextureRect
## painted with a diagonal GradientTexture2D sits behind a transparent flat Button (StyleBoxEmpty on
## every state) so clicks/hover still work natively -- StyleBoxFlat has no gradient-fill support in
## Godot 4, and a sharp-cornered gradient rect (vs. the mock's border-radius:10px) is an accepted
## placeholder-tier simplification, same spirit as this codebase's other cosmetic simplifications
## (e.g. sanctum.gd's square-portrait-vs-mock's-rounded-corners comment).
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
	_wisp_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_wisp_label.add_theme_font_size_override("font_size", 20)
	_status_label.add_theme_font_size_override("font_size", 12)

	# Screen background IS the golden radial gradient (per this task's own "Golden radial
	# background" instruction, matching CLAUDE.md's Resource LOCKED design text) -- Card exists
	# only to center/pad content, so it stays transparent rather than drawing a second, visually
	# competing panel on top of the full-bleed background.
	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(0, 0, 0, 0)
	$CenterContainer/Card.add_theme_stylebox_override("panel", card_style)
