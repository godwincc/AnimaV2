extends Control

## The real Shop node screen -- Rest + Wares only this pass (see this file's own AUGMENT STUB
## comment below for what's deliberately not built yet). Reached from delve.gd, per Shop node
## arrival.
##
## Same child-panel/shared-connection architecture as resource_node.gd/treasure_node.gd/
## reforge_node.gd -- see resource_node.gd's own top-of-file comment for why a real scene change
## would orphan the in-progress DelveRun. start(hub) receives delve.gd's already-open HubConnection
## via loose .call(), same as those three.
##
## RPC NAMES CONFIRMED AGAINST REAL CODE, NOT CLAUDE.md PROSE: CLAUDE.md's own Shop section names
## "ShopService.TryRest"/"BuyWaresArtifact"/"EmberService.TryBuyEmber" as if those were the public
## Hub RPCs -- reading GameHub.cs directly shows the actual public surface is GetShopStock() /
## RestAtShop() / BuyWaresEmber(BuyWaresEmberRequest) / BuyWaresArtifact(), each a thin wrapper that
## calls the Core-layer method CLAUDE.md names. EnsureShopVisited (also named in CLAUDE.md) is a
## PRIVATE server-side helper, not something this client ever calls directly -- it fires
## automatically as a side effect of GetShopStock/RestAtShop/BuyWaresEmber/BuyWaresArtifact, whichever
## the player does first.
##
## CLEARING TIMING (flagged, differs from Resource/Treasure): GetShopStock() itself triggers the
## server's EnsureShopVisited, which calls DelveRun.MarkCurrentNodeCleared() the FIRST time ANY Shop
## RPC fires for this node -- confirmed by reading GameHub.cs directly, not assumed. That means the
## node is already cleared server-side the moment start() below makes its first call (GetShopStock),
## same "resolves on load" timing Resource/Treasure already have -- NOT gated behind an explicit
## Accept/Decline the way Reforge's browse-then-commit flow is. The `resolved` signal below still
## only fires on Leave -- that's purely about when delve.gd tears this panel down and refreshes the
## map/Artifacts, not about when the node "counts" as cleared (which already happened, transparently,
## before this screen even finishes rendering).
##
## INSUFFICIENT-WISP HANDLING (flagged, differs from Reforge's AcceptReforge): RestAtShop/
## BuyWaresEmber/BuyWaresArtifact do NOT return a distinct "InsufficientWisp" outcome the way
## AcceptReforge does -- reading GameHub.cs shows all three just throw a generic HubException
## ("Insufficient Wisp.") on failure, indistinguishable on the wire from any other rejection. Since
## GetShopStock already returns the real current prices (Ember Core discount applied) and GetLedger
## gives the real current Wisp balance, this screen instead computes affordability CLIENT-SIDE from
## real server-confirmed numbers and disables/grays the relevant button before the player can even
## attempt an unaffordable purchase -- matching the task's "gray out + reason" requirement using the
## data that's actually available, rather than inventing a structured outcome the server doesn't
## send. A real server rejection (e.g. a stale balance) still falls back to a generic status message.
##
## AUGMENT (NEW, follow-up session -- replaces the old inline stub): a successful BuyWaresEmber now
## opens the real, shared augment_picker.gd/.tscn (Augment now / Convert to Wisp) as a CHILD panel
## of THIS screen's own AugmentOverlay -- same child-panel/shared-connection pattern this screen
## itself uses inside delve.gd's NodeEncounterOverlay, one level deeper. This screen owns nothing
## about Augment beyond instancing it and tearing it down on its `resolved` signal (see
## _open_augment_picker/_on_augment_picker_resolved below) -- no Shop-specific data is passed in
## beyond the Ember's own color, per augment_picker.gd's own ownership contract, so the exact same
## call will work unmodified from a future Combat Ember-drop.

signal resolved

