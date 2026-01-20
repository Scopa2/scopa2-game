extends TextureButton
class_name CardUI

const Card = preload("res://scripts/models/Card.gd")

@onready var card_sprite: TextureRect = $CardSprite
@onready var selection_highlight: ColorRect = $SelectionHighlight
@onready var card_label: Label = $CardLabel

# Debug overlay label (top-left)
var debug_label: Label = null

var card_data: Card
var is_selected: bool = false
var is_selectable: bool = true
var is_face_down: bool = false
const CARD_BACK_PATH = "res://assets/textures/deck/scopaback.png"

# Enable debug overlay and logging
const DEBUG = true

# Sprite sheet configuration - from Lua: px = 71, py = 95
const CARD_WIDTH = 142
const CARD_HEIGHT = 190
const COLUMNS = 10  # Cards per row in sprite sheet
const ROWS = 4      # Number of suits

signal card_clicked(card_ui: CardUI)
signal card_hover_started(card_ui: CardUI)
signal card_hover_ended(card_ui: CardUI)

func _ready():
	pressed.connect(_on_pressed)
	mouse_entered.connect(_on_mouse_entered)
	mouse_exited.connect(_on_mouse_exited)
	custom_minimum_size = Vector2(106, 142)  # 1.5x scale for visibility

	if DEBUG:
		debug_label = Label.new()
		debug_label.text = ""
		debug_label.visible = false
		debug_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
		debug_label.vertical_alignment = VERTICAL_ALIGNMENT_TOP
		debug_label.add_theme_font_size_override("font_size", 12)
		debug_label.modulate = Color(1,0,0,0.7)
		debug_label.position = Vector2(4, 4)
		add_child(debug_label)

func setup(card: Card, face_down: bool = false) -> void:
	card_data = card
	is_face_down = face_down
	if not is_inside_tree():
		await ready
	update_display()

func update_display() -> void:
	if card_data == null:
		return
	if is_face_down:
		show_card_back()
	else:
		show_card_front()

	if DEBUG:
		show_debug_overlay()

func show_card_back() -> void:
	if card_sprite:
		var back_texture = load(CARD_BACK_PATH)
		card_sprite.texture = back_texture
		card_sprite.visible = true
	if card_label:
		card_label.text = ""
		card_label.visible = DEBUG  # Show label only if DEBUG

func show_card_front() -> void:
	if card_label:
		card_label.text = card_data._to_string()
		card_label.add_theme_font_size_override("font_size", 16)
		card_label.visible = DEBUG  # Show label only if DEBUG
	setup_sprite_region()

func setup_sprite_region() -> void:
	if card_sprite == null or card_data == null:
		return
	var suit_index = ["C", "B", "D", "S"].find(card_data.suit)
	var rank_index = card_data.rank - 1
	if suit_index < 0:
		return
	var base_texture = card_sprite.texture
	if base_texture == null:
		return
	if base_texture is AtlasTexture:
		base_texture = base_texture.atlas
	if base_texture == null:
		return
	var tex_width = base_texture.get_width()
	var tex_height = base_texture.get_height()
	var card_w = tex_width / COLUMNS
	var card_h = tex_height / ROWS
	var atlas = AtlasTexture.new()
	atlas.atlas = base_texture
	atlas.region = Rect2(
		rank_index * card_w,
		suit_index * card_h,
		card_w,
		card_h
	)
	card_sprite.texture = atlas
	card_sprite.visible = true
	if card_label:
		card_label.visible = DEBUG

func show_debug_overlay() -> void:
	if debug_label:
		debug_label.text = card_data._to_string() if card_data else ""
		debug_label.visible = true

func debug_log(msg: String) -> void:
	if DEBUG:
		print("[CARDUI DEBUG] " + msg)

func set_selected(selected: bool) -> void:
	is_selected = selected
	if selection_highlight:
		selection_highlight.visible = selected
		selection_highlight.color = Color(1, 1, 0, 0.4)

func set_selectable(selectable: bool) -> void:
	is_selectable = selectable
	disabled = not selectable
	modulate.a = 1.0 if selectable else 0.6

func set_highlighted(highlighted: bool) -> void:
	if selection_highlight:
		selection_highlight.visible = highlighted
		if highlighted:
			selection_highlight.color = Color(0, 1, 0, 0.4)
		else:
			selection_highlight.color = Color(1, 1, 0, 0.4)

func _on_pressed() -> void:
	if is_selectable:
		emit_signal("card_clicked", self)

func _on_mouse_entered() -> void:
	if is_selectable and not is_face_down:
		emit_signal("card_hover_started", self)
		position.y -= 10
		z_index = 1

func _on_mouse_exited() -> void:
	emit_signal("card_hover_ended", self)
	position.y += 10 if position.y < 0 else 0
	z_index = 0

func get_card() -> Card:
	return card_data
