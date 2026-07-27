extends Control

## The real Delve map screen (replaces Hub's old "The Delve screen isn't built yet." status
## message). Resource, Treasure, Reforge, Shop, and (NEW, Combat Phase 1) Combat/Elite/Boss node
## screens are now all real (see resource_node.gd/treasure_node.gd/reforge_node.gd/shop_node.gd/
## combat.gd). Combat Phase 1 is layout + real-data rendering only -- no player interaction yet, no
## real way to win/lose a fight, see combat.gd's own top-of-file comment for full scope.
## The DEV ONLY panel below still exists for direct/instant testing convenience (jumping straight to
## a specific node type rather than walking real map traversal to find one) -- see that panel's own
## comment for how it works.
##
## NODE ENCOUNTER ARCHITECTURE: resource_node.tscn/treasure_node.tscn are instanced as CHILD panels
## of this scene's NodeEncounterOverlay, sharing THIS script's own _hub connection, rather than
## being reached via change_scene_to_file like every top-level screen (Hub/Sanctum/etc.) is. See
## resource_node.gd's own top-of-file comment for the full reasoning: a real scene change would
## tear down the connection that owns the in-progress DelveRun (ActiveDelveRun is in-memory,
## per-connection, no server-side resume), so returning to a FRESH delve.tscn afterward would lose
## the run or force a brand-new StartDelve(), wiping map progress. Same class of deviation as the
## Hub-portal fix from the map-screen session, applied one level deeper.
##
## ENTRY POINT DEVIATION (flagged explicitly): the task asked for hub.gd's portal press handler to
## call StartDelve() itself and then navigate here. That would break immediately given this
## codebase's locked "one HubConnection per scene" pattern (every real screen -- hub/sanctum/
## collection/anima_profile/starter_reveal -- opens its own fresh HubConnection in its own _ready(),
## per CLAUDE.md's own documented gap that a dropped/replaced connection gets a brand-new in-memory
## PlayerSession with ActiveDelveRun lost): change_scene_to_file() tears down hub.tscn's whole scene
## tree, including its HubConnection node, before delve.tscn's _ready() ever runs, so a DelveRun
## started on hub's connection would already be orphaned (garbage-collected with the old
## PlayerSession) by the time this screen could read it back. Instead, hub.gd's portal press just
## navigates here (identical one-liner to its Sanctum/Weaving/Collection cards), and THIS script
## calls StartDelve() itself once its own connection is live -- the only design that actually works
## under this codebase's existing architecture.
##
## Server contracts audited directly from Anima.Server/Hubs/GameHub.cs and GameHubModels.cs before
## writing this (not assumed): DelveStatus.AllNodes/Artifacts and AnimaPartSummary.Description/
## AppliedAugments are NEW fields added this session to close real gaps -- the map screen's own
## design (whole-graph rendering with cleared/dim states, inline held-Artifact descriptions, an
## augment badge + hover text on skill chips) had no wire data to read before this. See
## GameHubModels.cs's own comments on DelveMapNode/HeldArtifactSummary/AnimaPartSummary for why.
##
## GetDelveStatus() (NEW this session) IS now called, exactly once: after a Resource/Treasure
## encounter resolves. CollectResourceNode/ClaimTreasureNode don't return a DelveStatus themselves
## (they return their own reward-shaped result), so this is the one real gap StartDelve/MoveToNode's
## own return values don't already fill -- confirming last session's own note that it'd be added
## "if a real need for one ever shows up."
##
## DEV ONLY force-node panel (flagged loudly in the UI itself, not just here): calls the REAL,
## unmodified MoveToNode repeatedly, walking the REAL map graph (BFS over AllNodes.NextRefs) from
## the current position to the nearest node of the requested type, one real hop at a time -- it does
## NOT bypass the server's own AvailableNodes check (MoveToNode already rejects anything not
## adjacent to CurrentNode, confirmed by reading DelveRun.TryMoveTo; nothing server-side was changed
## to support this). Still needed even now that Combat Phase 1 exists: Floor 1 is fixed Combat, but
## Combat Phase 1 has no SubmitAction/win-condition wiring at all (see combat.gd's own top-of-file
## comment), so normal play still can't actually WIN that fight and advance past it -- strip this
## panel out once Combat's real interactive loop (Phase 2+) ships and normal traversal covers this
## testing need on its own.

const SERVER_WS_URL := "ws://localhost:5143/hubs/game"
const LOGIN_SCENE := "res://scenes/login.tscn"
const HUB_SCENE := "res://scenes/hub.tscn"

# Warm sanctuary/workshop theme, matching every other real screen's own palette exactly.
const COLOR_GRADIENT_TOP := "4a3a2e"
const COLOR_GRADIENT_MID := "2b2018"
const COLOR_GRADIENT_BOTTOM := "1a130e"
const COLOR_CARD_BG := "1e1610"
const COLOR_CARD_BORDER := "c9b89e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_TEXT_CREAM_DIM := "e8cf9a"
const COLOR_TEXT_MUTED := "a89680"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_ERROR := "e2554a"

## Split into two independent strips (live-testing feedback) -- the map-confirm bar and this info
## bar used to share one message area far from the map, so a node selection and a skill hover
## silently overwrote each other. Map-confirm stays idle-EMPTY; this one gets its own idle hint
## since it's the one that still needs one. GENERALIZED (this session) beyond skill chips: Artifact
## sidebar rows now tap/hover into the exact same strip, so its name/state are no longer
## skill-specific -- one consistent "tap something, read about it down here" pattern screen-wide.
const IDLE_INFO_MESSAGE := "Tap a skill chip or artifact for details."

# Skill-type icon color coding, identical to hub.gd/sanctum.gd's own ICON_COLORS (same locked Icon
# Conventions) -- used for the Team panel's skill chips, NOT the map's own node-type icons below.
const SKILL_ICON_COLORS := {
	"sword": "e0736a",
	"shield": "7aa8d8",
	"heart": "6cb87c",
	"bolt": "e8b95a",
	"diamond": "e8a03a",
}

