extends TextureButton
class_name CardUI

const CardData = preload("res://scripts/models/CardData.gd")

const CARD_DECK_PATH = "res://assets/textures/deck/scopadeck.png"
const CARD_BACK_PATH = "res://assets/textures/deck/scopaback.png"

const CARD_ATLAS_WIDTH = 142
const CARD_ATLAS_HEIGHT = 190
const SUIT_MAP = { "C": 0, "B": 1, "D": 2, "S": 3 }

@onready var card_sprite: TextureRect = $CardSprite
@onready var selection_highlight: Panel = $SelectionHighlight

signal card_ui_clicked(card_ui: CardUI)

var card_data: CardData
var selected: bool = false

func _ready() -> void:
	pressed.connect(_on_pressed)
	custom_minimum_size = Vector2(CARD_ATLAS_WIDTH / 1.5, CARD_ATLAS_HEIGHT / 1.5)
	if is_instance_valid(card_sprite):
		card_sprite.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

# --- Public API ---

func setup(p_card_data: CardData) -> void:
	self.card_data = p_card_data
	disabled = false
	if not is_inside_tree():
		await ready
	update_display()
	name = card_data._to_string() if is_instance_valid(card_data) else "Card"

func set_disabled_state(is_disabled: bool) -> void:
	disabled = is_disabled
	if is_disabled:
		modulate = Color(0.5, 0.5, 0.5)
		if selected: toggle_selection() # Deselect if disabling
	else:
		modulate = Color(0.9, 0.9, 0.9) if not selected else Color.WHITE

func toggle_selection() -> void:
	selected = not selected
	var tween = create_tween()
	if selected:
		# Use a springy-looking transition
		tween.tween_property(self, "position:y", position.y - 20, 0.2).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
		modulate = Color.WHITE
	else:
		tween.tween_property(self, "position:y", position.y + 20, 0.2).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
		modulate = Color(0.9, 0.9, 0.9)

func set_selected_state(is_selected: bool):
	if selected != is_selected:
		toggle_selection()

func animate_move(target_pos: Vector2, delay: float = 0.0) -> void:
	var tween = create_tween()
	tween.set_parallel(true)
	# Set a delay if needed for staggered animations
	tween.tween_interval(delay)
	# Animate the movement
	tween.tween_property(self, "global_position", target_pos, 0.35).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

# --- Internal ---

func update_display() -> void:
	if not is_instance_valid(card_data):
		visible = false
		return
	
	visible = true
	modulate = Color(0.9, 0.9, 0.9) # Default tint
	if card_data._to_string() == "X":
		show_card_back()
	else:
		show_card_front()

func show_card_back() -> void:
	if is_instance_valid(card_sprite):
		card_sprite.texture = load(CARD_BACK_PATH)

func show_card_front() -> void:
	if not is_instance_valid(card_sprite): return
	
	var base_texture = load(CARD_DECK_PATH)
	var rank_index = card_data.rank - 1
	if not SUIT_MAP.has(card_data.suit) or rank_index < 0:
		# This can happen for the "BACK" card, it's fine.
		show_card_back()
		return

	var suit_index = SUIT_MAP[card_data.suit]
	var atlas = AtlasTexture.new()
	atlas.atlas = base_texture
	atlas.region = Rect2(rank_index * CARD_ATLAS_WIDTH, suit_index * CARD_ATLAS_HEIGHT, CARD_ATLAS_WIDTH, CARD_ATLAS_HEIGHT)
	card_sprite.texture = atlas

func _on_pressed() -> void:
	emit_signal("card_ui_clicked", self)
