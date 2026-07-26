extends Control

## The real Hub screen (replaces the old post_login.tscn "you're in" placeholder content --
## renamed to hub.tscn/hub.gd in the Starter Trio session, now filled in for real). Matches
## assets/screens/hub_parts_display.html exactly: header (welcome + roster-count subtitle,
## right-aligned resource bar), 3 destination cards (Sanctum/Weaving/Collection -- all placeholder
## targets, none of those real screens exist yet), a team row (real Aspect sprites for non-hybrid
## Anima, flat color-block fallback for Vulcan/Mirage) + the reusable amber Delve portal, and a
## "Last delve" summary bar.
##
## STILL checks GetPendingStarterReveal() first, before rendering any of the above -- this is the
## one piece of hub.gd's old placeholder-era logic that must not regress just because the screen's
## content changed. Covers both a brand-new account and a reconnect mid-naming.

const SERVER_WS_URL := "ws://localhost:5143/hubs/game"
const LOGIN_SCENE := "res://scenes/login.tscn"
const STARTER_REVEAL_SCENE := "res://scenes/starter_reveal.tscn"
const SANCTUM_SCENE := "res://scenes/sanctum.tscn"
const WEAVING_SCENE := "res://scenes/weaving.tscn"
const COLLECTION_SCENE := "res://scenes/collection.tscn"
const DELVE_SCENE := "res://scenes/delve.tscn"

# Warm sanctuary/workshop theme + card styling, matching hub_parts_display.html's own inline styles.
const COLOR_GRADIENT_TOP := "4a3a2e"
const COLOR_GRADIENT_MID := "2b2018"
const COLOR_GRADIENT_BOTTOM := "1a130e"
const COLOR_CARD_BG := "1e1610" # rgba(30,22,16,*) in the mock
const COLOR_CARD_BORDER := "c9b89e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_TEXT_CREAM_DIM := "e8cf9a"
const COLOR_TEXT_MUTED := "a89680"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_ECHO_DOT := "c4423a"
const COLOR_VESSEL_DOT := "3a8c4a"
const COLOR_ERROR := "e2554a"

# Skill-type icon color coding, per CLAUDE.md's Skill-type icon set (matches the mock's iconColor
# map exactly): sword=Attack, heart=Heal, shield=Shield-granting Buff, bolt=other Buff, diamond=
# passive Crest. Debuff/Move/Summon have no dedicated icon anywhere in CLAUDE.md's icon set -- they
# fall back to "bolt" (grouped with "other Buff" as the closest available generic-effect icon).
const ICON_COLORS := {
	"sword": "e0736a",
	"shield": "7aa8d8",
	"heart": "6cb87c",
	"bolt": "e8b95a",
	"diamond": "e8a03a",
}

# Real Aspect sprites (locked this session) -- Vulcan/Mirage (hybrids) have no single Aspect sprite
# of their own and fall back to a flat color-block placeholder until/unless hybrid art is
# commissioned (flagged explicitly here, not silently defaulted to one parent color).
const ASPECT_SPRITES := {
	"Crimson": "res://assets/aspects/crimson.png",
	"Onyx": "res://assets/aspects/onyx.png",
	"Verdant": "res://assets/aspects/verdant.png",
	"Azure": "res://assets/aspects/azure.png",
}
const HYBRID_FALLBACK_TINTS := {
	"Vulcan": "6a3a52", # Onyx+Crimson blend, placeholder tint only
	"Mirage": "3a5a6a", # Verdant+Azure blend, placeholder tint only
}

const ICON_GLYPH_SCRIPT := preload("res://scripts/icon_glyph.gd")

