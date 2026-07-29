extends Control
class_name DelveMapCanvas

## Backdrop for the Delve map's connector lines -- a plain Control drawing thin lines behind
## whatever DelveMapNode children delve.gd adds on top of it (a Control's children always draw
## after its own _draw(), so lines sit behind nodes for free, no z-index juggling needed). Segments
## are Dictionary{from: Vector2, to: Vector2, is_path: bool} in this canvas's own local coordinate
## space, computed by delve.gd from the real DelveStatus.AllNodes adjacency (NextRefs) it already has
## to walk anyway to place the node views themselves -- is_path (NEW) marks an edge as part of the
## real path the player has walked (both endpoints cleared-or-current), see delve.gd's _render_map
## for how that's derived.

signal background_pressed

var _segments: Array = []

const DEFAULT_LINE_COLOR := "6a5a48"
const PATH_LINE_COLOR := "e8a03a" # same gold as the "current" node fill / selected-ring stroke


func set_segments(segments: Array) -> void:
	_segments = segments
	queue_redraw()


func _draw() -> void:
	# Default connectors first, path-taken lines drawn after (on top) so a walked path reads clearly
	# even where it happens to run alongside an unrelated default connector.
	for seg: Dictionary in _segments:
		if not bool(seg.get("is_path", false)):
			draw_line(seg["from"], seg["to"], Color(Color(DEFAULT_LINE_COLOR), 0.55), 1.5, true)
	for seg: Dictionary in _segments:
		if bool(seg.get("is_path", false)):
			draw_line(seg["from"], seg["to"], Color(PATH_LINE_COLOR), 3.0, true)


## Fires only for clicks that land on genuinely empty canvas area -- any DelveMapNode child (even a
## non-interactive dim/cleared one) has its own mouse_filter = STOP, which already swallows a click
## before it can reach this Control underneath, so this never double-fires alongside a node's own
## node_pressed. delve.gd uses this to implement "click elsewhere on the map deselects."
func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		background_pressed.emit()
