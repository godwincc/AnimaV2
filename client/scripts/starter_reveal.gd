extends Control

## Starter-trio orchestration around the shared AnimaRevealPanel component (see
## anima_reveal_panel.gd) -- owns the GameHub connection, the GetPendingStarterReveal/
## ConfirmStarterAnima RPC calls, and the "walk through 3 slots in sequence" logic. All of the
## actual portrait/Name-Color-Gen/Threads/naming/Confirm UI lives in the panel instance; this
## script only feeds it data (render) and reacts to its `confirmed` signal.
##
## Reached from hub.gd when GetPendingStarterReveal() comes back non-empty (a brand-new account, or
## a reconnect mid-naming). Opens its OWN GameHub connection (scenes don't share a live connection
## across change_scene_to_file in this codebase yet -- same pattern login.gd -> hub.gd already
## has). Naming is strictly sequential and mandatory -- there is no "skip" or "reroll" control
## anywhere on this screen, matching the server's own no-discard-path contract.

const SERVER_WS_URL := "ws://localhost:5143/hubs/game"
const LOGIN_SCENE := "res://scenes/login.tscn"
const HUB_SCENE := "res://scenes/hub.tscn"

# Warm sanctuary/workshop theme, same palette login.gd/hub.gd already use.
const COLOR_GRADIENT_TOP := "4a3a2e"
const COLOR_GRADIENT_MID := "2b2018"
const COLOR_GRADIENT_BOTTOM := "1a130e"
const COLOR_TEXT_CREAM := "e8cf9a"
const COLOR_ERROR := "e2554a"

# Starter Anima are always Gen 1 -- not part of the wire shape (see AnimaRevealPanel's own
# comment for why Gen has to be supplied by the caller rather than read off the genome).
const STARTER_GEN := 1

@onready var _background: TextureRect = $Background
@onready var _card: PanelContainer = $CenterContainer/Card
@onready var _panel: AnimaRevealPanel = $CenterContainer/Card/Margin/Content/RevealPanel
@onready var _status_label: Label = $CenterContainer/Card/Margin/Content/StatusLabel

var _hub: HubConnection
var _busy: bool = false


func _ready() -> void:
	_apply_theme()
	_panel.confirmed.connect(_on_panel_confirmed)

	if not AuthState.is_authenticated():
		_set_status("No active session -- returning to Login.", true)
		get_tree().change_scene_to_file(LOGIN_SCENE)
		return

	_set_status("Connecting...", false)
	_run()


func _run() -> void:
	var ok := await _connect_hub(AuthState.token)
	if not ok:
		_set_status("Could not connect to GameHub.", true)
		return

	var pending: Variant = await _hub.invoke("GetPendingStarterReveal", [])
	_apply_remaining(pending)


## Shared by the initial load and every ConfirmStarterAnima response -- both hand this method the
## SAME shape (an Array of StarterRevealEntry-like Dictionaries), so there's exactly one place that
## decides "show the next entry" vs. "all done, go to Hub."
func _apply_remaining(remaining: Variant) -> void:
	if not (remaining is Array) or remaining.size() == 0:
		get_tree().change_scene_to_file(HUB_SCENE)
		return

	var entry: Dictionary = remaining[0]
	var slot_number: int = int(entry.get("slotNumber", 0))
	var total_count: int = int(entry.get("totalCount", 3))
	var archetype_name: String = str(entry.get("archetypeName", ""))
	var genome: Dictionary = entry.get("genome", {})

	_panel.render(genome, STARTER_GEN, archetype_name, "Starter Anima %d of %d" % [slot_number, total_count])
	_set_status("", false)


func _on_panel_confirmed(name: String) -> void:
	if _busy:
		return

	_busy = true
	_panel.set_busy(true)
	_set_status("Confirming...", false)

	var result: Variant = await _hub.invoke("ConfirmStarterAnima", [{"name": name}])

	_busy = false
	_panel.set_busy(false)

	if result == null or not (result is Dictionary):
		_panel.show_error("Something went wrong confirming this Anima. Check your connection and try again.")
		return

	var remaining: Variant = result.get("remaining", [])
	_apply_remaining(remaining)


func _set_status(text: String, is_error: bool) -> void:
	_status_label.remove_theme_color_override("font_color")
	_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR) if is_error else Color(COLOR_TEXT_CREAM))
	_status_label.text = text


## Same connect-and-poll pattern as connectivity_test.gd/hub.gd's own _connect_hub -- see
## connectivity_test.gd's comment for why a bare `await hub.connected` isn't safe to use here.
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

	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(30.0 / 255.0, 22.0 / 255.0, 16.0 / 255.0, 0.85)
	card_style.border_color = Color(Color("c9b89e"), 0.2)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(12)
	_card.add_theme_stylebox_override("panel", card_style)