# Map node-type icons, per CLAUDE.md's Delve screen icon list. Combat reuses the existing "sword"
# kind (no separate map-specific sword needed).
const NODE_ICONS := {
	"Combat": "sword",
	"Elite": "skull",
	"Resource": "coin",
	"Treasure": "gift",
	"Shop": "building_store",
	"Reforge": "hammer",
	"Boss": "crown",
}

const RESOURCE_ORDER := ["Wisp", "EchoShard", "VesselShard"]
const RESOURCE_ICONS := {"Wisp": "sparkle", "EchoShard": "shard", "VesselShard": "hexagon"}
const RESOURCE_LABELS := {"Wisp": "Wisp", "EchoShard": "Echo Shards", "VesselShard": "Vessel Shards"}

const MAX_ARTIFACTS := 3

# Same 12-entry icon mapping as collection.gd -- neither icon kind nor display order exists on the
# wire (HeldArtifactSummary is just Name/Description), so this is the client's own static table,
# duplicated rather than shared per this codebase's existing per-screen convention (see e.g.
# sanctum.gd/hub.gd both keeping their own ICON_COLORS copies).
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

# Real Aspect sprites / hybrid fallback tints -- identical mapping to hub.gd's own team row.
const ASPECT_SPRITES := {
	"Crimson": "res://assets/aspects/crimson.png",
	"Onyx": "res://assets/aspects/onyx.png",
	"Verdant": "res://assets/aspects/verdant.png",
	"Azure": "res://assets/aspects/azure.png",
}
const HYBRID_FALLBACK_TINTS := {
	"Vulcan": "6a3a52",
	"Mirage": "3a5a6a",
}

# Map layout constants -- COLUMN_COUNT mirrors Anima.Core.Map.DungeonMap's real Width=7 (confirmed
# by reading the code, not assumed); map pixel extents are computed dynamically from the real node
# data in _render_map rather than a hardcoded floor count, so Boss (FloorIndex 15, outside the
# 7-wide grid proper per MapNode's own "Boss reuses this type with FloorIndex 15 and Column -1"
# comment) needs no special-casing here.
const COLUMN_COUNT := 7
const FLOOR_SPACING_X := 90.0
const COLUMN_SPACING_Y := 70.0
const MAP_MARGIN := 40.0
const NODE_VIEW_SIZE := 52.0

const ZOOM_MIN := 0.5
const ZOOM_MAX := 2.0

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")
const DELVE_MAP_NODE_SCRIPT := preload("res://scripts/delve_map_node.gd")
const RESOURCE_NODE_SCENE := preload("res://scenes/resource_node.tscn")
const TREASURE_NODE_SCENE := preload("res://scenes/treasure_node.tscn")
const COMBAT_SCENE := preload("res://scenes/combat.tscn")

@onready var _background: TextureRect = $Background
@onready var _dev_force_resource_button: Button = $Margin/Content/DevPanel/DevMargin/DevRow/DevForceResourceButton
@onready var _dev_force_treasure_button: Button = $Margin/Content/DevPanel/DevMargin/DevRow/DevForceTreasureButton
@onready var _dev_force_combat_button: Button = $Margin/Content/DevPanel/DevMargin/DevRow/DevForceCombatButton
@onready var _node_encounter_overlay: Control = $NodeEncounterOverlay
@onready var _content: VBoxContainer = $Margin/Content
@onready var _status_label: Label = $Margin/Content/StatusLabel
@onready var _title_label: Label = $Margin/Content/HeaderRow/TitleLabel
@onready var _retreat_button: Button = $Margin/Content/HeaderRow/RetreatButton
@onready var _map_scroll: DelveMapScroll = $Margin/Content/MapBox/MapBoxMargin/MapScroll
@onready var _map_canvas: DelveMapCanvas = $Margin/Content/MapBox/MapBoxMargin/MapScroll/MapCanvas
@onready var _team_list: VBoxContainer = $Margin/Content/BottomRow/TeamPanel/TeamList
@onready var _resource_list: VBoxContainer = $Margin/Content/BottomRow/Sidebar/ResourceList
@onready var _artifact_list: VBoxContainer = $Margin/Content/BottomRow/Sidebar/ArtifactList
@onready var _map_confirm_label: Label = $Margin/Content/MapConfirmBar/MapConfirmMargin/MapConfirmRow/MapConfirmLabel
@onready var _enter_node_button: Button = $Margin/Content/MapConfirmBar/MapConfirmMargin/MapConfirmRow/EnterNodeButton
@onready var _cancel_button: Button = $Margin/Content/MapConfirmBar/MapConfirmMargin/MapConfirmRow/CancelButton
@onready var _info_label: Label = $Margin/Content/InfoBar/InfoMargin/InfoLabel
@onready var _retreat_overlay: Control = $RetreatOverlay
@onready var _retreat_confirm_button: Button = $RetreatOverlay/CenterContainer/ModalPanel/ModalMargin/ModalContent/ModalButtonRow/RetreatConfirmButton
@onready var _retreat_cancel_button: Button = $RetreatOverlay/CenterContainer/ModalPanel/ModalMargin/ModalContent/ModalButtonRow/RetreatCancelButton
@onready var _end_summary_panel: CenterContainer = $EndSummaryPanel
@onready var _end_summary_header: Label = $EndSummaryPanel/EndSummaryCard/EndSummaryMargin/EndSummaryContent/EndSummaryHeaderLabel
@onready var _end_summary_text: Label = $EndSummaryPanel/EndSummaryCard/EndSummaryMargin/EndSummaryContent/EndSummaryTextLabel
@onready var _return_to_hub_button: Button = $EndSummaryPanel/EndSummaryCard/EndSummaryMargin/EndSummaryContent/ReturnToHubButton

var _hub: HubConnection
var _roster: Array = []
var _current_status: Dictionary = {}
var _selected_node: Variant = null # Dictionary (a NodeRef-shaped entry from AvailableNodes) or null
var _hovered_info_text: String = ""
var _zoom: float = 1.0
var _map_canvas_base_size: Vector2 = Vector2(800, 300)
var _node_views: Dictionary = {} # "floorIndex,column" -> Dictionary{view, data, pos}


