class_name MainGame
extends Node2D

const CardScene = preload("res://card.tscn")
const CardData = preload("res://scripts/models/CardData.gd")

const MY_PLAYER_ID = "p1"

# --- Nodes ---
@onready var opponent_hand_anchor = $OpponentHandAnchor
@onready var table_anchor = $TableAnchor
@onready var my_hand_anchor = $MyHandAnchor
@onready var deck_pos = $DeckPosition
@onready var player_captured_anchor = $PlayerCapturedAnchor
@onready var opponent_captured_anchor = $OpponentCapturedAnchor
@onready var deck_count_label = $UI/DeckCountLabel
@onready var start_button = $UI/StartButton
@onready var play_button = $UI/PlayButton
@onready var waiting_label = $UI/WaitingLabel

# --- State Management ---
var card_nodes: Dictionary = {} # { "7D": CardUI, ... }
var player_captured_nodes: Array[CardUI] = []
var opponent_captured_nodes: Array[CardUI] = []
var is_my_turn: bool = false

# Input state machine
var selected_hand_card: CardUI = null
var selected_table_cards: Array[CardUI] = []


func _ready() -> void:
	# --- Signal Connections ---
	if not is_instance_valid(NetworkManager):
		printerr("MainGame: NetworkManager autoload not found.")
		return
	
	NetworkManager.state_updated.connect(_on_server_state_updated)
	start_button.pressed.connect(_on_start_button_pressed)
	play_button.pressed.connect(_on_play_button_pressed)


# ------------------------------------------------------------------------------
# --- INPUT HANDLING ---
# ------------------------------------------------------------------------------

func _on_card_clicked(card_node: CardUI) -> void:
	if not is_my_turn:
		return

	if card_node.get_parent() == my_hand_anchor:
		_handle_hand_card_selection(card_node)
	elif card_node.get_parent() == table_anchor:
		_handle_table_card_selection(card_node)
	
	_update_play_button_visibility()


func _handle_hand_card_selection(card_node: CardUI) -> void:
	if selected_hand_card == card_node:
		card_node.toggle_selection()
		selected_hand_card = null
		for table_card in selected_table_cards:
			table_card.toggle_selection()
		selected_table_cards.clear()
	else:
		if is_instance_valid(selected_hand_card):
			selected_hand_card.toggle_selection()
		selected_hand_card = card_node
		selected_hand_card.toggle_selection()
		for table_card in selected_table_cards:
			table_card.toggle_selection()
		selected_table_cards.clear()


func _handle_table_card_selection(card_node: CardUI) -> void:
	if not is_instance_valid(selected_hand_card):
		return
		
	var hand_value = selected_hand_card.card_data.rank
	var selected_table_value = 0
	for card in selected_table_cards:
		selected_table_value += card.card_data.rank
	
	if selected_table_value + card_node.card_data.rank > hand_value:
		# If adding the new card exceeds the hand card value, reset selection
		for card in selected_table_cards:
			card.toggle_selection()
		selected_table_cards.clear()

	card_node.toggle_selection()
	if selected_table_cards.has(card_node):
		selected_table_cards.erase(card_node)
	else:
		selected_table_cards.append(card_node)


func _update_play_button_visibility() -> void:
	play_button.visible = is_my_turn and is_instance_valid(selected_hand_card)


func _on_play_button_pressed() -> void:
	if not is_my_turn or not is_instance_valid(selected_hand_card):
		return

	var hand_card_str = selected_hand_card.card_data._to_string()
	var action_str = hand_card_str

	if not selected_table_cards.is_empty():
		var table_cards_str_arr = []
		for card in selected_table_cards:
			table_cards_str_arr.append(card.card_data._to_string())
		
		var targets_str = table_cards_str_arr.join("+")
		action_str = "%sx%s" % [hand_card_str, targets_str]

	print("Sending action: ", action_str)
	NetworkManager.send_action(action_str)
	_reset_selection()
	play_button.visible = false
	is_my_turn = false
	waiting_label.visible = true


func _reset_selection():
	if is_instance_valid(selected_hand_card):
		selected_hand_card.set_selected_state(false)
	for card in selected_table_cards:
		card.set_selected_state(false)
	selected_hand_card = null
	selected_table_cards.clear()


# ------------------------------------------------------------------------------
# --- STATE SYNC & ANIMATION ---
# ------------------------------------------------------------------------------

