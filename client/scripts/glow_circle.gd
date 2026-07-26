extends Control
class_name GlowCircle

## Soft radial glow behind a centerpiece icon (Resource/Treasure node screens, per their locked
## mockups' `radial-gradient(circle, rgba(...,0.35) 0%, rgba(...,0.05) 60%, transparent 100%)`
## treatment) -- approximated via fading concentric circles, same technique delve_portal.gd's own
## outer glow already uses (Godot's draw_circle has no native radial-gradient fill the way CSS's
## radial-gradient does). Shared here (not duplicated per screen) the same way IconGlyph is already
## a shared drawing primitive rather than being reimplemented per caller.

@export var glow_color: Color = Color.WHITE:
	set(value):
		glow_color = value
		if is_node_ready(): queue_redraw()

@export var glow_radius: float = 60.0:
	set(value):
		glow_radius = value
		if is_node_ready(): queue_redraw()


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	queue_redraw()


func _draw() -> void:
	var center := size * 0.5
	for i in range(6, 0, -1):
		var t := i / 6.0
		draw_circle(center, glow_radius * t, Color(glow_color, 0.35 * (1.0 - t)))
