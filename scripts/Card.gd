extends TextureButton
class_name CardUI

# Preload the data model for a card.
const CardData = preload("res://scripts/models/CardData.gd")

# Textures and paths from the user's project.
const CARD_DECK_PATH = "res://assets/textures/deck/scopadeck.png"
const CARD_BACK_PATH = "res://assets/textures/deck/scopaback.png"

# Spritesheet configuration from user's example.
const CARD_ATLAS_WIDTH = 142
const CARD_ATLAS_HEIGHT = 190
const SUIT_MAP = { "C": 0, "B": 1, "D": 2, "S": 3 } # Coppe, Bastoni, Denari, Spade

@onready var card_sprite: TextureRect = $CardSprite
@onready var selection_highlight: Panel = $SelectionHighlight

signal card_clicked(card_data: CardData)

var card_data: CardData
var is_face_down: bool = false

func _ready() -> void:
	# Connect the button's own signal to our handler.
	pressed.connect(_on_pressed)
	
	# Set a minimum size to ensure the button is visible even without a texture.
	custom_minimum_size = Vector2(CARD_ATLAS_WIDTH / 1.5, CARD_ATLAS_HEIGHT / 1.5)
	
	# The TextureRect should fill the button.
	if is_instance_valid(card_sprite):
		card_sprite.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

# --- Public API ---

## Main function to configure the card's appearance.
func setup(p_card_data: CardData, p_is_face_down: bool = false) -> void:
	self.card_data = p_card_data
	self.is_face_down = p_is_face_down
	
	# The node might not be ready yet when setup is called.
	if not is_inside_tree():
		await ready
		
	update_display()

func set_selected(selected: bool) -> void:
	if is_instance_valid(selection_highlight):
		selection_highlight.visible = selected

# --- Internal Functions ---

func update_display() -> void:
	if not is_instance_valid(card_data):
		visible = false
		return
	
	visible = true
	if is_face_down:
		show_card_back()
	else:
		show_card_front()

func show_card_back() -> void:
	if is_instance_valid(card_sprite):
		card_sprite.texture = load(CARD_BACK_PATH)

func show_card_front() -> void:
	if not is_instance_valid(card_sprite):
		return
	
	var base_texture = load(CARD_DECK_PATH)
	var rank_index = card_data.rank - 1
	
	if not SUIT_MAP.has(card_data.suit) or rank_index < 0:
		printerr("CardUI: Invalid card data for atlas: ", card_data._to_string())
		return

	var suit_index = SUIT_MAP[card_data.suit]

	# Create a new AtlasTexture to represent the card's region in the spritesheet.
	var atlas = AtlasTexture.new()
	atlas.atlas = base_texture
	atlas.region = Rect2(
		rank_index * CARD_ATLAS_WIDTH,
		suit_index * CARD_ATLAS_HEIGHT,
		CARD_ATLAS_WIDTH,
		CARD_ATLAS_HEIGHT
	)
	card_sprite.texture = atlas

func _on_pressed() -> void:
	# When this button is pressed, emit a signal with its data.
	emit_signal("card_clicked", card_data)
