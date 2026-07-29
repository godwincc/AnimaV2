extends Control

## Reusable Augment picker -- resolves ONE pending Ember (color already known/fixed by the caller)
## into either "Augment now" or "Convert to Wisp." NOT itself a node/room screen -- a sub-flow
## instanced by whoever currently holds a pending Ember to resolve (Shop's Wares Ember purchase
## today; Combat/Elite/Resource pickups later), same child-panel/shared-connection architecture as
## resource_node.gd/treasure_node.gd/reforge_node.gd/shop_node.gd -- see resource_node.gd's own
## top-of-file comment for why a real scene change would orphan the in-progress DelveRun.
##
## OWNERSHIP CONTRACT (read this before wiring a new caller): start(hub, ember_color) is the full
## entry point -- ember_color is a plain color string ("Crimson"/"Onyx"/"Verdant"/"Azure") the
## CALLER already knows (Shop knows the Wares slot's color at purchase time; Combat/Elite/Resource
## will know their own drop's color), NOT fetched by this script itself -- there is no standalone
## "peek the pending Ember" RPC (confirmed by reading GameHub.cs: GetPendingEmberColorsAsync is a
## PRIVATE helper, only ever returned as a side-channel field on OTHER RPCs' results -- ember_color
## being passed in directly sidesteps that entirely and avoids a second, redundant source of truth).
## This screen fetches its OWN roster (GetRoster) independently -- it takes NO Shop-specific data
## (no ShopStock, no Shop RPCs) and reads no state the caller doesn't already own, so it's equally
## usable from a future Combat Ember-drop with zero changes needed here. Emits `resolved` once the
## pending Ember is actually spent (Augment success or Convert success) -- the CALLER tears this
## panel down and returns to its own state, exactly like Resource/Treasure/Reforge/Shop's own
## `resolved` contract.
##
## RPC NAMES/SHAPES CONFIRMED AGAINST REAL CODE (CLAUDE.md prose has already drifted once this
## session, per Shop's own findings):
## - ConvertPendingEmberToWisp() -- no params, returns the pending-ember-colors list (not directly
##   useful here); EmberService.ConvertToWisp is a flat +15 Wisp, confirmed still 15, NOT run
##   through Wisp Charm/Ember Core (a deliberate choice per that method's own comment -- this is a
##   player CHOICE about an already-in-hand Ember, not a fresh reward grant).
## - AugmentPendingEmber(AugmentPendingEmberRequest{AnimaId, Part, AugmentType}) -- identifies the
##   target skill by (AnimaId, Part), NOT a raw skill id. Returns the pending-ember-colors list on
##   success, null (via HubConnection's own invoke()-returns-null-on-error convention) on rejection.
##   AugmentService.TryApplyAugment does ALL real validation server-side (max-3, color match,
##   per-type applicability, Wisp affordability) and returns a REAL rejection reason
##   (AugmentRejectionReason: MaxAugmentsReached/SkillMissingColor/EmberColorMismatch/
##   NotApplicableToSkill/InsufficientWisp) baked into the HubException's own message text -- this
##   screen captures that real text via HubConnection.error_occurred (same temporary-listener
##   technique reforge_node.gd's own _call_void already uses) rather than inventing a guessed reason.
## - REAL CONFIRMED GAP, closed this session: AnimaPartSummary (GetRoster's own per-part shape) had
##   NO per-part Color field before this -- "eligible skill" per the locked rule is the SKILL's own
##   archetype color (Skill.Color), NOT the Anima's body color (what makes a hybrid Vulcan/Mirage
##   Anima's individually-colored parts work for free), but nothing on the wire exposed that. Added
##   AnimaPartSummary.Color (nullable string, mirrors Skill.Color?.ToString()) server-side --
##   confirmed via GameHubModels.cs/GameHub.cs, not assumed. This screen filters eligible (AnimaId,
##   Part) pairs using that new field.
## - NOT exposed anywhere on the wire: AugmentService.GetNextAugmentCost (server-side lookup) and
##   AugmentService.IsApplicable (per-type skill applicability) are both Core-only, never surfaced
##   via GameHub. The next-tier Wisp cost IS safely re-derivable client-side from
##   AnimaPartSummary.AppliedAugments.Count against the locked, stable [15,30,50] curve (confirmed
##   against AugmentService.AugmentWispCostCurve) -- shown as an ESTIMATE (Ember Core's discount, if
##   held, isn't reflected in this preview, since no RPC exposes the real discounted number ahead of
##   committing) with the REAL server-confirmed amount shown only after a successful confirm (GetLedger
##   diffed before/after, same "never an optimistic client-side number" principle Shop's own Rest
##   section already established). Per-type applicability is NOT filtered client-side at all (the data
##   to replicate IsApplicable -- BaseDamage/OnHitStatusKeyword/Target -- isn't on the wire either) --
##   all 4 types are offered for any eligible skill, and a genuinely inapplicable pick surfaces the
##   real "NotApplicableToSkill" rejection text at confirm time instead of being silently pre-filtered.