# Warm amber, deliberately close to Hub/Reforge's own 4a3a2e/2b2018/1a130e sanctuary gradient
# (CLAUDE.md's own "closest to Hub's tone" instruction for Shop) but nudged slightly warmer/more
# amber than that neutral tone, and clearly less saturated-gold than Resource's own
# 4a3e1e/2b2413/17130a -- distinguishes the room at a glance without introducing a whole new palette
# family the other real node screens don't have.
const COLOR_GRADIENT_TOP := "4a3c24"
const COLOR_GRADIENT_MID := "2b2016"
const COLOR_GRADIENT_BOTTOM := "1a130d"
const COLOR_CARD_BG := "1e1710"
const COLOR_CARD_BORDER := "c9b89e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_TEXT_MUTED := "a89680"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_GLOW := "e8a03a"
const COLOR_BUTTON_TEXT := "2b2413"
const COLOR_ERROR := "e2554a"
const COLOR_SUCCESS := "7ac47a"

const GRADIENT_BUTTON_A := "d1963a"
const GRADIENT_BUTTON_B := "a66f1f"

# Same 12-entry icon mapping as collection.gd/delve.gd/treasure_node.gd -- neither icon kind nor
# display order exists on the wire, so this is this screen's own static table, duplicated per this
# codebase's existing per-screen convention.
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

# Same per-color hex tint convention anima_reveal_panel.gd's own COLOR_TINTS already uses (duplicated
# here, not shared -- this codebase's established small-mapping-per-file pattern). Ember slots have
# no portrait art of their own; a color-tinted "sun" glyph (already Ember Core's own icon) stands in
# for "a glowing Ember of this color."
const EMBER_COLOR_TINTS := {
	"Crimson": "8a3a3a",
	"Onyx": "4a4a52",
	"Verdant": "3a6a4a",
	"Azure": "3a5a7a",
}

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")
const GLOW_CIRCLE_SCRIPT := preload("res://scripts/glow_circle.gd")
const AUGMENT_PICKER_SCENE := preload("res://scenes/augment_picker.tscn")

@onready var _augment_overlay: Control = $AugmentOverlay
@onready var _background: TextureRect = $Background
@onready var _title_label: Label = $CenterContainer/Card/Margin/Content/HeaderRow/TitleLabel
@onready var _subtitle_label: Label = $CenterContainer/Card/Margin/Content/SubtitleLabel
@onready var _rest_panel: PanelContainer = $CenterContainer/Card/Margin/Content/RestPanel
@onready var _rest_header_label: Label = $CenterContainer/Card/Margin/Content/RestPanel/RestMargin/RestContent/RestHeaderLabel
@onready var _rest_team_list: VBoxContainer = $CenterContainer/Card/Margin/Content/RestPanel/RestMargin/RestContent/RestTeamList
@onready var _rest_desc_label: Label = $CenterContainer/Card/Margin/Content/RestPanel/RestMargin/RestContent/RestActionRow/RestDescLabel
@onready var _rest_button_slot: CenterContainer = $CenterContainer/Card/Margin/Content/RestPanel/RestMargin/RestContent/RestActionRow/RestButtonSlot
@onready var _rest_reason_label: Label = $CenterContainer/Card/Margin/Content/RestPanel/RestMargin/RestContent/RestReasonLabel
@onready var _wares_header_label: Label = $CenterContainer/Card/Margin/Content/WaresHeaderLabel
@onready var _ember_row: HBoxContainer = $CenterContainer/Card/Margin/Content/EmberRow
@onready var _artifact_slot: VBoxContainer = $CenterContainer/Card/Margin/Content/ArtifactSlot
@onready var _status_label: Label = $CenterContainer/Card/Margin/Content/StatusLabel
@onready var _leave_button: Button = $CenterContainer/Card/Margin/Content/FooterRow/LeaveButton

var _hub: HubConnection
var _busy: bool = false

