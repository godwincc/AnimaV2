extends Control
class_name DelveMapCanvas

## Backdrop for the Delve map's connector lines -- a plain Control drawing thin lines behind
## whatever DelveMapNode children delve.gd adds on top of it (a Control's children always draw
## after its own _draw(), so lines sit behind nodes for free, no z-index juggling needed). Segments
## are plain [Vector2, Vector2] pairs in this canvas's own local coordinate space, computed by
## delve.gd from the real DelveStatus.AllNodes adjacency (NextRefs) it already has to walk anyway
## to place the node views themselves.

var _segments: Array = []


func set_segments(segments: Array) -> void:
	_segments = segments
	queue_redraw()


func _draw() -> void:
	for seg: Array in _segments:
		draw_line(seg[0], seg[1], Color(Color("6a5a48"), 0.55), 1.5, true)