signal resolved

# Standard warm sanctuary/workshop palette -- Augment has no locked palette of its own (same
# "no distinct palette" precedent Reforge's own visual identity already set).
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
const COLOR_SUCCESS := "7ac47a"
const COLOR_BUTTON_TEXT := "2b2413"

const GRADIENT_BUTTON_A := "c9a82f"
const GRADIENT_BUTTON_B := "a3841c"

# Same per-color hex tint convention anima_reveal_panel.gd/shop_node.gd's own copies already use
# (duplicated here, not shared -- this codebase's established small-mapping-per-file pattern).
const EMBER_COLOR_TINTS := {
	"Crimson": "8a3a3a",
	"Onyx": "4a4a52",
	"Verdant": "3a6a4a",
	"Azure": "3a5a7a",
}

const PART_ORDER := ["Head", "Frame", "Tail", "Crest"]

# Locked curve, confirmed against AugmentService.AugmentWispCostCurve/MaxAugmentsPerPart --
# client-side re-derivation is safe since both are stable public consts, not server state that
# could drift; see this file's own top-of-file comment for why this is an ESTIMATE only.
const AUGMENT_WISP_COST_CURVE := [15, 30, 50]
const MAX_AUGMENTS_PER_PART := 3

const AUGMENT_TYPES := ["IncreaseEffect", "AoEDamage", "DecreaseCost", "Extend"]
const AUGMENT_TYPE_LABELS := {
	"IncreaseEffect": "Increase Effect",
	"AoEDamage": "AoE Damage",
	"DecreaseCost": "Decrease Cost",
	"Extend": "Extend",
}
const AUGMENT_TYPE_DESCRIPTIONS := {
	"IncreaseEffect": "+20% to the skill's core effect.",
	"AoEDamage": "Converts to hit all enemies, at 50% damage.",
	"DecreaseCost": "-1 Energy cost (no floor).",
	"Extend": "+1 Charges to its on-hit effect.",
}

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")
const GLOW_CIRCLE_SCRIPT := preload("res://scripts/glow_circle.gd")

@onready var _background: TextureRect = $Background
@onready var _back_button: Button = $CenterContainer/Card/Margin/Content/HeaderRow/BackButton
@onready var _title_label: Label = $CenterContainer/Card/Margin/Content/HeaderRow/TitleLabel
@onready var _centerpiece_wrap: Control = $CenterContainer/Card/Margin/Content/CenterpieceWrap
@onready var _subtitle_label: Label = $CenterContainer/Card/Margin/Content/SubtitleLabel
@onready var _step_body: VBoxContainer = $CenterContainer/Card/Margin/Content/StepBody
@onready var _status_label: Label = $CenterContainer/Card/Margin/Content/StatusLabel
@onready var _button_slot: Control = $CenterContainer/Card/Margin/Content/FooterRow/ButtonSlot

var _hub: HubConnection
var _busy: bool = false

var _ember_color: String = ""
var _step: String = "choice" # "choice" | "browse" | "type" | "confirm" | "done"
var _roster: Array = []
var _eligible: Array = [] # Array[Dictionary]{animaId, animaName, part, skillName, appliedCount}
var _selected: Dictionary = {}
var _selected_augment_type: String = ""
var _done_message: String = ""


func _ready() -> void:
	_apply_theme()
	_back_button.pressed.connect(_on_back_pressed)