@onready var _background: TextureRect = $Background
@onready var _status_label: Label = $Margin/Content/StatusLabel
@onready var _welcome_label: Label = $Margin/Content/HeaderRow/WelcomeBox/WelcomeLabel
@onready var _subtitle_label: Label = $Margin/Content/HeaderRow/WelcomeBox/SubtitleLabel
@onready var _wisp_label: Label = $Margin/Content/HeaderRow/ResourceBar/WispBox/WispLabel
@onready var _echo_label: Label = $Margin/Content/HeaderRow/ResourceBar/EchoBox/EchoLabel
@onready var _vessel_label: Label = $Margin/Content/HeaderRow/ResourceBar/VesselBox/VesselLabel
@onready var _sanctum_card: Button = $Margin/Content/DestinationGrid/SanctumCard
@onready var _weaving_card: Button = $Margin/Content/DestinationGrid/WeavingCard
@onready var _collection_card: Button = $Margin/Content/DestinationGrid/CollectionCard
@onready var _team_row: HBoxContainer = $Margin/Content/TeamPortalRow/TeamRow
@onready var _delve_portal: DelvePortal = $Margin/Content/TeamPortalRow/DelvePortal
@onready var _last_delve_text: Label = $Margin/Content/LastDelveBar/Margin/Content/LastDelveText

var _hub: HubConnection


func _ready() -> void:
	_apply_theme()
	_sanctum_card.pressed.connect(func(): get_tree().change_scene_to_file(SANCTUM_SCENE))
	_weaving_card.pressed.connect(func(): get_tree().change_scene_to_file(WEAVING_SCENE))
	_collection_card.pressed.connect(func(): get_tree().change_scene_to_file(COLLECTION_SCENE))
	_delve_portal.pressed.connect(_on_delve_pressed)

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

	# Starter-trio check (UNCHANGED from the placeholder-era hub.gd) -- must happen before
	# rendering any real Hub content, so a fresh/resuming account never sees the Hub while it still
	# has unnamed starter Anima. GetPendingStarterReveal returns an empty array (not null) when
	# nothing is pending, per its own contract.
	var pending: Variant = await _hub.invoke("GetPendingStarterReveal", [])
	if pending is Array and pending.size() > 0:
		_set_status("%d starter Anima still need names -- opening the reveal screen." % pending.size(), false)
		get_tree().change_scene_to_file(STARTER_REVEAL_SCENE)
		return

	_set_status("", false)
	await _load_hub_content()


func _load_hub_content() -> void:
	var roster: Variant = await _hub.invoke("GetRoster", [])
	var ledger: Variant = await _hub.invoke("GetLedger", [])
	var last_delve: Variant = await _hub.invoke("GetLastDelveSummary", [])

	_render_header(roster, ledger)
	_render_team(roster)
	_render_last_delve(last_delve)


func _render_header(roster: Variant, ledger: Variant) -> void:
	_welcome_label.text = "Welcome back"
	var count: int = roster.size() if roster is Array else 0
	_subtitle_label.text = "%d anima%s in your roster" % [count, "" if count == 1 else "s"]

	var balances: Dictionary = ledger.get("balances", {}) if ledger is Dictionary else {}
	_wisp_label.text = str(int(balances.get("Wisp", 0)))
	_echo_label.text = str(int(balances.get("EchoShard", 0)))
	_vessel_label.text = str(int(balances.get("VesselShard", 0)))
	# NOTE: the hub_parts_display.html mock has a 4th resource-bar entry (a ti-hexagon icon + a
	# count of 1) alongside Wisp/EchoShard/VesselShard -- Anima.Core.Economy.ResourceType only
	# defines those 3 resource types (confirmed by reading the enum, not assumed), so that 4th mock
	# entry doesn't correspond to anything real and is deliberately NOT reproduced here rather than
	# wiring up a fake/hardcoded "1".


func _render_team(roster: Variant) -> void:
	# free(), not queue_free() -- queue_free() defers removal to end-of-frame, so a second
	# _render_team call in the same frame/tight sequence (e.g. right after a fresh SetTeam) would
	# still see the stale children when rebuilding, leaving duplicates behind. These are plain
	# freshly-built UI nodes with nothing else holding a reference or mid-signal against them, so
	# immediate freeing is safe here.
	for child in _team_row.get_children():
		child.free()

	var team: Array = []
	if roster is Array:
		for a: Variant in roster:
			if a is Dictionary and bool(a.get("inTeam", false)):
				team.append(a)

	if team.is_empty():
		var empty_label := Label.new()
		empty_label.text = "No team selected yet."
		empty_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
		empty_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		empty_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		empty_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		_team_row.add_child(empty_label)
		return

	for a: Dictionary in team:
		_team_row.add_child(_build_team_card(a))


