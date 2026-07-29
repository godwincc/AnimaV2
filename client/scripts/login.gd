extends Control

## Login / Create Account screen -- the first real UI screen for the client, on top of the
## connectivity layer proven in connectivity_test.gd. Talks to the real /api/auth/login and
## /api/auth/register REST endpoints (see Anima.Server's AuthController/AuthModels.cs -- the
## request shapes below are read directly from that contract, not assumed) via the shared
## ApiClient helper. On success, stores the JWT in the AuthState autoload and hands off to
## hub.tscn, which opens the real GameHub connection.
##
## Login only accepts a Username field server-side (LoginRequest has no email lookup) -- the
## field is labeled "Username" to match the real contract, not "Username/Email".
##
## NOTE (starter-trio feature): both Login and Create Account route to the SAME hub.tscn -- this
## screen deliberately does NOT assume "just registered" means "definitely has unnamed starter
## Anima" (or that logging in never does, e.g. a reconnect mid-naming). hub.tscn itself checks
## GetPendingStarterReveal() on entry and redirects to starter_reveal.tscn when needed, so this
## screen's own routing logic doesn't need to know or guess anything about starter-reveal state.

const LOGIN_PATH := "/api/auth/login"
const REGISTER_PATH := "/api/auth/register"
const HUB_SCENE := "res://scenes/hub.tscn"

# Warm sanctuary/workshop theme, per Anima_Design_Doc.md Section 9 / CLAUDE.md's ART DIRECTION.
const COLOR_GRADIENT_TOP := "4a3a2e"
const COLOR_GRADIENT_MID := "2b2018"
const COLOR_GRADIENT_BOTTOM := "1a130e"
const COLOR_CARD_BORDER := "c9b89e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_TEXT_CREAM_DIM := "e8cf9a"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_ERROR := "e2554a"

@onready var _background: TextureRect = $Background
@onready var _card: PanelContainer = $CenterContainer/Card
@onready var _title_label: Label = $CenterContainer/Card/Margin/Content/TitleLabel
@onready var _subtitle_label: Label = $CenterContainer/Card/Margin/Content/SubtitleLabel
@onready var _login_tab_button: Button = $CenterContainer/Card/Margin/Content/TabRow/LoginTabButton
@onready var _register_tab_button: Button = $CenterContainer/Card/Margin/Content/TabRow/RegisterTabButton
@onready var _login_fields: VBoxContainer = $CenterContainer/Card/Margin/Content/LoginFields
@onready var _login_username_edit: LineEdit = $CenterContainer/Card/Margin/Content/LoginFields/LoginUsernameEdit
@onready var _login_password_edit: LineEdit = $CenterContainer/Card/Margin/Content/LoginFields/LoginPasswordEdit
@onready var _register_fields: VBoxContainer = $CenterContainer/Card/Margin/Content/RegisterFields
@onready var _register_username_edit: LineEdit = $CenterContainer/Card/Margin/Content/RegisterFields/RegisterUsernameEdit
@onready var _register_email_edit: LineEdit = $CenterContainer/Card/Margin/Content/RegisterFields/RegisterEmailEdit
@onready var _register_password_edit: LineEdit = $CenterContainer/Card/Margin/Content/RegisterFields/RegisterPasswordEdit
@onready var _error_label: Label = $CenterContainer/Card/Margin/Content/ErrorLabel
@onready var _submit_button: Button = $CenterContainer/Card/Margin/Content/SubmitButton

var _is_register_tab: bool = false
var _request_in_flight: bool = false


func _ready() -> void:
	_apply_theme()

	_login_tab_button.pressed.connect(func(): _set_active_tab(false))
	_register_tab_button.pressed.connect(func(): _set_active_tab(true))
	_submit_button.pressed.connect(_on_submit_pressed)
	_login_password_edit.text_submitted.connect(func(_t): _on_submit_pressed())
	_register_password_edit.text_submitted.connect(func(_t): _on_submit_pressed())

	_set_active_tab(false)


func _set_active_tab(register_active: bool) -> void:
	_is_register_tab = register_active
	_login_tab_button.button_pressed = not register_active
	_register_tab_button.button_pressed = register_active
	_login_fields.visible = not register_active
	_register_fields.visible = register_active
	_submit_button.text = "Create Account" if register_active else "Log In"
	_style_tab_buttons()
	_clear_error()