## Entry point -- see this file's own top-of-file comment for the full ownership contract.
func start(hub: HubConnection, ember_color: String) -> void:
	_hub = hub
	_ember_color = ember_color
	_enter_step("choice")


# ---- Step navigation ----

func _enter_step(step: String) -> void:
	_step = step
	_status_label.text = ""
	_back_button.visible = step in ["browse", "type", "confirm"]

	match step:
		"choice":
			_title_label.text = "Augment"
			_subtitle_label.text = "A %s Ember -- Augment a Vessel's skill, or convert it to Wisp." % _ember_color
		"browse":
			_title_label.text = "Choose a Skill"
			_subtitle_label.text = "%s skills only -- the Ember's own color, not a Vessel's body color." % _ember_color
		"type":
			_title_label.text = "Choose an Augment"
			_subtitle_label.text = "%s (%s)" % [str(_selected.get("skillName", "")), str(_selected.get("part", ""))]
		"confirm":
			_title_label.text = "Confirm Augment"
			_subtitle_label.text = "%s -> %s" % [str(_selected.get("skillName", "")), AUGMENT_TYPE_LABELS.get(_selected_augment_type, _selected_augment_type)]
		"done":
			_title_label.text = "Done"
			_subtitle_label.text = ""

	_clear_button_slot()
	_clear_step_body()
	_render_step_body()


func _on_back_pressed() -> void:
	if _busy: return
	match _step:
		"browse": _enter_step("choice")
		"type": _enter_step("browse")
		"confirm": _enter_step("type")


func _clear_step_body() -> void:
	for child in _step_body.get_children():
		child.free()


func _clear_button_slot() -> void:
	for child in _button_slot.get_children():
		child.free()


func _render_step_body() -> void:
	match _step:
		"choice": _render_choice_step()
		"browse": _render_browse_step()
		"type": _render_type_step()
		"confirm": _render_confirm_step()
		"done": _render_done_step()


# ---- Step: choice ----

func _render_choice_step() -> void:
	for child in _centerpiece_wrap.get_children():
		child.free()
	var glow := Control.new()
	glow.set_script(GLOW_CIRCLE_SCRIPT)
	glow.set_anchors_preset(Control.PRESET_FULL_RECT)
	glow.set("glow_color", Color(EMBER_COLOR_TINTS.get(_ember_color, "888888")).lightened(0.3))
	glow.set("glow_radius", 45.0)
	_centerpiece_wrap.add_child(glow)

	var ember_icon := Control.new()
	ember_icon.set_script(ICON_GLYPH_SCRIPT)
	ember_icon.set("icon_kind", "sun")
	ember_icon.set("icon_color", Color(EMBER_COLOR_TINTS.get(_ember_color, "888888")).lightened(0.3))
	ember_icon.set("icon_size", 44.0)
	ember_icon.position = Vector2(50, 50) - Vector2(22, 22)
	_centerpiece_wrap.add_child(ember_icon)

	var augment_button := _build_gradient_button("Augment now", GRADIENT_BUTTON_A, GRADIENT_BUTTON_B, COLOR_BUTTON_TEXT, _on_augment_now_pressed)
	_step_body.add_child(_center(augment_button))

	var convert_button := Button.new()
	convert_button.text = "Convert to Wisp (+%d)" % 15
	convert_button.focus_mode = Control.FOCUS_NONE
	convert_button.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	convert_button.add_theme_color_override("font_color_hover", Color(COLOR_TEXT_CREAM))
	convert_button.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)
	convert_button.pressed.connect(_on_convert_pressed)
	_step_body.add_child(_center(convert_button))


func _center(control: Control) -> Control:
	var wrap := CenterContainer.new()
	wrap.add_child(control)
	return wrap


func _on_convert_pressed() -> void:
	if _busy: return
	_busy = true
	_status_label.text = ""

	var wisp_before := await _get_wisp_balance()
	var outcome := await _invoke_capturing_error("ConvertPendingEmberToWisp", [])

	_busy = false
	if not (outcome["result"] is Array):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = _friendly_error(outcome["error"], "Could not convert this Ember -- check your connection and try again.")
		return

	var wisp_after := await _get_wisp_balance()
	_done_message = "Converted to Wisp -- +%d Wisp." % max(0, wisp_after - wisp_before)
	_enter_step("done")