func _ready() -> void:
	_apply_theme()
	_retreat_button.pressed.connect(_on_retreat_pressed)
	_retreat_confirm_button.pressed.connect(_on_retreat_confirm_pressed)
	_retreat_cancel_button.pressed.connect(_on_retreat_cancel_pressed)
	_enter_node_button.pressed.connect(_on_enter_node_pressed)
	_cancel_button.pressed.connect(_on_cancel_pressed)
	_return_to_hub_button.pressed.connect(func(): get_tree().change_scene_to_file(HUB_SCENE))
	_map_scroll.zoom_requested.connect(_on_zoom_requested)
	_dev_force_resource_button.pressed.connect(_on_dev_force_node.bind("Resource"))
	_dev_force_treasure_button.pressed.connect(_on_dev_force_node.bind("Treasure"))
	_dev_force_combat_button.pressed.connect(_on_dev_force_node.bind("Combat"))

	if not AuthState.is_authenticated():
		_set_status("No active session -- returning to Login.", true)
		get_tree().change_scene_to_file(LOGIN_SCENE)
		return

	_set_status("Connecting to GameHub...", false)
	_run()


func _run() -> void:
	var ok := await _connect_hub(AuthState.token)
	if not ok:
		_set_status("Could not connect to GameHub.", true)
		return

	_set_status("Starting Delve...", false)

	var roster: Variant = await _hub.invoke("GetRoster", [])
	_roster = roster if roster is Array else []

	var team_ids: Array = []
	for a: Variant in _roster:
		if a is Dictionary and bool(a.get("inTeam", false)):
			team_ids.append(str(a.get("id", "")))

	if team_ids.is_empty():
		_set_status("No team selected -- choose a team in Sanctum before delving.", true)
		return

	var status: Variant = await _hub.invoke("StartDelve", [{"teamAnimaIds": team_ids}])
	if not (status is Dictionary):
		_set_status("Could not start a Delve -- check your connection.", true)
		return

	_current_status = status
	_set_status("", false)

	var ledger: Variant = await _hub.invoke("GetLedger", [])
	_render_map()
	_render_team()
	_render_resources(ledger if ledger is Dictionary else {})
	_render_artifacts()
	_update_map_confirm_bar()
	_update_info_bar()


# ---- Map rendering ----

func _node_key(node_data: Dictionary) -> String:
	return "%d,%d" % [int(node_data.get("floorIndex", 0)), int(node_data.get("column", 0))]


func _node_pixel_pos(floor_index: int, column: int) -> Vector2:
	var col := column if column >= 0 else int(COLUMN_COUNT / 2) # Boss (column -1) centered vertically
	return Vector2(MAP_MARGIN + floor_index * FLOOR_SPACING_X, MAP_MARGIN + col * COLUMN_SPACING_Y)


func _render_map() -> void:
	for child in _map_canvas.get_children():
		child.free()
	_node_views.clear()

	var all_nodes: Array = _current_status.get("allNodes", [])
	var current: Variant = _current_status.get("currentNode")
	var available: Array = _current_status.get("availableNodes", [])

	var current_key := _node_key(current) if current is Dictionary else ""
	var available_keys: Dictionary = {}
	for n: Variant in available:
		if n is Dictionary:
			available_keys[_node_key(n)] = true
	var selected_key := _node_key(_selected_node) if _selected_node is Dictionary else ""

	var max_x := 0.0
	var max_y := 0.0

	for n: Variant in all_nodes:
		if not (n is Dictionary): continue
		var floor_index := int(n.get("floorIndex", 0))
		var column := int(n.get("column", 0))
		var key := _node_key(n)
		var pos := _node_pixel_pos(floor_index, column)
		max_x = max(max_x, pos.x)
		max_y = max(max_y, pos.y)

		var state := "dim"
		if key == current_key:
			state = "current"
		elif available_keys.has(key):
			state = "selected" if key == selected_key else "reachable"

		var view := _build_node_view(n, state)
		view.position = pos - Vector2(NODE_VIEW_SIZE, NODE_VIEW_SIZE) * 0.5
		_map_canvas.add_child(view)
		_node_views[key] = {"data": n, "pos": pos}

	_map_canvas_base_size = Vector2(max_x + MAP_MARGIN + NODE_VIEW_SIZE * 0.5, max_y + MAP_MARGIN + NODE_VIEW_SIZE * 0.5)
	_apply_zoom()

	var segments: Array = []
	for key: String in _node_views.keys():
		var entry: Dictionary = _node_views[key]
		var node_data: Dictionary = entry["data"]
		var from_pos: Vector2 = entry["pos"]
		for next_ref: Variant in node_data.get("nextRefs", []):
			if next_ref is Dictionary:
				var next_key := _node_key(next_ref)
				if _node_views.has(next_key):
					segments.append([from_pos, _node_views[next_key]["pos"]])
	_map_canvas.set_segments(segments)


func _build_node_view(node_data: Dictionary, state: String) -> Control:
	var type_name := str(node_data.get("type", "Combat"))

	var view := Control.new()
	view.custom_minimum_size = Vector2(NODE_VIEW_SIZE, NODE_VIEW_SIZE)
	view.size = view.custom_minimum_size
	view.set_script(DELVE_MAP_NODE_SCRIPT)
	view.set("node_type", type_name)
	view.set("visual_state", state)
	# String-based connect (not view.node_pressed.connect(...)) -- view's static type is plain
	# Control (matching IconGlyph's own set_script()+set() usage elsewhere in this codebase), which
	# has no node_pressed signal declared at parse time; Object.connect(name, callable) resolves the
	# dynamically-attached script's signal by name at runtime instead.
	view.connect("node_pressed", _on_map_node_pressed.bind(node_data))

	var icon := Control.new()
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", NODE_ICONS.get(type_name, "sword"))
	# Dark icon on the "current" node's gold fill for contrast; cream everywhere else (dimming for
	# the "dim" state comes from the whole view's modulate.a, not a separate icon color).
	icon.set("icon_color", Color(COLOR_GRADIENT_BOTTOM) if state == "current" else Color(COLOR_TEXT_CREAM))
	icon.set("icon_size", 16.0)
	icon.position = Vector2(NODE_VIEW_SIZE, NODE_VIEW_SIZE) * 0.5 - Vector2(8, 8)
	view.add_child(icon)

	return view


