class_name MainGame
extends Node

## The main controller for the game scene.
## It listens to NetworkManager and updates the UI based on the game state.

# Preload the Card scene and data model.
const CardScene = preload("res://card.tscn")
const CardData = preload("res://scripts/models/CardData.gd")

# UI containers and labels that will be populated in _ready.
var my_hand_container: HBoxContainer
var opponent_hand_container: HBoxContainer
var table_container: HBoxContainer
var deck_count_label: Label
var start_button: Button

# We assume p1 is always our player, as seen from the client.
const MY_PLAYER_ID = "p1"

func _ready() -> void:
	# --- Node Acquisition ---
	my_hand_container = get_node_or_null("UI/VBox/MyHandContainer")
	opponent_hand_container = get_node_or_null("UI/VBox/OpponentHandContainer")
	table_container = get_node_or_null("UI/VBox/CenterContainer/TableContainer")
	deck_count_label = get_node_or_null("UI/VBox/CenterContainer/HBox/DeckCountLabel")
	start_button = get_node_or_null("UI/StartButton")
	
	# --- Signal Connections & Safety Checks ---
	if not is_instance_valid(NetworkManager):
		printerr("MainGame: NetworkManager autoload not found.")
		return
	
	NetworkManager.state_updated.connect(_on_state_updated)
	
	if is_instance_valid(start_button):
		start_button.pressed.connect(_on_start_button_pressed)
	else:
		printerr("MainGame: StartButton node not found.")
		
	if not is_instance_valid(my_hand_container) or not is_instance_valid(opponent_hand_container) or not is_instance_valid(table_container):
		printerr("MainGame: One or more card container nodes are missing.")

## Called when the "Start Game" button is pressed.
func _on_start_button_pressed() -> void:
	NetworkManager.start_game()
	start_button.hide()

## The core function to render the game state.
func _on_state_updated(state: Dictionary) -> void:
	print("\n--- MainGame: Received state update ---")
	print("Raw State Data: ", state)

	if not state.has("state"):
		print("ERROR: Received data does not contain a 'state' object. Cannot render.")
		return
		
	var game_state: Dictionary = state.get("state")
	print("Extracted Game State: ", game_state)

	# 1. Clear all current card containers.
	print("Clearing card containers...")
	_clear_container(my_hand_container)
	_clear_container(opponent_hand_container)
	_clear_container(table_container)
	
	# 2. Update deck count label from the 'deck' array size.
	if game_state.has("deck") and is_instance_valid(deck_count_label):
		var deck_array = game_state.get("deck", [])
		deck_count_label.text = "Deck: %d" % deck_array.size()
		print("Deck count updated to: ", deck_count_label.text)
	else:
		print("No 'deck' array found in game_state.")

	# 3. Populate the table.
	if game_state.has("table"):
		var table_cards = game_state.get("table", [])
		print("Found %d cards for the table." % table_cards.size())
		for card_code in table_cards:
			_create_card(card_code, table_container, false)
			print("  - Created table card: ", card_code)
	else:
		print("No 'table' key found in game_state.")

	# 4. Populate player hands.
	if game_state.has("players"):
		var players: Dictionary = game_state.get("players", {})
		print("Found 'players' key. Processing %d players." % players.size())
		for player_id in players:
			var player_data: Dictionary = players.get(player_id, {})
			if not player_data.has("hand"): continue
			
			var hand_cards = player_data.get("hand", [])
			print("Player '%s' has %d cards." % [player_id, hand_cards.size()])

			if player_id == MY_PLAYER_ID:
				# Our hand - cards are clickable and face up.
				for card_code in hand_cards:
					var card_node = _create_card(card_code, my_hand_container, false)
					card_node.card_clicked.connect(_on_my_card_clicked)
					print("  - Created player hand card: ", card_code)
			else:
				# Opponent's hand - cards are face down.
				for card_code in hand_cards:
					# The opponent's hand in the JSON is ["X", "X", "X"]. 
					# The CardData class will fail on "X".
					# We should pass "BACK" to _create_card instead.
					_create_card("BACK", opponent_hand_container, true)
					print("  - Created opponent hand card (face down)")
	else:
		print("No 'players' key found in game_state.")
	
	print("--- MainGame: State update processing finished ---\n")

## Handles when a card in our hand is clicked.
func _on_my_card_clicked(card_data: CardData) -> void:
	# The signal now sends the CardData object. We convert it back to a string for the server.
	var action_string = "play:%s" % card_data._to_string()
	NetworkManager.send_action(action_string)

## Helper function to remove all children from a container.
func _clear_container(container: Node) -> void:
	if not is_instance_valid(container): return
	for child in container.get_children():
		child.queue_free()

## Helper function to instantiate a card and add it to a container.
func _create_card(card_code: String, parent: Node, is_face_down: bool) -> CardUI:
	# Instantiate the data object for the card.
	var card_data = CardData.new(card_code)
	
	# Instantiate the scene and set it up.
	var card_node: CardUI = CardScene.instantiate()
	card_node.setup(card_data, is_face_down)
	
	parent.add_child(card_node)
	return card_node