func _on_server_state_updated(state: Dictionary) -> void:
	if not state.has("state"): return
	var game_state: Dictionary = state.get("state")
	
	var current_player = game_state.get("currentTurnPlayer", "")
	is_my_turn = (current_player == MY_PLAYER_ID)
	waiting_label.visible = not is_my_turn
	if not is_my_turn:
		_reset_selection()
		play_button.visible = false

	var all_cards_in_new_state = {}
	
	var my_hand_codes = game_state.get("players", {}).get(MY_PLAYER_ID, {}).get("hand", [])
	for code in my_hand_codes:
		all_cards_in_new_state[code] = { "anchor": my_hand_anchor, "face_down": false }
		
	var opponent_id = "p2" if MY_PLAYER_ID == "p1" else "p1"
	var opponent_hand_codes = game_state.get("players", {}).get(opponent_id, {}).get("hand", [])
	for code in opponent_hand_codes:
		all_cards_in_new_state[code] = { "anchor": opponent_hand_anchor, "face_down": (code == "X") }

	var table_codes = game_state.get("table", [])
	for code in table_codes:
		all_cards_in_new_state[code] = { "anchor": table_anchor, "face_down": false }

	var new_state_codes = all_cards_in_new_state.keys()
	var old_on_screen_codes = card_nodes.keys()
	
	var cards_to_add = new_state_codes.filter(func(c): return not old_on_screen_codes.has(c))
	var cards_to_remove = old_on_screen_codes.filter(func(c): return not new_state_codes.has(c))

	# Heuristic to detect opponent's discard
	var added_table_cards = cards_to_add.filter(func(c): return all_cards_in_new_state[c].anchor == table_anchor)
	var removed_opponent_placeholders = cards_to_remove.filter(func(c): return c.begins_with("OPP"))

	if added_table_cards.size() == 1 and removed_opponent_placeholders.size() == 1:
		var real_card_code = added_table_cards[0]
		var placeholder_code = removed_opponent_placeholders[0]
		
		var card_node = card_nodes[placeholder_code]
		
		card_nodes.erase(placeholder_code)
		cards_to_remove.erase(placeholder_code)
		cards_to_add.erase(real_card_code)
		
		card_node.setup(CardData.new(real_card_code))
		card_nodes[real_card_code] = card_node
		
		card_node.reparent(table_anchor)

	var last_capture_player = game_state.get("lastCapturePlayer", "")

	for code in cards_to_remove:
		if not card_nodes.has(code): continue
		var card_node = card_nodes[code]
		
		var target_anchor = player_captured_anchor if last_capture_player == MY_PLAYER_ID else opponent_captured_anchor
		card_node.reparent(target_anchor)
		card_node.animate_move(Vector2.ZERO)
		
		if last_capture_player == MY_PLAYER_ID:
			player_captured_nodes.append(card_node)
		else:
			opponent_captured_nodes.append(card_node)
			
		card_nodes.erase(code)

	for code in cards_to_add:
		var card_info = all_cards_in_new_state[code]
		var new_card = _create_card(code, card_info.anchor, deck_pos.global_position, card_info.face_down)
		card_nodes[code] = new_card

	_reposition_cards_in_anchors(all_cards_in_new_state)
	_update_captured_deck_layout(player_captured_nodes, player_captured_anchor)
	_update_captured_deck_layout(opponent_captured_nodes, opponent_captured_anchor)
	
	var deck_array = game_state.get("deck", [])
	deck_count_label.text = "Deck: %d" % deck_array.size()


func _reposition_cards_in_anchors(all_cards_map: Dictionary):
	var hand_cards = all_cards_map.keys().filter(func(c): return all_cards_map[c].anchor == my_hand_anchor)
	_animate_card_layout(hand_cards, my_hand_anchor)
	
	var table_cards = all_cards_map.keys().filter(func(c): return all_cards_map[c].anchor == table_anchor)
	_animate_card_layout(table_cards, table_anchor)

	var opponent_cards = all_cards_map.keys().filter(func(c): return all_cards_map[c].anchor == opponent_hand_anchor)
	_animate_card_layout(opponent_cards, opponent_hand_anchor)


func _animate_card_layout(card_codes: Array, anchor: Node2D):
	var card_width = 95
	var total_width = card_codes.size() * card_width
	var start_x = -total_width / 2.0 + card_width / 2.0
	
	for i in range(card_codes.size()):
		var code = card_codes[i]
		if not card_nodes.has(code): continue
		var card_node = card_nodes[code]
		var target_pos = anchor.global_position + Vector2(start_x + i * card_width, 0)
		card_node.animate_move(target_pos, i * 0.05)


func _update_captured_deck_layout(captured_nodes: Array[CardUI], anchor: Node2D):
	for i in range(captured_nodes.size()):
		var card_node = captured_nodes[i]
		card_node.animate_move(anchor.global_position + Vector2(0, i * -2), 0.1)


# ------------------------------------------------------------------------------
# --- HELPERS ---
# ------------------------------------------------------------------------------

func _on_start_button_pressed() -> void:
	NetworkManager.start_game()
	start_button.hide()


func _create_card(card_code: String, parent: Node, start_pos: Vector2, is_face_down: bool) -> CardUI:
	var card_data = CardData.new(card_code)
	var card_node: CardUI = CardScene.instantiate()
	parent.add_child(card_node)
	card_node.global_position = start_pos
	card_node.setup(card_data)
	card_node.card_ui_clicked.connect(_on_card_clicked)
	return card_node