func _on_map_node_pressed(node_data: Dictionary) -> void:
	_selected_node = node_data
	# call_deferred, not a direct call -- this handler runs INSIDE the clicked DelveMapNode's own
	# node_pressed emission (itself inside that node's _gui_input), so the node is still "locked"
	# on the call stack. _render_map()'s cleanup loop calls child.free() (immediate, not queue_free
	# -- see this codebase's own recurring-pitfall note on why), which throws "Object is locked and
	# can't be freed" if run synchronously here. Deferring the rebuild to the next idle frame lets
	# the click's own call stack unwind first, exactly like a real mouse click would still need to
	# finish its own _gui_input dispatch before anything tries to free that node. Found via a real
	# driver script clicking through the actual scene, not assumed -- this would have hit every real
	# mouse click too, not just the driver.
	call_deferred("_render_map")
	_update_map_confirm_bar()


func _on_zoom_requested(factor: float) -> void:
	_zoom = clamp(_zoom * factor, ZOOM_MIN, ZOOM_MAX)
	_apply_zoom()


func _apply_zoom() -> void:
	_map_canvas.scale = Vector2(_zoom, _zoom)
	_map_canvas.custom_minimum_size = _map_canvas_base_size * _zoom


# ---- Node selection / move confirm (its own strip, directly below the boxed map) ----
# Independent from the skill-info strip below -- a node selection and a skill hover used to share
# one message area and silently overwrite each other (live-testing feedback); now each has its own
# strip and its own state, so neither can stomp the other.

func _update_map_confirm_bar() -> void:
	if _selected_node is Dictionary:
		var floor_display := int(_selected_node.get("floorIndex", 0)) + 1
		var type_name := str(_selected_node.get("type", "?"))
		_map_confirm_label.text = "Move to the %s node on Floor %d?" % [type_name, floor_display]
		_enter_node_button.visible = true
		_cancel_button.visible = true
	else:
		_map_confirm_label.text = ""
		_enter_node_button.visible = false
		_cancel_button.visible = false


# ---- Hover/tap info (its own strip, below the Team panel) -- shared by skill chips AND Artifact
# sidebar rows (GENERALIZED this session; used to be skill-chip-only). One consistent "tap
# something, read about it down here" interaction across the whole screen.

func _update_info_bar() -> void:
	_info_label.text = _hovered_info_text if _hovered_info_text != "" else IDLE_INFO_MESSAGE


func _set_hovered_info(text: String) -> void:
	_hovered_info_text = text
	_update_info_bar()


func _clear_hovered_info() -> void:
	_hovered_info_text = ""
	_update_info_bar()


func _on_enter_node_pressed() -> void:
	if not (_selected_node is Dictionary): return

	var request := {"floorIndex": int(_selected_node.get("floorIndex", 0)), "column": int(_selected_node.get("column", 0))}
	var result: Variant = await _hub.invoke("MoveToNode", [request])
	_selected_node = null

	if not (result is Dictionary):
		_set_status("Could not move to that node -- check your connection.", true)
		return

	_current_status = result
	_render_map()
	_render_artifacts()
	_update_map_confirm_bar()
	_handle_node_arrival(result)


func _on_cancel_pressed() -> void:
	_selected_node = null
	_render_map()
	_update_map_confirm_bar()


# Resource/Treasure/Reforge/Shop/Combat/Elite/Boss all now open their real screens (as child
# panels -- see this file's own top-of-file comment for why). Combat/Elite/Boss share the same
# combat.tscn -- see _open_combat_encounter's own comment for why that one needs a second start()
# argument the other four don't.
func _handle_node_arrival(status: Dictionary) -> void:
	var current: Variant = status.get("currentNode")
	if not (current is Dictionary): return
	var type_name := str(current.get("type", "This"))
	match type_name:
		"Resource": _open_node_encounter(RESOURCE_NODE_SCENE)
		"Treasure": _open_node_encounter(TREASURE_NODE_SCENE)
		"Combat", "Elite", "Boss": _open_combat_encounter(type_name)
		_: _set_status("%s node -- not built yet." % type_name, false)

# ---- Resource/Treasure node encounters (child panels, share this scene's own _hub) ----

func _open_node_encounter(scene: PackedScene) -> void:
	for child in _node_encounter_overlay.get_children():
		child.free()

	var instance := scene.instantiate() as Control
	_node_encounter_overlay.add_child(instance)
	_node_encounter_overlay.visible = true
	# String-based connect()/call() (not instance.resolved.connect(...) / instance.start(...)) --
	# instance's static type is plain Control (resource_node.gd/treasure_node.gd have no shared
	# class_name to type this as), same reasoning as the map nodes' own connect() usage above.
	instance.connect("resolved", _on_node_encounter_resolved.bind(instance))
	instance.call("start", _hub)


func _on_node_encounter_resolved(instance: Control) -> void:
	_node_encounter_overlay.visible = false
	instance.queue_free()
	await _refresh_after_node_resolution()


# ---- Combat/Elite/Boss encounters (Phase 1: layout + real-data rendering only, see combat.gd's
# own top-of-file comment for full scope) ----

# combat.gd's start() takes a second argument (node_type) the other four node screens' start(hub)
# doesn't need -- CombatStatus carries no node-type field at all (confirmed by reading
# GameHubModels.cs), so the Combat/Elite/Boss background-palette tier can only come from what THIS
# script already knows about the node it's routing through, same as this match statement itself
# already relies on to dispatch here in the first place.
func _open_combat_encounter(node_type: String) -> void:
	for child in _node_encounter_overlay.get_children():
		child.free()

	var instance := COMBAT_SCENE.instantiate() as Control
	_node_encounter_overlay.add_child(instance)
	_node_encounter_overlay.visible = true
	instance.connect("resolved", _on_combat_encounter_closed.bind(instance))
	instance.call("start", _hub, node_type)


