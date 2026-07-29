extends VBoxContainer
class_name AnimaRevealPanel

## The reusable Anima Reveal component (NEW -- extracted from starter_reveal.gd, which used to
## fuse this display/naming UI together with the starter-trio-specific hub-connection/RPC/
## progress-sequencing logic in the same script). Genuinely reusable now: this component has NO
## knowledge of Starter/Weave/Boss-hatch, no hub connection, no RPC method names, and no opinion
## about what happens after Confirm is pressed -- it only displays a genome and collects a name.
##
## Portrait -> Name/Color/Gen -> Threads (Dominant always shown, R1/R2 behind "Show hidden",
## default off) -> mandatory naming -> Confirm. The calling scene owns fetching whatever needs to
## be revealed and calling the right Confirm* hub method:
## - starter_reveal.gd: GetPendingStarterReveal/ConfirmStarterAnima, walks a sequence of slots,
##   passes progress_text ("Starter Anima N of 3") and default_name (the archetype name).
## - A future weave_reveal.gd: AttemptWeave/ConfirmWeave, would call render() once for the Primary
##   (and again for the Twin if Echo triggered) with progress_text="" (no multi-slot sequence to
##   show) and default_name="" (no default -- an offspring has no archetype to suggest a name from).
## - A future boss_hatch_reveal.gd: ConfirmBossHatch, same shape as Weave's Primary-only case.
##
## Gen is NOT part of the wire shape (WeaveGenomePreview has no Gen field, for any of the 3
## sources -- confirmed by reading GameHubModels.cs, not assumed) -- it's passed in explicitly by
## the caller instead, since every caller already knows it locally (Starter/Boss-hatch: always 1;
## Weaving: max(parentA.Gen, parentB.Gen)+1, computed from the two parent cards already on screen).

signal confirmed(name: String)

const COLOR_CARD_BORDER := "c9b89e"
const COLOR_TEXT_CREAM := "f0e4d4"
const COLOR_ACCENT_AMBER := "e8a03a"
const COLOR_GRADIENT_BOTTOM := "1a130e"
const COLOR_ERROR := "e2554a"

# Portrait tint per body Color -- placeholder only (real pixel-art portraits are deferred, per
# CLAUDE.md's own note), just enough to visually distinguish entries from each other.
const COLOR_TINTS := {
	"Crimson": "8a3a3a",
	"Onyx": "4a4a52",
	"Verdant": "3a6a4a",
	"Azure": "3a5a7a",
}

@onready var _progress_label: Label = $ProgressLabel
@onready var _portrait: ColorRect = $Portrait
@onready var _portrait_label: Label = $Portrait/PortraitLabel
@onready var _name_color_gen_label: Label = $NameColorGenLabel
@onready var _show_hidden_toggle: CheckButton = $ThreadsHeaderRow/ShowHiddenToggle
@onready var _head_row: Label = $ThreadsRows/HeadRow
@onready var _frame_row: Label = $ThreadsRows/FrameRow
@onready var _tail_row: Label = $ThreadsRows/TailRow
@onready var _crest_row: Label = $ThreadsRows/CrestRow
@onready var _name_line_edit: LineEdit = $NameLineEdit
@onready var _error_label: Label = $ErrorLabel
@onready var _confirm_button: Button = $ConfirmButton

var _current_genome: Dictionary = {}


func _ready() -> void:
	_apply_theme()
	_show_hidden_toggle.toggled.connect(func(_pressed: bool): _render_threads())
	_confirm_button.pressed.connect(_on_confirm_pressed)
	_name_line_edit.text_submitted.connect(func(_t): _on_confirm_pressed())


## genome: a WeaveGenomePreview-shaped Dictionary (color/hybridTriggered/parts). gen: passed in by
## the caller (see this file's own comment for why it can't be read from genome). default_name:
## pre-fills the naming field, editable ("" leaves it blank -- Weave/Boss-hatch offspring have no
## archetype to default to). progress_text: shown above the portrait if non-empty; the whole
## ProgressLabel is hidden when empty, since only the starter-trio flow has a multi-slot sequence
## to show progress through.
func render(genome: Dictionary, gen: int, default_name: String = "", progress_text: String = "") -> void:
	_current_genome = genome
	_progress_label.visible = progress_text != ""
	_progress_label.text = progress_text

	var color_name: String = str(genome.get("color", ""))
	_portrait_label.text = default_name if default_name != "" else color_name
	_portrait.color = Color(COLOR_TINTS.get(color_name, "555555"))
	_name_color_gen_label.text = "Color: %s  |  Gen: %d" % [color_name, gen]

	_name_line_edit.text = default_name
	clear_error()

	_render_threads()