func _build_team_card(anima: Dictionary) -> Control:
	var card := VBoxContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.add_theme_constant_override("separation", 5)

	var color_name: String = str(anima.get("color", ""))

	var portrait_wrap := AspectRatioContainer.new()
	portrait_wrap.ratio = 1.0
	# AspectRatioContainer has no natural minimum size of its own -- without an explicit
	# custom_minimum_size, the parent VBoxContainer gives it ~zero height and the portrait
	# (whatever's inside it) never becomes visible at all. Real bug, found via an actual
	# screenshot (the earlier automated driver only checked node/texture PROPERTIES, never
	# rendered appearance -- see CLAUDE.md's Hub Screen section for the same class of gap this
	# also caught in AnimaRevealPanel's own Portrait node).
	portrait_wrap.custom_minimum_size = Vector2(0, 90)
	portrait_wrap.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.add_child(portrait_wrap)

	if ASPECT_SPRITES.has(color_name):
		var portrait := TextureRect.new()
		portrait.texture = load(ASPECT_SPRITES[color_name])
		# EXPAND_IGNORE_SIZE (not the proportional modes) -- AspectRatioContainer already owns
		# sizing/aspect for this child; a proportional expand_mode conflicts with that and Godot
		# logs a warning ("Proportional TextureRect is currently not supported inside
		# AspectRatioContainer") if set.
		portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		portrait.stretch_mode = TextureRect.STRETCH_SCALE
		# Renders as a plain square -- TextureRect has no built-in corner-radius clip the way a
		# PanelContainer's StyleBox does, so this is a cosmetic simplification vs. the mock's
		# rounded corners, not a functional gap.
		portrait_wrap.add_child(portrait)
	else:
		# Hybrid (Vulcan/Mirage) fallback -- no single Aspect sprite exists for a hybrid body
		# color, so this is a flat color-block placeholder until/unless hybrid art is commissioned.
		var fallback := ColorRect.new()
		fallback.color = Color(HYBRID_FALLBACK_TINTS.get(color_name, "555555"))
		portrait_wrap.add_child(fallback)

	var name_label := Label.new()
	name_label.text = str(anima.get("name", "?"))
	name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	name_label.add_theme_font_size_override("font_size", 12)
	card.add_child(name_label)

	var parts_list := VBoxContainer.new()
	parts_list.add_theme_constant_override("separation", 2)
	card.add_child(parts_list)

	var parts: Array = anima.get("parts", [])
	for p: Variant in parts:
		if p is Dictionary:
			parts_list.add_child(_build_part_row(p))

	return card


func _build_part_row(part: Dictionary) -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 4)

	var icon_kind := _icon_kind_for_part(part)
	var icon := Control.new()
	icon.set_script(ICON_GLYPH_SCRIPT)
	icon.set("icon_kind", icon_kind)
	icon.set("icon_color", Color(ICON_COLORS.get(icon_kind, "e8a03a")))
	icon.set("icon_size", 11.0)
	row.add_child(icon)

	var label := Label.new()
	label.text = str(part.get("skillName", "--"))
	label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	label.add_theme_font_size_override("font_size", 10)
	row.add_child(label)

	return row


# Icon-kind resolution, per CLAUDE.md's Skill-type icon set: Crest is ALWAYS diamond regardless of
# category (the passive-icon rule); Attack->sword, Heal->heart, Buff splits on GrantsShield
# (shield vs bolt); everything else (Debuff/Move/Summon) falls back to bolt, the closest generic
# "effect" icon -- CLAUDE.md never defines a dedicated icon for those 3 categories.
func _icon_kind_for_part(part: Dictionary) -> String:
	if str(part.get("part", "")) == "Crest":
		return "diamond"

	var category := str(part.get("category", ""))
	match category:
		"Attack": return "sword"
		"Heal": return "heart"
		"Buff": return "shield" if bool(part.get("grantsShield", false)) else "bolt"
		_: return "bolt"