# Deliberately does NOT call _refresh_after_node_resolution() -- Phase 1 has no way to actually WIN
# or LOSE a fight yet (no SubmitAction calls at all), so closing this panel (via combat.gd's own
# DEV-only "Back to Map" button -- see its top-of-file comment) never actually resolves the node;
# nothing about the map/Artifacts/Wisp changed, so there is nothing real to refresh. The node stays
# uncleared and ActiveCombat stays alive server-side (GetCombatState's resume support covers
# reconnecting into it), exactly like standing on any other unresolved node.
func _on_combat_encounter_closed(instance: Control) -> void:
	_node_encounter_overlay.visible = false
	instance.queue_free()



# CollectResourceNode/ClaimTreasureNode return their own reward-shaped result, not a DelveStatus --
# GetDelveStatus (unused everywhere else in this script) is the one real gap that leaves for the
# map's cleared-state/Artifacts to catch up on, per this file's own top-of-file comment.
func _refresh_after_node_resolution() -> void:
	var status: Variant = await _hub.invoke("GetDelveStatus", [])
	if status is Dictionary:
		_current_status = status
		_render_map()
		_render_artifacts()

	var ledger: Variant = await _hub.invoke("GetLedger", [])
	_render_resources(ledger if ledger is Dictionary else {})
	_update_map_confirm_bar()


# ---- DEV ONLY: force-reach a Resource/Treasure node for manual testing ----
# See this file's own top-of-file comment: this walks the REAL map graph via repeated real
# MoveToNode calls (never a fabricated/forced server state) -- strip this panel out once Combat
# exists and normal play can reach Floor 6+ on its own.

## Deliberately does NOT special-case "already standing on a matching node" -- a real bug caught by
## this session's own driver script: the current node might already be CLEARED (its own encounter
## already resolved), in which case re-triggering it just throws ("already cleared") instead of
## walking to the NEXT occurrence. _dev_find_path_to_type always requires at least one real hop, so
## it naturally walks past an already-cleared current position to find the next real match, whether
## standing on one or not.
func _on_dev_force_node(type_name: String) -> void:
	var path := _dev_find_path_to_type(type_name)
	if path.is_empty():
		_set_status("DEV: no reachable %s node found on this map." % type_name, true)
		return

	_set_status("DEV: auto-walking to a %s node (%d hop%s)..." % [type_name, path.size(), "" if path.size() == 1 else "s"], false)

	var result: Variant = null
	for step: Dictionary in path:
		var request := {"floorIndex": int(step.get("floorIndex", 0)), "column": int(step.get("column", 0))}
		result = await _hub.invoke("MoveToNode", [request])
		if not (result is Dictionary):
			_set_status("DEV: auto-walk failed mid-path -- check your connection.", true)
			return
		_current_status = result

	_selected_node = null
	_render_map()
	_render_artifacts()
	_update_map_confirm_bar()
	_handle_node_arrival(result)


# Plain BFS over AllNodes' real NextRefs adjacency, starting from CurrentNode (or every Floor-1
# start node if none yet) -- returns the ordered list of node entries to MoveToNode through, one
# real hop at a time, EXCLUDING the start node. Empty if no matching node is reachable at all
# (e.g. a Boss-only remaining path, or a freak map with neither type rolled anywhere ahead).
func _dev_find_path_to_type(type_name: String) -> Array:
	var all_nodes: Array = _current_status.get("allNodes", [])
	var by_key: Dictionary = {}
	for n: Variant in all_nodes:
		if n is Dictionary:
			by_key[_node_key(n)] = n

	var visited: Dictionary = {}
	var queue: Array = []
	var current: Variant = _current_status.get("currentNode")

	if current is Dictionary:
		# Real current position, zero hops taken -- matching THIS node is already handled by the
		# caller before this ever runs, so it seeds the search but is never itself a valid return
		# (a length-0 "path" would mean "call MoveToNode on where we already are", which isn't real).
		var start_key := _node_key(current)
		visited[start_key] = true
		queue.append({"key": start_key, "path": []})
	else:
		# No move yet this Delve -- every AvailableNodes entry is already a real, valid ONE-hop
		# MoveToNode target in its own right (there is no "already there" yet to exclude), so each
		# seeds the queue with a length-1 path, not length-0.
		for n: Variant in _current_status.get("availableNodes", []):
			if not (n is Dictionary): continue
			var key := _node_key(n)
			if visited.has(key): continue
			visited[key] = true
			queue.append({"key": key, "path": [n]})

	while not queue.is_empty():
		var entry: Dictionary = queue.pop_front()
		var node_data: Dictionary = by_key.get(entry["key"], {})
		var hop_path: Array = entry["path"]
		# Require at least one real hop -- the seed entry for a real CurrentNode has an empty path
		# (that's our actual position, not a MoveToNode target); without this guard, standing on an
		# already-cleared matching node would "find" a zero-hop path and immediately re-trigger the
		# same cleared encounter instead of walking to the next real occurrence (see this function's
		# own caller for the real bug this fixes).
		if not hop_path.is_empty() and str(node_data.get("type", "")) == type_name:
			return hop_path
		for next_ref: Variant in node_data.get("nextRefs", []):
			if not (next_ref is Dictionary): continue
			var next_key := _node_key(next_ref)
			if visited.has(next_key): continue
			visited[next_key] = true
			var next_node: Dictionary = by_key.get(next_key, {})
			var new_path: Array = (entry["path"] as Array).duplicate()
			new_path.append(next_node)
			queue.append({"key": next_key, "path": new_path})

	return []


# ---- Team panel ----

func _icon_kind_for_part(part: Dictionary) -> String:
	if str(part.get("part", "")) == "Crest":
		return "diamond"
	var category := str(part.get("category", ""))
	match category:
		"Attack": return "sword"
		"Heal": return "heart"
		"Buff": return "shield" if bool(part.get("grantsShield", false)) else "bolt"
		_: return "bolt"


