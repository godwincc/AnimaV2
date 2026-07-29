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
## stroke, brightest; "reachable" = the node's own solid type-color fill, clickable; "selected" = a
## reachable node the player just tapped, brighter gold ring, still clickable; "cleared" (NEW --
## previously lumped into "dim", now split out now that DelveMapNode.Cleared is real, wired data,
## see delve.gd's _render_map) = a node the player has actually walked through, desaturated + darkened
## but its own type color still recognizable underneath -- a "spent" look, distinct from "dim"; "dim"
## = not-yet-reachable-ahead only now (never actually visited), non-interactive; "legend" = a static,
## always-full-opacity, non-interactive display copy for delve.gd's own Legend panel (same fill/icon
## colors as the real map, just never dims and never responds to hover/click).
##
## FILL_COLORS/ICON_COLORS (NEW, solid-fill redesign) are the single source of truth for both this
## node's own real map rendering AND delve.gd's Legend badges -- kept here (not duplicated in
## delve.gd, unlike this codebase's usual "small mapping, duplicate per file" convention) because the
## Legend's whole purpose is to stay visually IDENTICAL to the real map, so a second, driftable copy
## would defeat the point. delve.gd references these via this script's own class_name.

## is_double_click mirrors the triggering InputEventMouseButton.double_click -- delve.gd's handler
## branches on it directly (single = select/deselect, double = enter) rather than this script trying
## to guess intent itself, since "what a click means" is delve.gd's concern, not this primitive's.
signal node_pressed(is_double_click: bool)

@export var node_type: String = "Combat":
	set(value):
		node_type = value
		if is_node_ready(): queue_redraw()

@export var visual_state: String = "dim": # "current" | "reachable" | "selected" | "cleared" | "dim" | "legend"
	set(value):
		visual_state = value
		if is_node_ready(): _apply_state()

## Overall view size in pixels -- configurable (not hardcoded) so delve.gd can reuse this exact
## script at a smaller size for Legend badges, rather than hand-rolling a second copy of the
## shape-drawing logic. Radii below are stored as ratios of this, not absolute pixels, so the visual
## proportions stay identical at any size.
@export var view_size: float = 52.0:
	set(value):
		view_size = value
		if is_node_ready():
			custom_minimum_size = Vector2(view_size, view_size)
			size = custom_minimum_size
			pivot_offset = size * 0.5
			queue_redraw()

const RADIUS_COMBAT_RATIO := 13.0 / 52.0
const RADIUS_ELITE_RATIO := 17.0 / 52.0
const RADIUS_BOSS_RATIO := 22.0 / 52.0
const RADIUS_SAFE_RATIO := 14.0 / 52.0 # Resource/Treasure/Shop/Reforge diamond half-width

const COLOR_GOLD_FILL := "e8a03a"
const COLOR_GOLD_STROKE := "f4dba8"
const COLOR_OUTLINE := "c9b89e"

# Solid per-type fill (NEW, live-testing feedback -- outline-only nodes read as too thin/dense at a
# glance). Combat is neutral gray (no locked color identity of its own, so it deliberately recedes
# relative to the others); Elite/Boss tie to their own locked room-background/vignette colors
# (#5a2e3a / #6b1a1a); Resource reuses the existing Wisp/sparkle amber (#e8a03a, same as
# COLOR_GOLD_FILL above -- deliberate, ties the "safe Wisp cache" node to the same accent color the
# rest of the UI already uses for Wisp); Treasure ties to its own locked room gem-tone purple; Shop
# is a warmer/browner amber than Resource so the two read as visually distinct at a glance; Reforge
# is bronze/copper, distinct from both.
const FILL_COLORS := {
	"Combat": "6e6a62",
	"Elite": "6e2e3a",
	"Boss": "6b1a1a",
	"Resource": "e8a03a",
	"Treasure": "9a4fc4",
	"Shop": "c9843a",
	"Reforge": "b87333",
}

# Per-type icon contrast -- light icon on the darker fills (Combat/Elite/Boss/Treasure), dark icon
# on the brighter ones (Resource/Shop/Reforge), matching the "current" node's own existing
# dark-icon-on-gold treatment (Resource's fill is the same gold, so it already needed this).
const ICON_COLORS := {
	"Combat": "f0e4d4",
	"Elite": "f0e4d4",
	"Boss": "f0e4d4",
	"Resource": "1a130e",
	"Treasure": "f0e4d4",
	"Shop": "1a130e",
	"Reforge": "1a130e",
}

const CIRCLE_TYPES := ["Combat", "Elite", "Boss"]
const INTERACTIVE_STATES := ["reachable", "selected"]
const FULL_OPACITY_STATES := ["current", "reachable", "selected", "legend"]

# "dim" (cleared-behind / not-yet-reachable-ahead) used to be a whole-node modulate.a=0.45 -- alpha
# blending a node against this screen's dark backdrop reads as washed-out/translucent rather than a
# clean "dimmed" look (real bug, live-testing feedback), and it was invisibly composing badly with
# anything drawn behind a node (map connector lines showing faintly through it). Fixed by keeping
# modulate.a always at 1.0 (fully opaque, always) and instead darkening the fill/stroke colors
# themselves in _draw() below. dim_color() is exposed so delve.gd can apply the identical darkening
# to the externally-set icon color for dim nodes (the icon is a sibling Control, not drawn by this
# script, so it needs the same treatment applied at its own call site).
const DIM_DARKEN_AMOUNT := 0.55

static func dim_color(c: Color) -> Color:
	return c.darkened(DIM_DARKEN_AMOUNT)

