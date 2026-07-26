extends Control
class_name IconGlyph

## Small procedurally-drawn stand-in icons -- this project has no icon font asset (Tabler icons,
## used by the HTML mocks, aren't available in Godot), so each of the handful of icon kinds the
## Hub screen needs (home/flask/archive/sparkle -- destination cards + Wisp; sword/shield/heart/
## bolt/diamond -- the Skill-type icon set from CLAUDE.md) is drawn as a simple vector shape
## instead. Deliberately plain (a handful of primitives each) -- these exist to carry the right
## information (which icon, correct color) at Hub-placeholder fidelity, not as final art.

@export var icon_kind: String = "sword":
	set(value):
		icon_kind = value
		if is_node_ready():
			queue_redraw()

@export var icon_color: Color = Color.WHITE:
	set(value):
		icon_color = value
		if is_node_ready():
			queue_redraw()

@export var icon_size: float = 20.0:
	set(value):
		icon_size = value
		if is_node_ready():
			custom_minimum_size = Vector2(icon_size, icon_size)
			queue_redraw()


func _ready() -> void:
	custom_minimum_size = Vector2(icon_size, icon_size)
	queue_redraw()


func _draw() -> void:
	var s := icon_size
	var c := s * 0.5

	match icon_kind:
		"sword":
			draw_line(Vector2(s * 0.2, s * 0.85), Vector2(s * 0.8, s * 0.15), icon_color, s * 0.09, true)
			draw_line(Vector2(s * 0.32, s * 0.68), Vector2(s * 0.5, s * 0.86), icon_color, s * 0.09, true)
			draw_line(Vector2(s * 0.62, s * 0.28), Vector2(s * 0.78, s * 0.42), icon_color, s * 0.09, true)
		"shield":
			var pts := PackedVector2Array([
				Vector2(c, s * 0.08), Vector2(s * 0.85, s * 0.22), Vector2(s * 0.85, s * 0.5),
				Vector2(c, s * 0.92), Vector2(s * 0.15, s * 0.5), Vector2(s * 0.15, s * 0.22),
			])
			draw_colored_polygon(pts, icon_color)
		"heart":
			var pts := PackedVector2Array([
				Vector2(c, s * 0.92),
				Vector2(s * 0.08, s * 0.45), Vector2(s * 0.08, s * 0.2), Vector2(s * 0.28, s * 0.08),
				Vector2(c, s * 0.28),
				Vector2(s * 0.72, s * 0.08), Vector2(s * 0.92, s * 0.2), Vector2(s * 0.92, s * 0.45),
			])
			draw_colored_polygon(pts, icon_color)
		"bolt":
			var pts := PackedVector2Array([
				Vector2(s * 0.58, s * 0.05), Vector2(s * 0.2, s * 0.58), Vector2(s * 0.46, s * 0.58),
				Vector2(s * 0.38, s * 0.95), Vector2(s * 0.8, s * 0.38), Vector2(s * 0.52, s * 0.38),
			])
			draw_colored_polygon(pts, icon_color)
		"diamond":
			var pts := PackedVector2Array([Vector2(c, s * 0.08), Vector2(s * 0.92, c), Vector2(c, s * 0.92), Vector2(s * 0.08, c)])
			draw_colored_polygon(pts, icon_color)
		"home":
			var roof := PackedVector2Array([Vector2(c, s * 0.08), Vector2(s * 0.9, s * 0.48), Vector2(s * 0.1, s * 0.48)])
			draw_colored_polygon(roof, icon_color)
			draw_rect(Rect2(s * 0.24, s * 0.48, s * 0.52, s * 0.42), icon_color)
		"flask":
			# Neck drawn as its own thin rect (not folded into one polygon with the body) so it
			# reads as a distinct narrow neck at small sizes, rather than blending into the body
			# silhouette and looking like a plain triangle.
			draw_rect(Rect2(s * 0.42, s * 0.06, s * 0.16, s * 0.32), icon_color)
			var body := PackedVector2Array([
				Vector2(s * 0.42, s * 0.32), Vector2(s * 0.58, s * 0.32),
				Vector2(s * 0.88, s * 0.88), Vector2(s * 0.12, s * 0.88),
			])
			draw_colored_polygon(body, icon_color)
		"archive":
			draw_rect(Rect2(s * 0.1, s * 0.15, s * 0.8, s * 0.7), icon_color, false, s * 0.08)
			draw_line(Vector2(s * 0.22, s * 0.4), Vector2(s * 0.78, s * 0.4), icon_color, s * 0.06)
			draw_line(Vector2(s * 0.22, s * 0.6), Vector2(s * 0.78, s * 0.6), icon_color, s * 0.06)
		"sparkle":
			var outer := s * 0.48
			var inner := s * 0.14
			var pts := PackedVector2Array()
			for i in range(8):
				var r := outer if i % 2 == 0 else inner
				var angle := i * PI / 4.0
				pts.append(Vector2(c, c) + Vector2(cos(angle), sin(angle)) * r)
			draw_colored_polygon(pts, icon_color)
		"hexagon":
			var pts := PackedVector2Array()
			for i in range(6):
				var angle := i * PI / 3.0 - PI / 2.0
				pts.append(Vector2(c, c) + Vector2(cos(angle), sin(angle)) * (s * 0.46))
			draw_colored_polygon(pts, icon_color)
		"dot":
			# Anima Profile's Threads section dot-accent -- a plain filled circle, deliberately
			# simpler than the full sword/heart/shield/bolt/diamond icon shapes Sanctum/Hub use for
			# the same skill-type coloring, per CLAUDE.md's original "dot-accent" screen-4 language.
			draw_circle(Vector2(c, c), s * 0.4, icon_color)
		"pencil":
			# Diagonal body (thick line) + a small triangular writing tip -- same "a handful of
			# primitives, placeholder-tier fidelity" spirit as every other icon here.
			draw_line(Vector2(s * 0.75, s * 0.25), Vector2(s * 0.3, s * 0.7), icon_color, s * 0.16, true)
			var tip := PackedVector2Array([Vector2(s * 0.3, s * 0.7), Vector2(s * 0.22, s * 0.9), Vector2(s * 0.42, s * 0.78)])
			draw_colored_polygon(tip, icon_color)
		# Collection screen's Artifact icons (new) -- same placeholder-tier fidelity as every icon
		# above: a handful of primitives, parameterized by icon_color/icon_size, no attempt at
		# final art.
		"flame":
			var flame_pts := PackedVector2Array([
				Vector2(c, s * 0.05),
				Vector2(s * 0.7, s * 0.32), Vector2(s * 0.6, s * 0.52), Vector2(s * 0.8, s * 0.85),
				Vector2(c, s * 0.95),
				Vector2(s * 0.2, s * 0.85), Vector2(s * 0.4, s * 0.52), Vector2(s * 0.3, s * 0.32),
			])
			draw_colored_polygon(flame_pts, icon_color)
		"bell":
			# Outline only, deliberately no clapper -- a clapper would visually suggest the bell can
			# be "rung"/interacted with, but Silent Chime's effect is fully automatic (no player
			# action ever triggers it).
			draw_line(Vector2(c, s * 0.05), Vector2(c, s * 0.13), icon_color, s * 0.05, true)
			draw_arc(Vector2(c, s * 0.28), s * 0.15, PI, 2.0 * PI, 16, icon_color, s * 0.07, true)
			var bell_body := PackedVector2Array([
				Vector2(s * 0.38, s * 0.28), Vector2(s * 0.62, s * 0.28),
				Vector2(s * 0.78, s * 0.78), Vector2(s * 0.22, s * 0.78), Vector2(s * 0.38, s * 0.28),
			])
			draw_polyline(bell_body, icon_color, s * 0.07, true)
			draw_line(Vector2(s * 0.16, s * 0.78), Vector2(s * 0.84, s * 0.78), icon_color, s * 0.06, true)
		"diagonal_lines":
			draw_line(Vector2(s * 0.12, s * 0.85), Vector2(s * 0.42, s * 0.15), icon_color, s * 0.1, true)
			draw_line(Vector2(s * 0.4, s * 0.85), Vector2(s * 0.7, s * 0.15), icon_color, s * 0.1, true)
			draw_line(Vector2(s * 0.68, s * 0.85), Vector2(s * 0.98, s * 0.15), icon_color, s * 0.1, true)
		"coin_star":
			draw_arc(Vector2(c, c), s * 0.42, 0.0, TAU, 32, icon_color, s * 0.07, true)
			var star_pts := PackedVector2Array()
			for i in range(8):
				var star_r: float = (s * 0.16) if i % 2 == 0 else (s * 0.06)
				var star_angle: float = i * PI / 4.0
				star_pts.append(Vector2(c, c) + Vector2(cos(star_angle), sin(star_angle)) * star_r)
			draw_colored_polygon(star_pts, icon_color)
		"tooth":
			# Not a stock Tabler shape (ti-tooth doesn't exist) -- a sharp tapering polygon, same
			# rationale CLAUDE.md's own Withering Fang icon note already gives.
			var tooth_pts := PackedVector2Array([
				Vector2(s * 0.28, s * 0.14), Vector2(s * 0.72, s * 0.14),
				Vector2(s * 0.62, s * 0.55), Vector2(c, s * 0.92), Vector2(s * 0.38, s * 0.55),
			])
			draw_colored_polygon(tooth_pts, icon_color)
		"magnifying_glass":
			# No plus sign -- deliberately just the glass + handle (per the Artifact Icons spec).
			draw_arc(Vector2(s * 0.42, s * 0.42), s * 0.28, 0.0, TAU, 24, icon_color, s * 0.08, true)
			draw_line(Vector2(s * 0.62, s * 0.62), Vector2(s * 0.9, s * 0.9), icon_color, s * 0.1, true)
		"asterisk":
			for i in range(3):
				var ast_angle: float = i * PI / 3.0
				var ast_dir := Vector2(cos(ast_angle), sin(ast_angle))
				draw_line(Vector2(c, c) - ast_dir * s * 0.42, Vector2(c, c) + ast_dir * s * 0.42, icon_color, s * 0.08, true)
		"sun":
			draw_circle(Vector2(c, c), s * 0.22, icon_color)
			for i in range(8):
				var ray_angle: float = i * PI / 4.0
				var ray_dir := Vector2(cos(ray_angle), sin(ray_angle))
				draw_line(Vector2(c, c) + ray_dir * s * 0.3, Vector2(c, c) + ray_dir * s * 0.46, icon_color, s * 0.07, true)
		"leaf":
			var leaf_pts := PackedVector2Array([
				Vector2(c, s * 0.08),
				Vector2(s * 0.68, s * 0.22), Vector2(s * 0.85, s * 0.45), Vector2(s * 0.78, s * 0.68), Vector2(c, s * 0.92),
				Vector2(s * 0.22, s * 0.68), Vector2(s * 0.15, s * 0.45), Vector2(s * 0.32, s * 0.22),
			])
			draw_colored_polygon(leaf_pts, icon_color)
			draw_line(Vector2(c, s * 0.2), Vector2(c, s * 0.82), icon_color.darkened(0.35), s * 0.05, true)
		"recycle":
			# 3 curved arrow segments arranged in a loop -- placeholder-tier stand-in for the real
			# ti-recycle glyph (no icon font asset available, same reason every other kind here is
			# hand-drawn instead of a real icon).
			for i in range(3):
				var loop_base: float = i * TAU / 3.0 - PI / 2.0
				var loop_end: float = loop_base + (TAU / 3.0) * 0.7
				draw_arc(Vector2(c, c), s * 0.34, loop_base, loop_end, 12, icon_color, s * 0.08, true)
				var arrow_dir := Vector2(cos(loop_end), sin(loop_end))
				var arrow_tangent := Vector2(-sin(loop_end), cos(loop_end))
				var arrow_tip := Vector2(c, c) + arrow_dir * s * 0.34
				var recycle_arrow := PackedVector2Array([
					arrow_tip + arrow_tangent * s * 0.12,
					arrow_tip + arrow_dir * s * 0.14,
					arrow_tip - arrow_tangent * s * 0.12,
				])
				draw_colored_polygon(recycle_arrow, icon_color)
		# Delve map screen's node-type icons (new) -- same placeholder-tier fidelity as every icon
		# above. Combat reuses the existing "sword" kind above rather than adding a duplicate.
		"skull":
			draw_circle(Vector2(c, s * 0.38), s * 0.32, icon_color)
			var jaw := PackedVector2Array([
				Vector2(s * 0.28, s * 0.5), Vector2(s * 0.72, s * 0.5),
				Vector2(s * 0.64, s * 0.82), Vector2(s * 0.36, s * 0.82),
			])
			draw_colored_polygon(jaw, icon_color)
			draw_circle(Vector2(s * 0.38, s * 0.36), s * 0.08, icon_color.darkened(0.65))
			draw_circle(Vector2(s * 0.62, s * 0.36), s * 0.08, icon_color.darkened(0.65))
		"crown":
			var crown_pts := PackedVector2Array([
				Vector2(s * 0.12, s * 0.78), Vector2(s * 0.12, s * 0.35), Vector2(s * 0.3, s * 0.52),
				Vector2(c, s * 0.18), Vector2(s * 0.7, s * 0.52), Vector2(s * 0.88, s * 0.35), Vector2(s * 0.88, s * 0.78),
			])
			draw_colored_polygon(crown_pts, icon_color)
			draw_rect(Rect2(s * 0.12, s * 0.78, s * 0.76, s * 0.1), icon_color)
		"coin":
			# Plain ring, deliberately distinct from Collection's "coin_star" (no star inside) --
			# the map's Resource node icon is a bare coin per CLAUDE.md's map icon list.
			draw_arc(Vector2(c, c), s * 0.4, 0.0, TAU, 32, icon_color, s * 0.07, true)
			draw_arc(Vector2(c, c), s * 0.22, 0.0, TAU, 24, icon_color, s * 0.05, true)
		"gift":
			draw_rect(Rect2(s * 0.15, s * 0.42, s * 0.7, s * 0.46), icon_color, false, s * 0.07)
			draw_rect(Rect2(s * 0.1, s * 0.3, s * 0.8, s * 0.14), icon_color)
			draw_rect(Rect2(s * 0.44, s * 0.3, s * 0.12, s * 0.58), icon_color)
			draw_circle(Vector2(s * 0.38, s * 0.22), s * 0.1, icon_color)
			draw_circle(Vector2(s * 0.62, s * 0.22), s * 0.1, icon_color)
		"building_store":
			draw_rect(Rect2(s * 0.14, s * 0.42, s * 0.72, s * 0.46), icon_color, false, s * 0.07)
			var awning := PackedVector2Array([Vector2(s * 0.08, s * 0.42), Vector2(c, s * 0.16), Vector2(s * 0.92, s * 0.42)])
			draw_polyline(awning, icon_color, s * 0.07, true)
			draw_rect(Rect2(s * 0.42, s * 0.62, s * 0.16, s * 0.26), icon_color)
		"hammer":
			var handle_start := Vector2(s * 0.28, s * 0.88)
			var handle_end := Vector2(s * 0.6, s * 0.5)
			draw_line(handle_start, handle_end, icon_color, s * 0.09, true)
			var dir := (handle_end - handle_start).normalized()
			var perp := Vector2(-dir.y, dir.x)
			var head_center := handle_end + dir * s * 0.08
			var half_len := s * 0.22
			var half_wid := s * 0.09
			var head_pts := PackedVector2Array([
				head_center + dir * half_wid + perp * half_len,
				head_center + dir * half_wid - perp * half_len,
				head_center - dir * half_wid - perp * half_len,
				head_center - dir * half_wid + perp * half_len,
			])
			draw_colored_polygon(head_pts, icon_color)
		"shard":
			# A small faceted gem/shard (NEW, this session) -- deliberately distinct from the plain
			# "diamond" outline/fill above, which stays reserved for Crest skill chips (the more
			# established convention). Pentagon gem body + 3 darker facet lines for a cut-gem look,
			# instead of diamond's flat single-fill rhombus -- same placeholder-tier fidelity as
			# skull/crown/coin/gift/building_store/hammer above.
			var shard_top_l := Vector2(s * 0.36, s * 0.16)
			var shard_top_r := Vector2(s * 0.64, s * 0.16)
			var shard_side_l := Vector2(s * 0.14, s * 0.42)
			var shard_side_r := Vector2(s * 0.86, s * 0.42)
			var shard_bottom := Vector2(c, s * 0.92)
			var shard_pts := PackedVector2Array([shard_top_l, shard_top_r, shard_side_r, shard_bottom, shard_side_l])
			draw_colored_polygon(shard_pts, icon_color)
			draw_line(shard_side_l, shard_side_r, icon_color.darkened(0.35), s * 0.04, true)
			draw_line(shard_top_l, shard_bottom, icon_color.darkened(0.35), s * 0.04, true)
			draw_line(shard_top_r, shard_bottom, icon_color.darkened(0.35), s * 0.04, true)
		_:
			draw_circle(Vector2(c, c), s * 0.4, icon_color)