func _render_team() -> void:
	for child in _team_list.get_children():
		child.free()

	var team: Array = []
	for a: Variant in _roster:
		if a is Dictionary and bool(a.get("inTeam", false)):
			team.append(a)

	if team.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No team on this Delve."
		empty_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		_team_list.add_child(empty_label)
		return

	for a: Dictionary in team:
		_team_list.add_child(_build_team_card(a))


# Portrait-on-top vertical box (NEW this session -- was a horizontal row with portrait+name+HP on
# the left and skills trailing off to the right). TeamList is now an HBoxContainer of 3 of these
# side-by-side (see delve.tscn) instead of 3 stacked rows -- each box is a bit taller, but 3 across
# nets a noticeably SHORTER Team panel overall, which is what actually matters for this screen's
# vertical-fit budget (see this file's own top-of-file comment on that).
func _build_team_card(anima: Dictionary) -> Control:
	var card := VBoxContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.add_theme_constant_override("separation", 3)

	var color_name: String = str(anima.get("color", ""))
	var portrait_wrap := AspectRatioContainer.new()
	portrait_wrap.ratio = 1.0
	# SHRINK_CENTER (not EXPAND_FILL) -- centers the portrait within the column's full width instead
	# of stretching it edge-to-edge; explicit custom_minimum_size still required regardless, per
	# hub.gd's own comment on why (a Control with real content but no minimum size collapses to
	# invisible inside a container that doesn't infer one for it).
	portrait_wrap.custom_minimum_size = Vector2(40, 40)
	portrait_wrap.size_flags_horizontal = Control.SIZE_SHRINK_CENTER

	if ASPECT_SPRITES.has(color_name):
		var portrait := TextureRect.new()
		portrait.texture = load(ASPECT_SPRITES[color_name])
		portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		portrait.stretch_mode = TextureRect.STRETCH_SCALE
		portrait_wrap.add_child(portrait)
	else:
		var fallback := ColorRect.new()
		fallback.color = Color(HYBRID_FALLBACK_TINTS.get(color_name, "555555"))
		portrait_wrap.add_child(fallback)
	card.add_child(portrait_wrap)

	var name_label := Label.new()
	name_label.text = str(anima.get("name", "?"))
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	name_label.add_theme_font_size_override("font_size", 12)
	card.add_child(name_label)

	var hp_label := Label.new()
	hp_label.text = "HP %d/%d" % [int(anima.get("currentHp", 0)), int(anima.get("maxHp", 0))]
	hp_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hp_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	hp_label.add_theme_font_size_override("font_size", 10)
	card.add_child(hp_label)

	# Compact 2x2 grid, not a single row -- 4 chips across in a narrower column (one of 3 sharing
	# the Team panel's width now, instead of the whole panel) would either crowd or force horizontal
	# scrolling; a grid keeps every chip's text readable at this width.
	#
	# Wrapped in a CenterContainer (real bug, caught via a live screenshot): "card" is a wide
	# SIZE_EXPAND_FILL column (roughly a third of the whole Team panel), and a bare GridContainer
	# has no size flags of its own pulling it toward any particular width -- left unwrapped, its two
	# columns spread across the ENTIRE card width (column 1 pinned near the card's left edge, far
	# from the portrait above it) instead of staying a tight, portrait-centered block. The
	# CenterContainer sizes to the grid's own natural (compact) content width and centers that
	# within the wide card, matching the portrait/name/HP above it.
	var chips_center := CenterContainer.new()
	card.add_child(chips_center)

	var chips_grid := GridContainer.new()
	chips_grid.columns = 2
	chips_grid.add_theme_constant_override("h_separation", 6)
	chips_grid.add_theme_constant_override("v_separation", 3)
	chips_center.add_child(chips_grid)

	for p: Variant in anima.get("parts", []):
		if p is Dictionary:
			chips_grid.add_child(_build_skill_chip(p))

	return card


func _build_skill_chip(part: Dictionary) -> Control:
	var wrapper := Control.new()
	# Explicit WIDTH too (was Vector2(0, 16), height-only) -- a real bug caught via a live
	# screenshot: inside the Team panel's new 2-column GridContainer wrapped in a CenterContainer
	# (see _build_team_card's own comment on why that wrapping exists), a cell with zero declared
	# minimum width gives the GridContainer nothing to size its columns from (the icon+label row
	# inside is anchor-positioned, which doesn't feed a minimum size back up to this wrapper), so
	# every chip collapsed to the same zero-width point and overlapped. 100px comfortably fits this
	# roster's longest skill names ("Guiding Light", "Bloodthirst") at this font size.
	wrapper.custom_minimum_size = Vector2(100, 16)
	wrapper.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	wrapper.mouse_filter = Control.MOUSE_FILTER_STOP
	wrapper.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND

	var row := HBoxContainer.new()
	row.set_anchors_preset(Control.PRESET_FULL_RECT)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_theme_constant_override("separation", 4)
	wrapper.add_child(row)

	var icon_kind := _icon_kind_for_part(part)
	var icon := Control.new()
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", icon_kind)
	icon.set("icon_color", Color(SKILL_ICON_COLORS.get(icon_kind, "e8a03a")))
	icon.set("icon_size", 11.0)
	row.add_child(icon)

	var label := Label.new()
	label.text = str(part.get("skillName", "--"))
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	label.add_theme_font_size_override("font_size", 10)
	row.add_child(label)

	var augments: Array = part.get("appliedAugments", [])
	if not augments.is_empty():
		wrapper.add_child(_build_augment_badge())

	var description := _skill_hover_text(part)
	wrapper.mouse_entered.connect(func(): _set_hovered_info(description))
	wrapper.mouse_exited.connect(func(): _clear_hovered_info())
	wrapper.gui_input.connect(_on_info_source_gui_input.bind(description))

	return wrapper


# Tapping a chip (or, GENERALIZED this session, an Artifact row) shows the same info hovering it
# would (desktop hover has no equivalent on the Web export's touch paths) -- it persists until
# something else changes the info bar (another chip/artifact, a node selection), rather than
# trying to fake a touch "mouse_exited" that doesn't really exist. Shared by both info sources --
# was skill-chip-only, renamed since it no longer is.
func _on_info_source_gui_input(event: InputEvent, description: String) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_set_hovered_info(description)