func _on_submit_pressed() -> void:
	if _request_in_flight:
		return

	var validation_error := _validate()
	if validation_error != "":
		_show_error(validation_error)
		return

	_clear_error()
	_set_busy(true)

	var result: Dictionary
	if _is_register_tab:
		result = await ApiClient.post_json(self, REGISTER_PATH, {
			"username": _register_username_edit.text.strip_edges(),
			"email": _register_email_edit.text.strip_edges(),
			"password": _register_password_edit.text,
		})
	else:
		result = await ApiClient.post_json(self, LOGIN_PATH, {
			"username": _login_username_edit.text.strip_edges(),
			"password": _login_password_edit.text,
		})

	_set_busy(false)
	_handle_response(result)


## Minimal, non-authoritative pre-flight checks -- the server (AuthController/RegisterRequest/
## LoginRequest's [Required]/[MinLength]/[EmailAddress] attributes) is the real source of truth;
## this just avoids an obviously-doomed round trip for empty fields etc.
func _validate() -> String:
	if _is_register_tab:
		if _register_username_edit.text.strip_edges() == "":
			return "Enter a username."
		if _register_username_edit.text.strip_edges().length() < 3:
			return "Username must be at least 3 characters."
		if not _looks_like_email(_register_email_edit.text.strip_edges()):
			return "Enter a valid email address."
		if _register_password_edit.text.length() < 6:
			return "Password must be at least 6 characters."
	else:
		if _login_username_edit.text.strip_edges() == "":
			return "Enter your username."
		if _login_password_edit.text == "":
			return "Enter your password."
	return ""


func _looks_like_email(value: String) -> bool:
	# Deliberately loose -- EmailAddress-attribute-equivalent strictness isn't worth reproducing
	# client-side, the server already validates for real. Just catches the obviously-missing case.
	return value.length() >= 3 and value.contains("@") and value.contains(".")


func _handle_response(result: Dictionary) -> void:
	var code: int = result.code
	var body: Variant = result.body

	if code == 200:
		var account_id: String = str(body.get("accountId", ""))
		var username: String = str(body.get("username", ""))
		var token: String = str(body.get("token", ""))
		var expires_at: String = str(body.get("expiresAtUtc", ""))
		if token == "":
			_show_error("Server returned no token. Something is wrong on the server side.")
			return
		AuthState.set_session(account_id, username, token, expires_at)
		_show_success_and_advance(username)
		return

	if code == -1:
		_show_error("Could not reach the server. Check your connection and try again.")
		return

	if _is_register_tab and code == 409:
		_show_error(_extract_error_message(body, "That username is already taken."))
		return

	if not _is_register_tab and code == 401:
		_show_error(_extract_error_message(body, "Invalid username or password."))
		return

	_show_error(_extract_error_message(body, "Something went wrong (HTTP %d)." % code))


## Handles both this server's explicit {"error": "..."} shape (Conflict/Unauthorized responses)
## and ASP.NET Core's default ValidationProblemDetails shape (a {"title": ..., "errors": {...}}
## dictionary, returned automatically for [Required]/[MinLength]/[EmailAddress] failures we didn't
## already catch client-side) -- falls back to a caller-supplied message if neither shape matches.
func _extract_error_message(body: Variant, fallback: String) -> String:
	if body is Dictionary:
		if body.has("error"):
			return str(body["error"])
		if body.has("title"):
			return str(body["title"])
	return fallback


func _show_success_and_advance(username: String) -> void:
	_error_label.remove_theme_color_override("font_color")
	_error_label.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
	_error_label.text = "Welcome, %s. Connecting..." % username
	_error_label.visible = true
	get_tree().change_scene_to_file(HUB_SCENE)


func _set_busy(busy: bool) -> void:
	_request_in_flight = busy
	_submit_button.disabled = busy
	_login_tab_button.disabled = busy
	_register_tab_button.disabled = busy
	_login_username_edit.editable = not busy
	_login_password_edit.editable = not busy
	_register_username_edit.editable = not busy
	_register_email_edit.editable = not busy
	_register_password_edit.editable = not busy


func _show_error(message: String) -> void:
	_error_label.remove_theme_color_override("font_color")
	_error_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
	_error_label.text = message
	_error_label.visible = true


func _clear_error() -> void:
	_error_label.visible = false
	_error_label.text = ""