# "cleared" (NEW) needs to read as visually distinct from "dim" -- a node the player actually walked
# through vs. one that was simply never reachable yet are conceptually different things, and testing
# on the un-split "dim" treatment flagged both looking identical. Desaturate-then-darken (rather than
# dim_color's plain darken) keeps the type's own hue faintly recognizable while still clearly reading
# as "spent" -- first-pass tuning, easy to adjust either constant independently later.
const CLEARED_DESATURATE_AMOUNT := 0.5
const CLEARED_DARKEN_AMOUNT := 0.25

static func cleared_color(c: Color) -> Color:
	var gray := (c.r + c.g + c.b) / 3.0
	return c.lerp(Color(gray, gray, gray, c.a), CLEARED_DESATURATE_AMOUNT).darkened(CLEARED_DARKEN_AMOUNT)

# Hover feedback (NEW, live-testing feedback) -- restrained per the project's "no glow/blur" visual
# language: a clean scale-up + fill-brighten only, no added glow/blur effects. Restricted to
# INTERACTIVE_STATES (reachable/selected) -- scaling a node the player can't actually click would
# read as a false affordance signal.
const HOVER_SCALE := 1.15
const HOVER_BRIGHTEN := 0.22

# Pulsing selected-ring tuning (live-testing feedback found the old static thicker-stroke "selected"
# treatment too subtle to read as clear click feedback at a glance). An animated ring is unambiguous
# in a way a static color/width change isn't -- confirmed via a real click during testing that the
# click handler itself was already firing correctly (message area updated immediately); this is
# purely a visual-strength fix, not a functional one.
const PULSE_SPEED := 4.0
const PULSE_RING_BASE_OFFSET := 6.0
const PULSE_RING_RANGE := 5.0

var _pulse_time: float = 0.0
var _hovered: bool = false


func _ready() -> void:
	custom_minimum_size = Vector2(view_size, view_size)
	size = custom_minimum_size
	pivot_offset = size * 0.5 # scale from center (hover growth), keeps the overlaid icon centered
	mouse_filter = Control.MOUSE_FILTER_STOP
	mouse_entered.connect(_on_mouse_entered)
	mouse_exited.connect(_on_mouse_exited)
	_apply_state()


func _apply_state() -> void:
	modulate.a = 1.0 # always fully opaque -- "dim" darkens fill/stroke colors directly, see DIM_DARKEN_AMOUNT above
	mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND if visual_state in INTERACTIVE_STATES else Control.CURSOR_ARROW
	_pulse_time = 0.0
	set_process(visual_state == "selected")
	if visual_state not in INTERACTIVE_STATES:
		_set_hovered(false)
	queue_redraw()


func _on_mouse_entered() -> void:
	if visual_state not in INTERACTIVE_STATES: return
	_set_hovered(true)


func _on_mouse_exited() -> void:
	_set_hovered(false)


func _set_hovered(value: bool) -> void:
	_hovered = value
	scale = Vector2.ONE * HOVER_SCALE if _hovered else Vector2.ONE
	queue_redraw()


func _process(delta: float) -> void:
	_pulse_time += delta
	queue_redraw()


func _gui_input(event: InputEvent) -> void:
	if visual_state not in INTERACTIVE_STATES:
		return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		node_pressed.emit(event.double_click)


func _draw() -> void:
	var c := size * 0.5
	var is_circle := node_type in CIRCLE_TYPES
	var radius_ratio := RADIUS_SAFE_RATIO
	match node_type:
		"Combat": radius_ratio = RADIUS_COMBAT_RATIO
		"Elite": radius_ratio = RADIUS_ELITE_RATIO
		"Boss": radius_ratio = RADIUS_BOSS_RATIO
	var radius := radius_ratio * view_size

	var is_current := visual_state == "current"
	var is_selected := visual_state == "selected"

	# "current" stays the boldest possible highlight (gold fill, unchanged from before the solid-fill
	# redesign) -- overrides the type's own color entirely, since "where you are right now" needs to
	# stay maximally distinct regardless of node type. Every other state uses its real type color.
	var fill_color := Color(COLOR_GOLD_FILL) if is_current else Color(FILL_COLORS.get(node_type, "666666"))
	if _hovered:
		fill_color = fill_color.lightened(HOVER_BRIGHTEN)

	var stroke_color := Color(COLOR_GOLD_STROKE) if (is_current or is_selected) else Color(COLOR_OUTLINE)
	var stroke_width := 2.5 if (is_current or is_selected) else 1.5

	if visual_state == "dim":
		fill_color = dim_color(fill_color)
		stroke_color = dim_color(stroke_color)
	elif visual_state == "cleared":
		fill_color = cleared_color(fill_color)
		stroke_color = cleared_color(stroke_color)

	if is_circle:
		draw_circle(c, radius, fill_color)
		draw_arc(c, radius, 0.0, TAU, 48, stroke_color, stroke_width, true)
	else:
		var pts := PackedVector2Array([
			c + Vector2(0, -radius), c + Vector2(radius, 0), c + Vector2(0, radius), c + Vector2(-radius, 0),
		])
		draw_colored_polygon(pts, fill_color)
		draw_polyline(PackedVector2Array([pts[0], pts[1], pts[2], pts[3], pts[0]]), stroke_color, stroke_width, true)

	# Pulsing outer ring, selected only -- an animated ring reads as "you did something" in a way a
	# static stroke-width/color change doesn't, per an earlier session's own live-testing feedback.
	if is_selected:
		var pulse: float = (sin(_pulse_time * PULSE_SPEED) + 1.0) * 0.5 # 0..1
		var pulse_radius := radius + PULSE_RING_BASE_OFFSET + pulse * PULSE_RING_RANGE
		var pulse_alpha := 0.35 + pulse * 0.45
		draw_arc(c, pulse_radius, 0.0, TAU, 48, Color(COLOR_GOLD_STROKE, pulse_alpha), 2.5, true)