var _wisp_balance: int = 0
var _team: Array = [] # Array[Dictionary] -- AnimaSummary shape, inTeam-filtered
var _ember_slots: Array = [] # Array[Dictionary]{index:int, color:String} -- color == "" once bought
var _artifact_name: String = ""
var _artifact_description: String = ""
var _ember_price: int = 0
var _artifact_price: int = 0
var _rest_wisp_cost: int = 0


func _ready() -> void:
	_apply_theme()
	_leave_button.pressed.connect(_on_leave_pressed)
	_status_label.text = ""
	_rest_reason_label.text = ""


## Called by delve.gd right after instancing this scene -- see this file's own top-of-file comment.
func start(hub: HubConnection) -> void:
	_hub = hub
	_load()


func _load() -> void:
	_set_busy(true)
	_status_label.text = ""

	var stock: Variant = await _hub.invoke("GetShopStock", [])
	var ledger: Variant = await _hub.invoke("GetLedger", [])
	var roster: Variant = await _hub.invoke("GetRoster", [])

	_set_busy(false)

	if not (stock is Dictionary):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not reach this stall -- check your connection."
		return

	_apply_stock(stock)
	_apply_ledger(ledger if ledger is Dictionary else {})
	_team = (roster if roster is Array else []).filter(func(a: Variant): return a is Dictionary and bool(a.get("inTeam", false)))

	_render_rest()
	_render_wares()


func _apply_stock(stock: Dictionary) -> void:
	_ember_slots = []
	for slot: Variant in stock.get("emberSlots", []):
		if slot is Dictionary:
			_ember_slots.append({"index": int(slot.get("index", 0)), "color": str(slot.get("color", ""))})
	_artifact_name = str(stock.get("artifactName", ""))
	_artifact_description = str(stock.get("artifactDescription", ""))
	_ember_price = int(stock.get("emberPrice", 0))
	_artifact_price = int(stock.get("artifactPrice", 0))
	_rest_wisp_cost = int(stock.get("restWispCost", 0))


func _apply_ledger(ledger: Dictionary) -> void:
	var balances: Dictionary = ledger.get("balances", {})
	_wisp_balance = int(balances.get("Wisp", 0))


func _set_busy(value: bool) -> void:
	_busy = value
	_leave_button.disabled = value


# ---- Rest ----

func _render_rest() -> void:
	for child in _rest_team_list.get_children():
		child.free()

	var any_below_max := false
	for a: Dictionary in _team:
		var current_hp := int(a.get("currentHp", 0))
		var max_hp := int(a.get("maxHp", 0))
		if current_hp < max_hp: any_below_max = true

		var row := Label.new()
		row.text = "%s  HP %d/%d" % [str(a.get("name", "?")), current_hp, max_hp]
		row.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		row.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
		_rest_team_list.add_child(row)

	_rest_desc_label.text = "Heal the whole team %d%% max HP." % int(round(0.40 * 100))

	var affordable := _wisp_balance >= _rest_wisp_cost
	var can_rest := affordable and any_below_max

	for child in _rest_button_slot.get_children():
		child.free()
	var button := _build_gradient_button("Rest (%d Wisp)" % _rest_wisp_cost, GRADIENT_BUTTON_A, GRADIENT_BUTTON_B, COLOR_BUTTON_TEXT, _on_rest_pressed)
	button.modulate.a = 1.0 if can_rest else 0.4
	_set_button_enabled(button, can_rest and not _busy)
	_rest_button_slot.add_child(button)

	if not affordable:
		_rest_reason_label.text = "Not enough Wisp (need %d, have %d)." % [_rest_wisp_cost, _wisp_balance]
	elif not any_below_max:
		_rest_reason_label.text = "Team already at full HP."
	else:
		_rest_reason_label.text = ""
	_rest_reason_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))