func _apply_theme() -> void:
	# Backdrop: approximates the locked
	# radial-gradient(ellipse at 30% 20%, #4a3a2e 0%, #2b2018 45%, #1a130e 100%) design-doc spec --
	# Godot's GradientTexture2D radial fill is the closest native equivalent to a CSS radial-gradient.
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

	# Card: dark semi-transparent panel, thin warm-cream border -- matches
	# rgba(30,22,16,0.75) bg / rgba(201,184,158,0.2) border from the locked visual theme.
	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(30.0 / 255.0, 22.0 / 255.0, 16.0 / 255.0, 0.85)
	card_style.border_color = Color(Color(COLOR_CARD_BORDER), 0.2)
	card_style.set_border_width_all(1)
	card_style.set_corner_radius_all(12)
	card_style.set_content_margin_all(0)
	_card.add_theme_stylebox_override("panel", card_style)

	_title_label.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
	_title_label.add_theme_font_size_override("font_size", 32)

	_subtitle_label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM_DIM))
	_subtitle_label.add_theme_font_size_override("font_size", 14)

	for line_edit: LineEdit in [
		_login_username_edit, _login_password_edit,
		_register_username_edit, _register_email_edit, _register_password_edit,
	]:
		_style_line_edit(line_edit)

	_style_submit_button()
	_style_tab_buttons()

	_error_label.add_theme_font_size_override("font_size", 14)


func _style_line_edit(line_edit: LineEdit) -> void:
	var normal := StyleBoxFlat.new()
	normal.bg_color = Color(0.11, 0.08, 0.06, 0.9)
	normal.border_color = Color(Color(COLOR_CARD_BORDER), 0.35)
	normal.set_border_width_all(1)
	normal.set_corner_radius_all(6)
	normal.set_content_margin_all(8)

	var focus := normal.duplicate()
	focus.border_color = Color(COLOR_ACCENT_AMBER)

	line_edit.add_theme_stylebox_override("normal", normal)
	line_edit.add_theme_stylebox_override("focus", focus)
	line_edit.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))
	line_edit.add_theme_color_override("font_placeholder_color", Color(Color(COLOR_TEXT_CREAM_DIM), 0.5))


func _style_submit_button() -> void:
	var normal := StyleBoxFlat.new()
	normal.bg_color = Color(Color(COLOR_ACCENT_AMBER), 0.85)
	normal.set_corner_radius_all(6)
	normal.set_content_margin_all(10)

	var hover := normal.duplicate()
	hover.bg_color = Color(COLOR_ACCENT_AMBER)

	var disabled := normal.duplicate()
	disabled.bg_color = Color(Color(COLOR_ACCENT_AMBER), 0.35)

	_submit_button.add_theme_stylebox_override("normal", normal)
	_submit_button.add_theme_stylebox_override("hover", hover)
	_submit_button.add_theme_stylebox_override("pressed", hover)
	_submit_button.add_theme_stylebox_override("disabled", disabled)
	_submit_button.add_theme_color_override("font_color", Color(COLOR_GRADIENT_BOTTOM))
	_submit_button.add_theme_color_override("font_hover_color", Color(COLOR_GRADIENT_BOTTOM))
	_submit_button.add_theme_color_override("font_disabled_color", Color(COLOR_GRADIENT_BOTTOM))


## Re-applied every tab switch so the active tab reads visually distinct (amber) from the
## inactive one (dim cream) -- toggle_mode's own default pressed-stylebox contrast isn't
## reliable enough to depend on across themes, so this is driven explicitly by font color.
func _style_tab_buttons() -> void:
	for button: Button in [_login_tab_button, _register_tab_button]:
		var is_active := button.button_pressed
		var style := StyleBoxFlat.new()
		style.bg_color = Color(Color(COLOR_ACCENT_AMBER), 0.18) if is_active else Color(0, 0, 0, 0)
		style.border_width_bottom = 2
		style.border_color = Color(COLOR_ACCENT_AMBER) if is_active else Color(Color(COLOR_CARD_BORDER), 0.25)
		style.set_corner_radius_all(4)
		style.set_content_margin_all(8)
		button.add_theme_stylebox_override("normal", style)
		button.add_theme_stylebox_override("hover", style)
		button.add_theme_stylebox_override("pressed", style)
		button.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER) if is_active else Color(COLOR_TEXT_CREAM_DIM))
		button.add_theme_color_override("font_pressed_color", Color(COLOR_ACCENT_AMBER) if is_active else Color(COLOR_TEXT_CREAM_DIM))
		button.add_theme_color_override("font_hover_color", Color(COLOR_ACCENT_AMBER))