func _skill_hover_text(part: Dictionary) -> String:
	var text := "%s (%s) -- %s" % [str(part.get("skillName", "?")), str(part.get("part", "?")), str(part.get("description", ""))]
	var augments: Array = part.get("appliedAugments", [])
	if not augments.is_empty():
		var names: Array = []
		for a: Variant in augments:
			names.append(str(a))
		text += " | Augments: %s" % ", ".join(names)
	return text


# Fixed-offset anchoring (not PRESET_MODE_MINSIZE) -- same reason sanctum.gd's own "In team" badge
# uses fixed offsets: this badge is built and returned before it (or its Label child) ever enters
# the scene tree, so a size-based preset would bake in a still-zero minimum size at build time.
func _build_augment_badge() -> Control:
	var badge := PanelContainer.new()
	badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
	badge.anchor_left = 1.0
	badge.anchor_right = 1.0
	badge.anchor_top = 0.0
	badge.anchor_bottom = 0.0
	badge.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	badge.grow_vertical = Control.GROW_DIRECTION_END
	badge.offset_left = -12.0
	badge.offset_right = 2.0
	badge.offset_top = -6.0
	badge.offset_bottom = 8.0

	var style := StyleBoxFlat.new()
	style.bg_color = Color(COLOR_ACCENT_AMBER)
	style.set_corner_radius_all(7)
	badge.add_theme_stylebox_override("panel", style)

	var label := Label.new()
	label.text = "+"
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.add_theme_color_override("font_color", Color(COLOR_GRADIENT_BOTTOM))
	label.add_theme_font_size_override("font_size", 10)
	badge.add_child(label)

	return badge


# ---- Resources & Artifacts sidebar ----

func _render_resources(ledger: Dictionary) -> void:
	for child in _resource_list.get_children():
		child.free()

	var balances: Dictionary = ledger.get("balances", {})
	for key: String in RESOURCE_ORDER:
		_resource_list.add_child(_build_resource_row(key, int(balances.get(key, 0))))


func _build_resource_row(key: String, count: int) -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)

	var icon := Control.new()
	icon.custom_minimum_size = Vector2(13, 13)
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", RESOURCE_ICONS.get(key, "sparkle"))
	icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	icon.set("icon_size", 13.0)
	row.add_child(icon)

	var label := Label.new()
	label.text = str(RESOURCE_LABELS.get(key, key))
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	label.add_theme_font_size_override("font_size", 11)
	row.add_child(label)

	var count_label := Label.new()
	count_label.text = str(count)
	count_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	count_label.add_theme_font_size_override("font_size", 12)
	row.add_child(count_label)

	return row


func _render_artifacts() -> void:
	for child in _artifact_list.get_children():
		child.free()

	var artifacts: Array = _current_status.get("artifacts", [])
	for i in range(MAX_ARTIFACTS):
		if i < artifacts.size() and artifacts[i] is Dictionary:
			_artifact_list.add_child(_build_artifact_row(artifacts[i]))
		else:
			_artifact_list.add_child(_build_empty_artifact_slot())


# Icon + name only, single compact line -- description moved to tap/hover into the shared info bar
# (GENERALIZED this session, see that section's own comment), same "tap something, read about it
# down here" pattern the skill chips already use, instead of a second, inconsistent inline-text
# pattern living in the sidebar.
func _build_artifact_row(artifact: Dictionary) -> Control:
	var card := PanelContainer.new()
	card.mouse_filter = Control.MOUSE_FILTER_STOP
	card.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND

	var style := StyleBoxFlat.new()
	style.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	card.add_theme_stylebox_override("panel", style)

	var margin := MarginContainer.new()
	margin.mouse_filter = Control.MOUSE_FILTER_IGNORE
	margin.add_theme_constant_override("margin_left", 6)
	margin.add_theme_constant_override("margin_top", 4)
	margin.add_theme_constant_override("margin_right", 6)
	margin.add_theme_constant_override("margin_bottom", 4)
	card.add_child(margin)

	var row := HBoxContainer.new()
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_theme_constant_override("separation", 6)
	margin.add_child(row)

	var icon := Control.new()
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon.custom_minimum_size = Vector2(16, 16)
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", ARTIFACT_ICONS.get(str(artifact.get("name", "")), "sparkle"))
	icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	icon.set("icon_size", 16.0)
	row.add_child(icon)

	var name_label := Label.new()
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_label.text = str(artifact.get("name", "?"))
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	name_label.add_theme_font_size_override("font_size", 11)
	row.add_child(name_label)

	var description := _artifact_hover_text(artifact)
	card.mouse_entered.connect(func(): _set_hovered_info(description))
	card.mouse_exited.connect(func(): _clear_hovered_info())
	card.gui_input.connect(_on_info_source_gui_input.bind(description))

	return card


func _artifact_hover_text(artifact: Dictionary) -> String:
	return "%s -- %s" % [str(artifact.get("name", "?")), str(artifact.get("description", ""))]


# Dashed border isn't natively supported by StyleBoxFlat -- approximated with a plain thin border,
# same "close enough at placeholder fidelity" simplification already flagged elsewhere in this
# codebase (e.g. sanctum.gd's square-portrait-vs-mock's-rounded-corners comment).
func _build_empty_artifact_slot() -> Control:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(0, 26)

	var style := StyleBoxFlat.new()
	style.bg_color = Color(0, 0, 0, 0)
	style.border_color = Color(Color(COLOR_CARD_BORDER), 0.15)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	panel.add_theme_stylebox_override("panel", style)

	var label := Label.new()
	label.text = "Empty slot"
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	label.add_theme_font_size_override("font_size", 11)
	panel.add_child(label)

	return panel


# ---- Retreat ----

func _on_retreat_pressed() -> void:
	_retreat_overlay.visible = true


func _on_retreat_cancel_pressed() -> void:
	_retreat_overlay.visible = false


