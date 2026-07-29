extends Control

## Bare placeholder for a destination screen that doesn't exist yet (Sanctum/Weaving/Collection) --
## same "label + Back button" pattern hub.tscn (formerly post_login.tscn) already used before it
## grew real content. One shared script; `title_text` is set per-instance via the Inspector/scene
## file (each of sanctum.tscn/weaving.tscn/collection.tscn overrides it to a different value), so
## there's exactly one script for all 3 destinations rather than 3 near-duplicates.

@export var title_text: String = "Coming Soon"

const HUB_SCENE := "res://scenes/hub.tscn"
const COLOR_GRADIENT_TOP := "4a3a2e"
const COLOR_GRADIENT_MID := "2b2018"
const COLOR_GRADIENT_BOTTOM := "1a130e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_ACCENT_AMBER := "e8a03a"

@onready var _background: TextureRect = $Background
@onready var _title_label: Label = $CenterContainer/Card/Margin/Content/TitleLabel
@onready var _back_button: Button = $CenterContainer/Card/Margin/Content/BackButton
@onready var _card: PanelContainer = $CenterContainer/Card


func _ready() -> void:
	_title_label.text = title_text
	_apply_theme()
	_back_button.pressed.connect(func(): get_tree().change_scene_to_file(HUB_SCENE))


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

	_title_label.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
	_title_label.add_theme_font_size_override("font_size", 24)