func _on_rest_pressed() -> void:
	if _busy: return
	_set_busy(true)
	_status_label.text = ""

	var result: Variant = await _hub.invoke("RestAtShop", [])

	_set_busy(false)
	if not (result is Dictionary):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not Rest -- check your Wisp and connection, then try again."
		return

	var wisp_spent := int(result.get("wispSpent", 0))
	_status_label.add_theme_color_override("font_color", Color(COLOR_SUCCESS))
	_status_label.text = "Rested for %d Wisp." % wisp_spent

	var ledger: Variant = await _hub.invoke("GetLedger", [])
	_apply_ledger(ledger if ledger is Dictionary else {})
	var roster: Variant = await _hub.invoke("GetRoster", [])
	_team = (roster if roster is Array else []).filter(func(a: Variant): return a is Dictionary and bool(a.get("inTeam", false)))

	_render_rest()
	_render_wares() # Wisp balance changed -- Wares affordability may too


# ---- Wares ----

func _render_wares() -> void:
	for child in _ember_row.get_children():
		child.free()
	for slot: Dictionary in _ember_slots:
		_ember_row.add_child(_build_ember_card(slot))

	for child in _artifact_slot.get_children():
		child.free()
	if _artifact_name != "":
		_artifact_slot.add_child(_build_artifact_card())


func _build_ember_card(slot: Dictionary) -> Control:
	var index: int = slot.get("index", 0)
	var color: String = slot.get("color", "")

	var card := _make_static_panel(Vector2(0, 0))
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var content := VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 4)
	card.add_child(content)

	var icon_wrap := CenterContainer.new()
	icon_wrap.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(icon_wrap)

	var icon := Control.new()
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", "sun")
	icon.custom_minimum_size = Vector2(28, 28)
	icon.set("icon_size", 28.0)
	icon_wrap.add_child(icon)

	if color == "":
		icon.set("icon_color", Color(COLOR_TEXT_MUTED))
		var sold_label := Label.new()
		sold_label.text = "Sold this visit."
		sold_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		sold_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		sold_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
		content.add_child(sold_label)
		return card

	icon.set("icon_color", Color(EMBER_COLOR_TINTS.get(color, "888888")))

	var name_label := Label.new()
	name_label.text = "%s Ember" % color
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	name_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	content.add_child(name_label)

	var affordable := _wisp_balance >= _ember_price
	var button := _build_gradient_button("%d Wisp" % _ember_price, GRADIENT_BUTTON_A, GRADIENT_BUTTON_B, COLOR_BUTTON_TEXT, _on_buy_ember_pressed.bind(index))
	button.custom_minimum_size = Vector2(0, 36)
	button.modulate.a = 1.0 if affordable else 0.4
	_set_button_enabled(button, affordable and not _busy)
	content.add_child(button)

	if not affordable:
		var reason := Label.new()
		reason.text = "Need %d, have %d." % [_ember_price, _wisp_balance]
		reason.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		reason.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		reason.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		reason.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
		content.add_child(reason)

	return card


func _on_buy_ember_pressed(slot_index: int) -> void:
	if _busy: return
	_set_busy(true)
	_status_label.text = ""

	var bought_color := ""
	for slot: Dictionary in _ember_slots:
		if int(slot.get("index", -1)) == slot_index:
			bought_color = str(slot.get("color", ""))

	var result: Variant = await _hub.invoke("BuyWaresEmber", [{"slotIndex": slot_index}])

	_set_busy(false)
	if not (result is Array):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not buy that Ember -- check your Wisp and connection, then try again."
		return

	for slot: Dictionary in _ember_slots:
		if int(slot.get("index", -1)) == slot_index:
			slot["color"] = ""

	var ledger: Variant = await _hub.invoke("GetLedger", [])
	_apply_ledger(ledger if ledger is Dictionary else {})

	_render_wares()
	_render_rest() # Wisp balance changed -- Rest affordability may too
	_open_augment_picker(bought_color)