func _on_retreat_confirm_pressed() -> void:
	_retreat_overlay.visible = false
	var result: Variant = await _hub.invoke("RetreatFromDelve", [])
	if result is Dictionary:
		_show_end_summary(result, "Delve Retreated")
	else:
		_set_status("Retreat failed -- check your connection.", true)


# Match Result screens don't exist yet either (separate future task, per this task's own scope
# note) -- a plain inline summary + Return to Hub button stands in for the fully designed
# Defeat/Retreat result screen from CLAUDE.md's Match Result & Retreat System section.
func _show_end_summary(summary: Dictionary, header: String) -> void:
	_content.visible = false
	_end_summary_panel.visible = true
	_end_summary_header.text = header
	var floor_display := int(summary.get("floorIndexReached", 0)) + 1
	_end_summary_text.text = "Wisp earned this run: %d -> kept: %d\nFloor reached: %d" % [
		int(summary.get("wispEarnedThisRun", 0)), int(summary.get("wispKept", 0)), floor_display]


# ---- Shared chrome ----

func _set_status(text: String, is_error: bool) -> void:
	_status_label.visible = text != ""
	_status_label.remove_theme_color_override("font_color")
	_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR) if is_error else Color(COLOR_TEXT_CREAM_DIM))
	_status_label.text = text


## Same connect-and-poll pattern as every other real screen's own _connect_hub.
func _connect_hub(token: String) -> bool:
	_hub = HubConnection.new()
	add_child(_hub)
	_hub.debug_mode(true)

	var fail_reason := ""
	_hub.error_occurred.connect(func(msg: String): fail_reason = msg)

	_hub.connect_to_url(SERVER_WS_URL, token)

	var elapsed := 0.0
	while not _hub.is_connected_to_hub() and elapsed < 10.0:
		await get_tree().process_frame
		elapsed += get_process_delta_time()

	if not _hub.is_connected_to_hub():
		_set_status("FAILED: hub did not connect within 10s. Last error: %s" % fail_reason, true)
		return false

	return true


func _apply_theme() -> void:
	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color(COLOR_GRADIENT_TOP), Color(COLOR_GRADIENT_MID), Color(COLOR_GRADIENT_BOTTOM)])
	gradient.offsets = PackedFloat32Array([0.0, 0.45, 1.0])

	var gradient_texture := GradientTexture2D.new()
	gradient_texture.gradient = gradient
	gradient_texture.fill = GradientTexture2D.FILL_RADIAL
	gradient_texture.width = 512
	gradient_texture.height = 512
	gradient_texture.fill_from = Vector2(0.3, 0.2)
	gradient_texture.fill_to = Vector2(0.95, 1.0)
	_background.texture = gradient_texture

	_title_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_title_label.add_theme_font_size_override("font_size", 18)
	_status_label.add_theme_font_size_override("font_size", 13)

	for label: Label in [$Margin/Content/BottomRow/TeamPanel/TeamHeaderLabel, $Margin/Content/BottomRow/Sidebar/ResourcesHeaderLabel, $Margin/Content/BottomRow/Sidebar/ArtifactsHeaderLabel]:
		label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
		label.add_theme_font_size_override("font_size", 13)

	_style_map_box()

	_style_panel($Margin/Content/MapConfirmBar)
	_map_confirm_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	_map_confirm_label.add_theme_font_size_override("font_size", 12)

	_style_panel($Margin/Content/InfoBar)
	_info_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	_info_label.add_theme_font_size_override("font_size", 12)

	_style_panel($RetreatOverlay/CenterContainer/ModalPanel)
	_style_panel($EndSummaryPanel/EndSummaryCard)
	_end_summary_header.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_end_summary_header.add_theme_font_size_override("font_size", 16)
	_end_summary_text.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))

	_style_dev_panel()


# DEV ONLY panel styling -- deliberately loud/unmistakable (hot-pink border+text, unrelated to the
# rest of this screen's warm palette) so it reads as "not part of the real game" at a glance, per
# this task's own "visually distinct" instruction. Strip this whole function (and the DevPanel node
# in delve.tscn) out once Combat ships.
func _style_dev_panel() -> void:
	const COLOR_DEV := "ff3ea5"
	var style := StyleBoxFlat.new()
	style.bg_color = Color(Color(COLOR_DEV), 0.12)
	style.border_color = Color(COLOR_DEV)
	style.set_border_width_all(2)
	style.set_corner_radius_all(6)
	$Margin/Content/DevPanel.add_theme_stylebox_override("panel", style)

	var dev_label: Label = $Margin/Content/DevPanel/DevMargin/DevRow/DevLabel
	dev_label.add_theme_color_override("font_color", Color(COLOR_DEV))
	dev_label.add_theme_font_size_override("font_size", 11)

	for button: Button in [_dev_force_resource_button, _dev_force_treasure_button, _dev_force_combat_button]:
		button.add_theme_color_override("font_color", Color(COLOR_DEV))
		button.add_theme_color_override("font_color_hover", Color(COLOR_DEV))
		button.add_theme_color_override("font_color_pressed", Color(COLOR_DEV))


func _style_panel(panel: PanelContainer) -> void:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(Color(COLOR_CARD_BG), 0.85)
	style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	style.set_border_width_all(1)
	style.set_corner_radius_all(12)
	panel.add_theme_stylebox_override("panel", style)


# The map's own box (NEW, live-testing feedback) -- noticeably darker/near-black than every other
# panel on this screen (rgba(0,0,0,0.55) here vs. _style_panel's own rgba(30,22,16,0.85)) so it
# reads as a clearly separate, boxed-off region from the Team panel below it, matching the level of
# separation CLAUDE.md's own Combat arena design calls for ("darker inset panel... clearly
# separated from the HUD").
func _style_map_box() -> void:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0, 0, 0, 0.55)
	style.border_color = Color(Color(COLOR_CARD_BORDER), 0.25)
	style.set_border_width_all(1)
	style.set_corner_radius_all(10)
	$Margin/Content/MapBox.add_theme_stylebox_override("panel", style)
