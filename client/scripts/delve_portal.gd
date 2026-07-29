extends Control
class_name DelvePortal

## The reusable amber magic-circle portal component -- CLAUDE.md's "THE ANIMA LOGO" (also the
## unofficial game logo / the "enter a Delve" button): concentric rings, rune-mark dots on the
## true diagonals, a central diamond gem, and 4 cardinal sigils each in its own dark medallion --
## North=Crimson (simple inverted-V spike), East=Onyx (open bracket-line, straight centered
## vertical middle), South=Verdant (trunk + 2 branch offshoots), West=Azure (two parallel curved
## waves). Procedurally drawn (no image asset, matching the locked design's own SVG mockup), sized
## and labeled by the caller -- built for the Hub's "Delve" portal, but parameterized by
## `label_text` specifically so Weaving's own mini portal button can instance this same scene
## later instead of forking a near-identical copy.

signal pressed

@export var label_text: String = "Delve":
	set(value):
		label_text = value
		if is_node_ready():
			_label.text = value

@export var portal_size: float = 130.0:
	set(value):
		portal_size = value
		if is_node_ready():
			_apply_size()

const VIEWBOX := 260.0

const COLOR_GLOW := "f4c979"
const COLOR_RING_OUTER := "6a4f2a"
const COLOR_RING_MID := "8a6a3a"
const COLOR_RING_INNER := "e8a03a"
const COLOR_DOT := "a3742f"
const COLOR_DIAMOND_FILL := "c9862f"
const COLOR_DIAMOND_STROKE := "f4dba8"
const COLOR_GEM := "fde9b0"
const COLOR_MEDALLION_FILL := "2b2018"
const COLOR_MEDALLION_STROKE := "6a4f2a"
const COLOR_SIGIL := "a3742f"
const COLOR_LABEL := "f4dba8"

@onready var _label: Label = $Label


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	_label.text = label_text
	_label.add_theme_color_override("font_color", Color(COLOR_LABEL))
	_apply_size()


func _apply_size() -> void:
	custom_minimum_size = Vector2(portal_size, portal_size + 24.0)
	_label.position = Vector2(0, portal_size + 4.0)
	_label.size = Vector2(portal_size, 20.0)
	queue_redraw()


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		pressed.emit()


func _draw() -> void:
	var scale := portal_size / VIEWBOX
	var center := Vector2(portal_size, portal_size) * 0.5

	# Soft outer glow -- approximated with fading concentric circles (Godot's draw_circle has no
	# native radial-gradient fill the way the SVG mockup's <radialGradient> does).
	for i in range(6, 0, -1):
		var t := i / 6.0
		draw_circle(center, 118.0 * scale * t, Color(Color(COLOR_GLOW), 0.3 * (1.0 - t)))

	draw_arc(center, 92.0 * scale, 0, TAU, 64, Color(Color(COLOR_RING_OUTER), 0.5), 1.0, true)
	draw_arc(center, 78.0 * scale, 0, TAU, 64, Color(Color(COLOR_RING_MID), 0.5), 1.5, true)
	draw_arc(center, 62.0 * scale, 0, TAU, 64, Color(COLOR_RING_INNER), 1.5, true)

	# Rune-mark dots on the true diagonals.
	for offset in [Vector2(55, -55), Vector2(55, 55), Vector2(-55, -55), Vector2(-55, 55)]:
		draw_circle(center + offset * scale, 2.0 * scale, Color(Color(COLOR_DOT), 0.6))

	# Central diamond gem -- also the symbol for the Crest skill type per CLAUDE.md.
	var diamond := PackedVector2Array([
		center + Vector2(0, -32) * scale,
		center + Vector2(22, 0) * scale,
		center + Vector2(0, 32) * scale,
		center + Vector2(-22, 0) * scale,
	])
	draw_colored_polygon(diamond, Color(COLOR_DIAMOND_FILL))
	draw_polyline(PackedVector2Array([diamond[0], diamond[1], diamond[2], diamond[3], diamond[0]]), Color(COLOR_DIAMOND_STROKE), 1.5, true)
	draw_circle(center, 8.0 * scale, Color(COLOR_GEM))

	# 4 cardinal sigils, each in its own dark medallion.
	_draw_medallion(center + Vector2(0, -104) * scale, scale, _sigil_crimson())
	_draw_medallion(center + Vector2(104, 0) * scale, scale, _sigil_onyx())
	_draw_medallion(center + Vector2(0, 104) * scale, scale, _sigil_verdant())
	_draw_medallion(center + Vector2(-104, 0) * scale, scale, _sigil_azure())


func _draw_medallion(pos: Vector2, scale: float, sigil_lines: Array) -> void:
	draw_circle(pos, 16.0 * scale, Color(Color(COLOR_MEDALLION_FILL), 0.9))
	draw_arc(pos, 16.0 * scale, 0, TAU, 32, Color(Color(COLOR_MEDALLION_STROKE), 0.7), 1.0, true)
	for line: PackedVector2Array in sigil_lines:
		var shifted := PackedVector2Array()
		for p: Vector2 in line:
			shifted.append(pos + p * scale)
		draw_polyline(shifted, Color(Color(COLOR_SIGIL), 0.7), 1.0, true)


# North = Crimson: simple inverted-V spike.
func _sigil_crimson() -> Array:
	return [PackedVector2Array([Vector2(-12, 12), Vector2(0, -13), Vector2(12, 12)])]


# East = Onyx: open bracket-line, straight centered vertical middle.
func _sigil_onyx() -> Array:
	return [PackedVector2Array([Vector2(-8, -13), Vector2(6, -6), Vector2(6, 6), Vector2(-8, 13)])]


# South = Verdant: trunk + 2 branch offshoots.
func _sigil_verdant() -> Array:
	return [
		PackedVector2Array([Vector2(0, 13), Vector2(0, -13)]),
		PackedVector2Array([Vector2(0, -3), Vector2(9, -13)]),
		PackedVector2Array([Vector2(0, 5), Vector2(-9, -6)]),
	]


# West = Azure: two parallel curved waves (approximated with short polylines -- Godot's draw
# calls have no native bezier primitive; at this size two segmented lines still read as a wave).
func _sigil_azure() -> Array:
	return [
		PackedVector2Array([Vector2(-12, -5), Vector2(-6, -13), Vector2(0, -5), Vector2(6, 3), Vector2(12, -5)]),
		PackedVector2Array([Vector2(-12, 5), Vector2(-6, -3), Vector2(0, 5), Vector2(6, 13), Vector2(12, 5)]),
	]