## Re-renders just the Threads rows -- called on render() AND whenever "Show hidden" is toggled,
## so flipping the toggle doesn't need a fresh server round-trip (the full genome, R1/R2 included,
## was already handed to render()).
func _render_threads() -> void:
	var parts: Array = _current_genome.get("parts", [])
	var show_hidden := _show_hidden_toggle.button_pressed

	var rows := { "Head": _head_row, "Frame": _frame_row, "Tail": _tail_row, "Crest": _crest_row }
	for part_name: String in rows.keys():
		var row: Label = rows[part_name]
		var part_data: Dictionary = _find_part(parts, part_name)
		if part_data.is_empty():
			row.text = "%s: --" % part_name
			continue

		var dominant: Dictionary = part_data.get("dominant", {})
		var text := "%s: %s" % [part_name, str(dominant.get("name", "--"))]
		if show_hidden:
			var r1: Dictionary = part_data.get("r1", {})
			var r2: Dictionary = part_data.get("r2", {})
			text += "  (hidden: %s / %s)" % [str(r1.get("name", "--")), str(r2.get("name", "--"))]
		row.text = text


func _find_part(parts: Array, part_name: String) -> Dictionary:
	for p: Variant in parts:
		if p is Dictionary and str(p.get("part", "")) == part_name:
			return p
	return {}


func _on_confirm_pressed() -> void:
	var name := _name_line_edit.text.strip_edges()
	if name == "":
		show_error("Enter a name.")
		return

	clear_error()
	confirmed.emit(name)


## Caller-driven -- e.g. disabled while a ConfirmStarterAnima/ConfirmWeave/ConfirmBossHatch call is
## in flight, so the player can't double-submit.
func set_busy(busy: bool) -> void:
	_confirm_button.disabled = busy
	_name_line_edit.editable = not busy


func show_error(message: String) -> void:
	_error_label.remove_theme_color_override("font_color")
	_error_label.add_theme_color_override("font_color", Color(COLOR_ERROR))
	_error_label.text = message
	_error_label.visible = true


func clear_error() -> void:
	_error_label.visible = false
	_error_label.text = ""


func _apply_theme() -> void:
	_progress_label.add_theme_color_override("font_color", Color(COLOR_ACCENT_AMBER))
	_progress_label.add_theme_font_size_override("font_size", 20)

	for label: Label in [_name_color_gen_label, _head_row, _frame_row, _tail_row, _crest_row]:
		label.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))

	_error_label.add_theme_font_size_override("font_size", 14)

	var name_edit_normal := StyleBoxFlat.new()
	name_edit_normal.bg_color = Color(0.11, 0.08, 0.06, 0.9)
	name_edit_normal.border_color = Color(Color(COLOR_CARD_BORDER), 0.35)
	name_edit_normal.set_border_width_all(1)
	name_edit_normal.set_corner_radius_all(6)
	name_edit_normal.set_content_margin_all(8)
	var name_edit_focus := name_edit_normal.duplicate()
	name_edit_focus.border_color = Color(COLOR_ACCENT_AMBER)
	_name_line_edit.add_theme_stylebox_override("normal", name_edit_normal)
	_name_line_edit.add_theme_stylebox_override("focus", name_edit_focus)
	_name_line_edit.add_theme_color_override("font_color", Color(COLOR_TEXT_CREAM))

	var button_normal := StyleBoxFlat.new()
	button_normal.bg_color = Color(Color(COLOR_ACCENT_AMBER), 0.85)
	button_normal.set_corner_radius_all(6)
	button_normal.set_content_margin_all(10)
	var button_hover := button_normal.duplicate()
	button_hover.bg_color = Color(COLOR_ACCENT_AMBER)
	var button_disabled := button_normal.duplicate()
	button_disabled.bg_color = Color(Color(COLOR_ACCENT_AMBER), 0.35)
	_confirm_button.add_theme_stylebox_override("normal", button_normal)
	_confirm_button.add_theme_stylebox_override("hover", button_hover)
	_confirm_button.add_theme_stylebox_override("pressed", button_hover)
	_confirm_button.add_theme_stylebox_override("disabled", button_disabled)
	_confirm_button.add_theme_color_override("font_color", Color(COLOR_GRADIENT_BOTTOM))