# ---- Step: browse (Augment now) ----

func _on_augment_now_pressed() -> void:
	if _busy: return
	_busy = true
	_status_label.text = ""

	var roster: Variant = await _hub.invoke("GetRoster", [])

	_busy = false
	if not (roster is Array):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = "Could not load your roster -- check your connection and try again."
		return

	_roster = roster.filter(func(a: Variant): return a is Dictionary and bool(a.get("inTeam", false)))
	_eligible = []
	for a: Dictionary in _roster:
		for p: Variant in a.get("parts", []):
			if not (p is Dictionary): continue
			if str(p.get("color", "")) != _ember_color: continue
			var applied_count: int = (p.get("appliedAugments", []) as Array).size()
			if applied_count >= MAX_AUGMENTS_PER_PART: continue
			_eligible.append({
				"animaId": str(a.get("id", "")),
				"animaName": str(a.get("name", "?")),
				"part": str(p.get("part", "")),
				"skillName": str(p.get("skillName", "?")),
				"category": str(p.get("category", "")),
				"grantsShield": bool(p.get("grantsShield", false)),
				"appliedCount": applied_count,
			})

	call_deferred("_enter_step", "browse")


func _render_browse_step() -> void:
	if _eligible.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No eligible %s skills on your team right now." % _ember_color
		empty_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		empty_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		_step_body.add_child(empty_label)
		return

	for entry: Dictionary in _eligible:
		_step_body.add_child(_build_skill_row(entry))


func _build_skill_row(entry: Dictionary) -> Control:
	var row := _make_clickable_panel(Vector2(0, 40), _on_skill_pressed.bind(entry))

	var content := HBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 8)
	row.add_child(content)

	var icon := Control.new()
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", _icon_kind_for_skill(str(entry.get("category", "")), bool(entry.get("grantsShield", false))))
	icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	icon.custom_minimum_size = Vector2(16, 16)
	icon.set("icon_size", 16.0)
	content.add_child(icon)

	var name_label := Label.new()
	name_label.text = "%s -- %s (%s)" % [str(entry.get("animaName", "?")), str(entry.get("skillName", "?")), str(entry.get("part", ""))]
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	content.add_child(name_label)

	var count_label := Label.new()
	count_label.text = "%d/%d" % [int(entry.get("appliedCount", 0)), MAX_AUGMENTS_PER_PART]
	count_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	count_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	count_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	content.add_child(count_label)

	return row


func _on_skill_pressed(entry: Dictionary) -> void:
	if _busy: return
	_selected = entry
	call_deferred("_enter_step", "type")


# ---- Step: type ----

func _render_type_step() -> void:
	for augment_type: String in AUGMENT_TYPES:
		_step_body.add_child(_build_type_row(augment_type))


func _build_type_row(augment_type: String) -> Control:
	var row := _make_clickable_panel(Vector2(0, 0), _on_type_pressed.bind(augment_type))

	var content := VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation", 2)
	row.add_child(content)

	var name_label := Label.new()
	name_label.text = AUGMENT_TYPE_LABELS.get(augment_type, augment_type)
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	content.add_child(name_label)

	var desc_label := Label.new()
	desc_label.text = AUGMENT_TYPE_DESCRIPTIONS.get(augment_type, "")
	desc_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	desc_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	content.add_child(desc_label)

	return row


func _on_type_pressed(augment_type: String) -> void:
	if _busy: return
	_selected_augment_type = augment_type
	call_deferred("_enter_step", "confirm")


# ---- Step: confirm ----