func _render_last_delve(last_delve: Variant) -> void:
	if last_delve is String and last_delve != "":
		_last_delve_text.text = last_delve
	else:
		_last_delve_text.text = "You haven't delved yet -- step through the portal to begin your first."


func _on_delve_pressed() -> void:
	# Just navigates, same one-liner pattern as the Sanctum/Weaving/Collection cards above --
	# delve.tscn calls StartDelve() itself once its own connection is live, rather than this screen
	# calling it and handing off a result. See delve.gd's own top-of-file comment for why: this
	# codebase's "one HubConnection per scene" pattern means a DelveRun started on THIS connection
	# would already be orphaned (this connection torn down mid scene-change) by the time delve.tscn
	# could ever read it back -- a real, immediate bug the literal "call StartDelve here" brief
	# would have produced, not a stylistic preference.
	get_tree().change_scene_to_file(DELVE_SCENE)


func _set_status(text: String, is_error: bool) -> void:
	_status_label.visible = text != ""
	_status_label.remove_theme_color_override("font_color")
	_status_label.add_theme_color_override("font_color", Color(COLOR_ERROR) if is_error else Color(COLOR_TEXT_CREAM_DIM))
	_status_label.text = text


## Same connect-and-poll pattern as connectivity_test.gd/starter_reveal.gd's own _connect_hub --
## see connectivity_test.gd's comment for why a bare `await hub.connected` isn't safe to use here.
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

	_welcome_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	_welcome_label.add_theme_font_size_override("font_size", 18)
	_subtitle_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	_subtitle_label.add_theme_font_size_override("font_size", 12)

	for label: Label in [_wisp_label, _echo_label, _vessel_label]:
		label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))

	for card: Button in [_sanctum_card, _weaving_card, _collection_card]:
		_style_card_button(card)

	_style_panel($Margin/Content/LastDelveBar)
	_last_delve_label_style()

	_status_label.add_theme_font_size_override("font_size", 13)


## The 3 destination cards' shared look: dark semi-transparent panel (rgba(30,22,16,0.75)), thin
## warm-cream border (rgba(201,184,158,0.2)), rounded corners -- matches the mock's card styling
## exactly. Applied to a Button (not a plain Panel) so the whole card is clickable via `.pressed`
## without a second custom gui_input handler.
func _style_card_button(card: Button) -> void:
	var normal := StyleBoxFlat.new()
	normal.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	normal.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	normal.set_border_width_all(1)
	normal.set_corner_radius_all(12)
	normal.set_content_margin_all(16)

	var hover := normal.duplicate()
	hover.bg_color = Color(Color(COLOR_CARD_BG), 0.9)
	hover.border_color = Color(COLOR_ACCENT_AMBER, 0.4)

	card.add_theme_stylebox_override("normal", normal)
	card.add_theme_stylebox_override("hover", hover)
	card.add_theme_stylebox_override("pressed", hover)
	card.add_theme_stylebox_override("focus", StyleBoxEmpty.new())


func _style_panel(panel: PanelContainer) -> void:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(Color(COLOR_CARD_BG), 0.75)
	style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	style.set_border_width_all(1)
	style.set_corner_radius_all(12)
	panel.add_theme_stylebox_override("panel", style)


func _last_delve_label_style() -> void:
	var last_delve_label: Label = $Margin/Content/LastDelveBar/Margin/Content/LastDelveLabel
	last_delve_label.add_theme_color_override("font_color", Color(COLOR_TEXT_MUTED))
	last_delve_label.add_theme_font_size_override("font_size", 11)
	_last_delve_text.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	_last_delve_text.add_theme_font_size_override("font_size", 12)