func _build_artifact_card() -> Control:
	var card := _make_static_panel(Vector2(0, 0))

	var content := HBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 10)
	card.add_child(content)

	var icon_slot_style := StyleBoxFlat.new()
	icon_slot_style.bg_color = Color(0, 0, 0, 0.3)
	icon_slot_style.set_corner_radius_all(8)

	var icon_slot := PanelContainer.new()
	icon_slot.custom_minimum_size = Vector2(44, 44)
	icon_slot.add_theme_stylebox_override("panel", icon_slot_style)
	content.add_child(icon_slot)

	var icon_center := CenterContainer.new()
	icon_slot.add_child(icon_center)

	var icon := Control.new()
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", ARTIFACT_ICONS.get(_artifact_name, "gift"))
	icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	icon.custom_minimum_size = Vector2(22, 22)
	icon.set("icon_size", 22.0)
	icon_center.add_child(icon)

	var text_col := VBoxContainer.new()
	text_col.mouse_filter = Control.MOUSE_FILTER_IGNORE
	text_col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	text_col.add_theme_constant_override("separation", 2)
	content.add_child(text_col)

	var name_label := Label.new()
	name_label.text = _artifact_name
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	name_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)
	text_col.add_child(name_label)

	var desc_label := Label.new()
	desc_label.text = _artifact_description
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	desc_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	text_col.add_child(desc_label)

	var button_col := VBoxContainer.new()
	button_col.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(button_col)

	var affordable := _wisp_balance >= _artifact_price
	var button := _build_gradient_button("%d Wisp" % _artifact_price, GRADIENT_BUTTON_A, GRADIENT_BUTTON_B, COLOR_BUTTON_TEXT, _on_buy_artifact_pressed)
	button.custom_minimum_size = Vector2(110, 36)
	button.modulate.a = 1.0 if affordable else 0.4
	_set_button_enabled(button, affordable and not _busy)
	button_col.add_child(button)

	if not affordable:
		var reason := Label.new()
		reason.text = "Need %d, have %d." % [_artifact_price, _wisp_balance]
		reason.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		reason.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		reason.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
		button_col.add_child(reason)

	return card


func _on_buy_artifact_pressed() -> void:
	if _busy: return
	_set_busy(true)
	_status_label.text = ""

	var result: Variant = await _hub.invoke("BuyWaresArtifact", [])

	_set_busy(false)
	if not (result is Dictionary):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not buy that Artifact -- check your Wisp and connection, then try again."
		return

	_status_label.add_theme_color_override("font_color", Color(COLOR_SUCCESS))
	_status_label.text = "Claimed %s!" % str(result.get("artifactName", _artifact_name))

	_artifact_name = ""
	_artifact_description = ""

	var ledger: Variant = await _hub.invoke("GetLedger", [])
	_apply_ledger(ledger if ledger is Dictionary else {})

	_render_wares()
	_render_rest()


# ---- Augment (shared child-panel, see this file's own top-of-file comment) ----

func _open_augment_picker(ember_color: String) -> void:
	for child in _augment_overlay.get_children():
		child.free()

	var instance := AUGMENT_PICKER_SCENE.instantiate() as Control
	_augment_overlay.add_child(instance)
	_augment_overlay.visible = true
	instance.connect("resolved", _on_augment_picker_resolved.bind(instance))
	instance.call("start", _hub, ember_color)


func _on_augment_picker_resolved(instance: Control) -> void:
	_augment_overlay.visible = false
	instance.queue_free()

	var ledger: Variant = await _hub.invoke("GetLedger", [])
	_apply_ledger(ledger if ledger is Dictionary else {})
	_render_wares()
	_render_rest()


# ---- Leave ----

func _on_leave_pressed() -> void:
	resolved.emit()


# ---- Shared small-widget builders (same pattern as reforge_node.gd's own copies) ----

func _make_static_panel(min_size: Vector2) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = min_size

	var style := StyleBoxFlat.new()
	style.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	style.set_border_width_all(1)
	style.set_corner_radius_all(10)
	style.set_content_margin_all(10)
	panel.add_theme_stylebox_override("panel", style)

	return panel