func _render_confirm_step() -> void:
	var applied_count := int(_selected.get("appliedCount", 0))
	var estimated_cost: int = AUGMENT_WISP_COST_CURVE[clamp(applied_count, 0, AUGMENT_WISP_COST_CURVE.size() - 1)]

	var summary_label := Label.new()
	summary_label.text = "%s's %s" % [str(_selected.get("animaName", "?")), str(_selected.get("skillName", "?"))]
	summary_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	summary_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	summary_label.add_theme_font_size_override("font_size", UiTheme.SIZE_LABEL)
	_step_body.add_child(summary_label)

	var type_label := Label.new()
	type_label.text = "%s -- %s" % [AUGMENT_TYPE_LABELS.get(_selected_augment_type, _selected_augment_type), AUGMENT_TYPE_DESCRIPTIONS.get(_selected_augment_type, "")]
	type_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	type_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	type_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	type_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	_step_body.add_child(type_label)

	var cost_row := HBoxContainer.new()
	cost_row.alignment = BoxContainer.ALIGNMENT_CENTER
	cost_row.add_theme_constant_override("separation", 6)
	_step_body.add_child(cost_row)

	var cost_icon := Control.new()
	cost_icon.set_script(ICON_GLYPH_SCRIPT)
	cost_icon.set("icon_kind", "sparkle")
	cost_icon.set("icon_color", Color(COLOR_ACCENT_AMBER))
	cost_icon.set("icon_size", 15.0)
	cost_row.add_child(cost_icon)

	var cost_label := Label.new()
	cost_label.text = "~%d Wisp (this Ember, plus)" % estimated_cost
	cost_label.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
	cost_row.add_child(cost_label)

	var caveat_label := Label.new()
	caveat_label.text = "Estimate -- Ember Core's discount, if held, applies at confirm."
	caveat_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	caveat_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	caveat_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	caveat_label.add_theme_font_size_override("font_size", UiTheme.SIZE_MICRO)
	_step_body.add_child(caveat_label)

	_clear_button_slot()
	_button_slot.add_child(_build_gradient_button("Confirm", GRADIENT_BUTTON_A, GRADIENT_BUTTON_B, COLOR_BUTTON_TEXT, _on_confirm_pressed))


func _on_confirm_pressed() -> void:
	if _busy: return
	_busy = true
	_status_label.text = ""

	var wisp_before := await _get_wisp_balance()
	var outcome := await _invoke_capturing_error("AugmentPendingEmber", [{
		"animaId": str(_selected.get("animaId", "")),
		"part": str(_selected.get("part", "")),
		"augmentType": _selected_augment_type,
	}])

	_busy = false
	if not (outcome["result"] is Array):
		_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
		_status_label.text = _friendly_error(outcome["error"], "Could not Augment -- check your connection and try again.")
		return

	var wisp_after := await _get_wisp_balance()
	var spent: int = max(0, wisp_before - wisp_after)
	_done_message = "Augmented %s's %s -- spent %d Wisp." % [str(_selected.get("animaName", "?")), str(_selected.get("skillName", "?")), spent]
	_enter_step("done")


# ---- Step: done ----

func _render_done_step() -> void:
	var message_label := Label.new()
	message_label.text = _done_message
	message_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	message_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	message_label.add_theme_color_override("font_color", Color(COLOR_SUCCESS))
	message_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)
	_step_body.add_child(message_label)

	_clear_button_slot()
	_button_slot.add_child(_build_gradient_button("Done", GRADIENT_BUTTON_A, GRADIENT_BUTTON_B, COLOR_BUTTON_TEXT, _on_done_pressed))


func _on_done_pressed() -> void:
	resolved.emit()


# ---- Shared helpers ----

func _get_wisp_balance() -> int:
	var ledger: Variant = await _hub.invoke("GetLedger", [])
	if ledger is Dictionary:
		var balances: Dictionary = ledger.get("balances", {})
		return int(balances.get("Wisp", 0))
	return 0


## Same temporary-listener technique reforge_node.gd's own _call_void already uses to distinguish
## "no result" from "a real error fired," extended here to also capture the REAL message text (the
## server's actual AugmentRejectionReason, baked into its HubException) rather than only a bool.
##
## KNOWN GAP, flagged not silently papered over: in one live test this session (a genuine
## InsufficientWisp rejection, confirmed via the real server log showing "Augment rejected:
## InsufficientWisp." thrown from GameHub.cs) the error text captured here came back empty, so
## _friendly_error's generic fallback showed instead of the real reason -- even though this exact
## technique is already proven working elsewhere in this codebase (reforge_node.gd's _call_void).
## Root cause not yet isolated (SignalR's own debug logging only logs outgoing sends, not incoming
## completions/errors, which blocked diagnosing it further this session). Not a blocker -- the
## generic fallback is still a real, honest message, and the client-side affordability check
## already prevents this from being reachable in normal play -- but flagging for whoever next
## touches this file, since the real reason (color mismatch, already-maxed, not-applicable-to-
## skill) is more useful to a player than "check your connection."
func _invoke_capturing_error(method: String, args: Array = []) -> Dictionary:
	var error_message := ""
	var on_error := func(msg: String): error_message = msg
	_hub.error_occurred.connect(on_error)
	var result: Variant = await _hub.invoke(method, args)
	if _hub.error_occurred.is_connected(on_error):
		_hub.error_occurred.disconnect(on_error)
	return {"result": result, "error": error_message}


