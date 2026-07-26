extends Control
class_name DelveMapNode

## A single Delve map node's procedurally-drawn shape -- circle sized by threat for Combat/Elite/
## Boss, diamond outline for the safe types (Resource/Treasure/Shop/Reforge), per CLAUDE.md's map
## icon "shape encodes risk" rule. This is deliberately a separate primitive from IconGlyph (which
## draws the type icon ON TOP as a sibling/child, composed by delve.gd) -- same "primitive + caller
## composes" split sanctum.gd/hub.gd already use for part-row icons, just applied to a bigger shape.
##
## visual_state drives both the shape's fill/stroke AND the whole node's opacity (via modulate, so
## an overlaid IconGlyph child dims identically with zero extra wiring): "current" = gold fill +
## stroke, brightest; "reachable" = plain bright outline, clickable; "selected" = a reachable node
## the player just tapped, brighter gold ring, still clickable; "dim" = cleared-behind AND
## not-yet-reachable-ahead, both intentionally sharing one 45%-opacity treatment for now (per this
## task's own instruction -- easy to visually split later if testing shows it's confusing) and
## non-interactive.

signal node_pressed

@export var node_type: String = "Combat":
	set(value):
		node_type = value
		if is_node_ready(): queue_redraw()

@export var visual_state: String = "dim": # "current" | "reachable" | "selected" | "dim"
	set(value):
		visual_state = value
		if is_node_ready(): _apply_state()

const RADIUS_COMBAT := 13.0
const RADIUS_ELITE := 17.0
const RADIUS_BOSS := 22.0
const RADIUS_SAFE := 14.0 # Resource/Treasure/Shop/Reforge diamond half-width

const COLOR_GOLD_FILL := "e8a03a"
const COLOR_GOLD_STROKE := "f4dba8"
const COLOR_OUTLINE := "c9b89e"

const CIRCLE_TYPES := ["Combat", "Elite", "Boss"]
const INTERACTIVE_STATES := ["reachable", "selected"]

# Pulsing selected-ring tuning (NEW -- live-testing feedback found the old static thicker-stroke
# "selected" treatment too subtle to read as clear click feedback at a glance). An animated ring
# is unambiguous in a way a static color/width change isn't -- confirmed via a real click during
# testing that the click handler itself was already firing correctly (message area updated
# immediately); this is purely a visual-strength fix, not a functional one.
const PULSE_SPEED := 4.0
const PULSE_RING_BASE_OFFSET := 6.0
const PULSE_RING_RANGE := 5.0

var _pulse_time: float = 0.0


func _ready() -> void:
	custom_minimum_size = Vector2(52, 52)
	size = custom_minimum_size
	mouse_filter = Control.MOUSE_FILTER_STOP
	_apply_state()


func _apply_state() -> void:
	modulate.a = 1.0 if visual_state in ["current", "reachable", "selected"] else 0.45
	mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND if visual_state in INTERACTIVE_STATES else Control.CURSOR_ARROW
	_pulse_time = 0.0
	set_process(visual_state == "selected")
	queue_redraw()


func _process(delta: float) -> void:
	_pulse_time += delta
	queue_redraw()


func _gui_input(event: InputEvent) -> void:
	if visual_state not in INTERACTIVE_STATES:
		return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		node_pressed.emit()


func _draw() -> void:
	var c := size * 0.5
	var is_circle := node_type in CIRCLE_TYPES
	var radius := RADIUS_SAFE
	match node_type:
		"Combat": radius = RADIUS_COMBAT
		"Elite": radius = RADIUS_ELITE
		"Boss": radius = RADIUS_BOSS

	var is_current := visual_state == "current"
	var is_selected := visual_state == "selected"
	var fill_color := Color(COLOR_GOLD_FILL) if is_current else Color(0, 0, 0, 0)
	var stroke_color := Color(COLOR_GOLD_STROKE) if (is_current or is_selected) else Color(COLOR_OUTLINE)
	var stroke_width := 2.5 if (is_current or is_selected) else 1.5

	if is_circle:
		if fill_color.a > 0.0:
			draw_circle(c, radius, fill_color)
		draw_arc(c, radius, 0.0, TAU, 48, stroke_color, stroke_width, true)
	else:
		var pts := PackedVector2Array([
			c + Vector2(0, -radius), c + Vector2(radius, 0), c + Vector2(0, radius), c + Vector2(-radius, 0),
		])
		if fill_color.a > 0.0:
			draw_colored_polygon(pts, fill_color)
		draw_polyline(PackedVector2Array([pts[0], pts[1], pts[2], pts[3], pts[0]]), stroke_color, stroke_width, true)

	# Pulsing outer ring, selected only -- an animated ring reads as "you did something" in a way a
	# static stroke-width/color change doesn't, per this session's own live-testing feedback.
	if is_selected:
		var pulse: float = (sin(_pulse_time * PULSE_SPEED) + 1.0) * 0.5 # 0..1
		var pulse_radius := radius + PULSE_RING_BASE_OFFSET + pulse * PULSE_RING_RANGE
		var pulse_alpha := 0.35 + pulse * 0.45
		draw_arc(c, pulse_radius, 0.0, TAU, 48, Color(COLOR_GOLD_STROKE, pulse_alpha), 2.5, true)