func _set_button_enabled(button_wrapper: Control, enabled: bool) -> void:
	var button: Button = button_wrapper.get_node("Button")
	button.disabled = not enabled
	button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND if enabled else Control.CURSOR_ARROW


## Same TextureRect+transparent-Button gradient technique as resource_node.gd/treasure_node.gd/
## reforge_node.gd's own copies -- see resource_node.gd's own comment for why (StyleBoxFlat has no
## gradient fill in Godot 4). The child Button is named "Button" explicitly so _set_button_enabled
## above can find it by path after the fact (needed here, unlike the other 3 screens, since Wares
## buttons must be able to flip disabled/enabled again as Wisp balance changes without being rebuilt).
func _build_gradient_button(label_text: String, color_a: String, color_b: String, text_color: String, on_pressed: Callable) -> Control:
	var wrapper := Control.new()
	wrapper.custom_minimum_size = Vector2(120, 40)

	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color(color_a), Color(color_b)])
	var tex := GradientTexture2D.new()
	tex.gradient = gradient
	tex.fill = GradientTexture2D.FILL_LINEAR
	tex.fill_from = Vector2(0.0, 0.0)
	tex.fill_to = Vector2(1.0, 1.0)
	tex.width = 120
	tex.height = 40

	var bg := TextureRect.new()
	bg.texture = tex
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	wrapper.add_child(bg)

	var button := Button.new()
	button.name = "Button"
	button.set_anchors_preset(Control.PRESET_FULL_RECT)
	button.text = label_text
	button.flat = true
	button.focus_mode = Control.FOCUS_NONE
	button.add_theme_color_override("font_color", Color(text_color))
	button.add_theme_color_override("font_color_hover", Color(text_color))
	button.add_theme_color_override("font_color_pressed", Color(text_color))
	button.add_theme_color_override("font_color_disabled", Color(text_color, 0.7))
	button.add_theme_stylebox_override("normal", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("hover", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("pressed", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("disabled", StyleBoxEmpty.new())
	button.pressed.connect(on_pressed)
	wrapper.add_child(button)

	return wrapper


# ---- Theme ----

func _apply_theme() -> void:
	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color(COLOR_GRADIENT_TOP), Color(COLOR_GRADIENT_MID), Color(COLOR_GRADIENT_BOTTOM)])
	gradient.offsets = PackedFloat32Array([0.0, 0.5, 1.0])

	var gradient_texture := GradientTexture2D.new()
	gradient_texture.gradient = gradient
	gradient_texture.fill = GradientTexture2D.FILL_RADIAL
	gradient_texture.width = 512
	gradient_texture.height = 512
	gradient_texture.fill_from = Vector2(0.5, 0.3)
	gradient_texture.fill_to = Vector2(1.0, 0.3)
	_background.texture = gradient_texture

	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(Color(COLOR_CARD_BG), 0.85)
	card_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(12)
	$CenterContainer/Card.add_theme_stylebox_override("panel", card_style)

	_title_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_title_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SUBHEADER)
	_subtitle_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_subtitle_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)

	var rest_style := StyleBoxFlat.new()
	rest_style.bg_color = Color(0, 0, 0, 0.25)
	rest_style.set_corner_radius_all(10)
	_rest_panel.add_theme_stylebox_override("panel", rest_style)
	_rest_header_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_rest_header_label.add_theme_font_size_override("font_size", UiTheme.SIZE_LABEL)
	_rest_desc_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_rest_desc_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)
	_rest_reason_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)

	_wares_header_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_wares_header_label.add_theme_font_size_override("font_size", UiTheme.SIZE_LABEL)

	_status_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)

	_leave_button.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_leave_button.add_theme_color_override("font_color_hover", Color(COLOR_TEXT_CREAM))
	_leave_button.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)
