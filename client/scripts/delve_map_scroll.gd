extends ScrollContainer
class_name DelveMapScroll

## Wheel-to-zoom instead of wheel-to-scroll for the Delve map, plus touch-pinch zoom -- "Zoom (mouse
## wheel + touch pinch) is a Godot-side detail, no visible UI control" per this task's own brief.
## Overriding _gui_input on a ScrollContainer subclass is the standard Godot technique for taking
## over its default mouse-wheel scroll behavior (the engine's own wheel-scroll handling is reached
## through the same _gui_input virtual a script overrides, so defining one here without chaining to
## it replaces that behavior rather than running alongside it) -- horizontal panning still works via
## the container's own visible horizontal scrollbar drag or a touch drag-scroll gesture, neither of
## which goes through this override. Flagged as the one piece of this screen not verified in a live
## Godot instance during this pass (see the task's own reminder to verify against a real Web build);
## if a real screenshot/interaction pass ever shows drag-scroll got suppressed too, the fix is to
## reintroduce it explicitly here rather than assume it still works.

signal zoom_requested(factor: float)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP:
			zoom_requested.emit(1.1)
			accept_event()
			return
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			zoom_requested.emit(0.9)
			accept_event()
			return
	if event is InputEventMagnifyGesture:
		zoom_requested.emit(event.factor)
		accept_event()