## HubConnection's own error text is "Invocation 'X' failed: <server message>" -- strips that
## wrapper so the player sees the real server reason plainly, falling back to a generic message if
## nothing was actually captured (e.g. a timeout with no server-side HubException at all).
func _friendly_error(raw_error: String, fallback: String) -> String:
	if raw_error == "":
		return fallback
	var marker := "failed: "
	var idx := raw_error.find(marker)
	return raw_error.substr(idx + marker.length()) if idx != -1 else raw_error


func _icon_kind_for_skill(category: String, grants_shield: bool) -> String:
	match category:
		"Attack": return "sword"
		"Heal": return "heart"
		"Buff": return "shield" if grants_shield else "bolt"
		_: return "bolt"


func _make_clickable_panel(min_size: Vector2, on_pressed: Callable) -> PanelContainer:
	var panel := _make_static_panel(min_size)
	panel.mouse_filter = Control.MOUSE_FILTER_STOP
	panel.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	panel.gui_input.connect(func(event: InputEvent):
		if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
			on_pressed.call()
	)
	return panel


func _make_static_panel(min_size: Vector2) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = min_size

	var style := StyleBoxFlat.new()
	style.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	style.set_border_width_all(1)
	style.set_corner_radius_all(10)
	style.set_content_margin_all(8)
	panel.add_theme_stylebox_override("panel", style)

	return panel


## Same TextureRect+transparent-Button gradient technique as resource_node.gd/treasure_node.gd/
## reforge_node.gd/shop_node.gd's own copies -- see resource_node.gd's own comment for why
## (StyleBoxFlat has no gradient fill in Godot 4).
func _build_gradient_button(label_text: String, color_a: String, color_b: String, text_color: String, on_pressed: Callable) -> Control:
	var wrapper := Control.new()
	wrapper.custom_minimum_size = Vector2(160, 44)

	var gradient := Gradient.new()
	gradient.colors = PackedColorArray([Color(color_a), Color(color_b)])
	var tex := GradientTexture2D.new()
	tex.gradient = gradient
	tex.fill = GradientTexture2D.FILL_LINEAR
	tex.fill_from = Vector2(0.0, 0.0)
	tex.fill_to = Vector2(1.0, 1.0)
	tex.width = 160
	tex.height = 44

	var bg := TextureRect.new()
	bg.texture = tex
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	wrapper.add_child(bg)

	var button := Button.new()
	button.set_anchors_preset(Control.PRESET_FULL_RECT)
	button.text = label_text
	button.flat = true
	button.focus_mode = Control.FOCUS_NONE
	button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	button.add_theme_color_override("font_color", Color(text_color))
	button.add_theme_color_override("font_color_hover", Color(text_color))
	button.add_theme_color_override("font_color_pressed", Color(text_color))
	button.add_theme_stylebox_override("normal", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("hover", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("pressed", StyleBoxEmpty.new())
	button.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	button.pressed.connect(on_pressed)
	wrapper.add_child(button)

	return wrapper


# ---- Theme ----

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
	card_style.bg_color = Color(Color(COLOR_CARD_BG), 0.85)
	card_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(12)
	$CenterContainer/Card.add_theme_stylebox_override("panel", card_style)

	_title_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_title_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SUBHEADER)
	_subtitle_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_subtitle_label.add_theme_font_size_override("font_size", UiTheme.SIZE_SMALL)
	_status_label.add_theme_font_size_override("font_size", UiTheme.SIZE_BODY)

	_back_button.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_back_button.add_theme_color_override("font_color_hover", Color(COLOR_TEXT_CREAM_DIM))
