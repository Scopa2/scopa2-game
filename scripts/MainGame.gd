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


func _handle_hand_card_selection(card_node: CardUI) -> void:
	# 1. Select the card
	if selected_hand_card == card_node:
		selected_hand_card.set_selected_state(false)
		selected_hand_card = null
		_reset_table_selection()
		return

	if is_instance_valid(selected_hand_card):
		selected_hand_card.set_selected_state(false)
		_reset_table_selection() 
	
	selected_hand_card = card_node
	selected_hand_card.set_selected_state(true)
	
	# 2. Analyze Captures
	var table_cards = _get_table_cards()
	var rank = card_node.card_data.rank
	
	# Check strict single match first (Scopa Rule: Must take single card if matches)
	var direct_matches = table_cards.filter(func(c): return c.card_data.rank == rank)
	
	if not direct_matches.is_empty():
		_set_table_cards_interactive(direct_matches)
		return
		
	# Check sum matches
	var valid_subset_cards = _get_cards_valid_for_sum(rank, table_cards)
	
	if valid_subset_cards.is_empty():
		# NO CAPTURE POSSIBLE -> THROW IMMEDIATELY
		_perform_action_throw(card_node)
	else:
		# CAPTURE POSSIBLE -> Highlight valid parts
		_set_table_cards_interactive(valid_subset_cards)


func _handle_table_card_selection(card_node: CardUI) -> void:
	if not is_instance_valid(selected_hand_card): return
	if card_node.disabled: return 

	# Toggle selection
	if selected_table_cards.has(card_node):
		card_node.set_selected_state(false)
		selected_table_cards.erase(card_node)
	else:
		card_node.set_selected_state(true)
		selected_table_cards.append(card_node)
		
	# Check if sum reached
	var current_sum = 0
	for c in selected_table_cards: current_sum += c.card_data.rank
	
	if current_sum == selected_hand_card.card_data.rank:
		_perform_action_capture(selected_hand_card, selected_table_cards)
	elif current_sum < selected_hand_card.card_data.rank:
		# Update interactivity for remaining needed sum
		var needed = selected_hand_card.card_data.rank - current_sum
		var remaining_table = _get_table_cards().filter(func(c): return not selected_table_cards.has(c))
		var valid_next = _get_cards_valid_for_sum(needed, remaining_table)
		
		# Keep currently selected cards enabled + valid next steps
		var all_interactive = selected_table_cards + valid_next
		_set_table_cards_interactive(all_interactive)


func _perform_action_throw(card_node: CardUI) -> void:
	var action_str = card_node.card_data._to_string()
	print("Auto-throwing: ", action_str)
	_send_network_action(action_str)


func _perform_action_capture(hand_card: CardUI, targets: Array[CardUI]) -> void:
	var hand_str = hand_card.card_data._to_string()
	var table_strs = []
	for c in targets: table_strs.append(c.card_data._to_string())
	var targets_str = "+".join(PackedStringArray(table_strs))
	var action_str = "%sx%s" % [hand_str, targets_str]
	
	print("Auto-capturing: ", action_str)
	_send_network_action(action_str)


func _send_network_action(action_str: String) -> void:
	NetworkManager.send_action(action_str)
	_reset_selection()
	is_my_turn = false
	waiting_label.visible = true


func _reset_selection():
	if is_instance_valid(selected_hand_card):
		selected_hand_card.set_selected_state(false)
	_reset_table_selection()
	selected_hand_card = null


func _reset_table_selection():
	for card in selected_table_cards:
		card.set_selected_state(false)
	selected_table_cards.clear()
	# Reset disabled state for all table cards
	for card in _get_table_cards():
		card.set_disabled_state(false)


func _get_table_cards() -> Array[CardUI]:
	var cards: Array[CardUI] = []
	for child in table_anchor.get_children():
		if child is CardUI and not child.is_queued_for_deletion():
			cards.append(child)
	return cards


func _set_table_cards_interactive(enabled_cards: Array[CardUI]):
	var all = _get_table_cards()
	for c in all:
		c.set_disabled_state(not enabled_cards.has(c))


# --- Capture Logic Helpers ---

func _get_cards_valid_for_sum(target: int, pool: Array[CardUI]) -> Array[CardUI]:
	var valid: Array[CardUI] = []
	for i in range(pool.size()):
		var c = pool[i]
		if c.card_data.rank > target: continue
		if c.card_data.rank == target:
			valid.append(c)
			continue
			
		var remaining_pool = pool.duplicate()
		remaining_pool.remove_at(i)
		if _can_sum(target - c.card_data.rank, remaining_pool):
			valid.append(c)
	return valid


func _can_sum(target: int, pool: Array[CardUI]) -> bool:
	if target == 0: return true
	if target < 0: return false
	if pool.is_empty(): return false
	
	var first = pool[0]
	var rest = pool.slice(1)
	
	# Try including first
	if _can_sum(target - first.card_data.rank, rest): return true
	# Try excluding first
	if _can_sum(target, rest): return true
	
	return false


# ------------------------------------------------------------------------------
# --- STATE SYNC & ANIMATION ---
# ------------------------------------------------------------------------------

func _on_server_state_updated(server_data: Dictionary) -> void:
	if not server_data.has("state"): return
	var game_state = server_data.get("state")
	
	# 1. Update Turn Info
	var current_player = game_state.get("currentTurnPlayer", "")
	is_my_turn = (current_player == MY_PLAYER_ID)
	waiting_label.visible = not is_my_turn
	
	if not is_my_turn:
		_reset_selection()

	# 2. Animate Last Move
	var pgn = game_state.get("lastMovePgn", "")
	if pgn == null: # Handle cases like first move
		pgn = ""
	if not pgn.is_empty():
		# If it is now my turn, the last move was made by opponent (p2)
		# If it is NOT my turn, the last move was made by me (p1)
		var mover_id = "p2" if is_my_turn else "p1"
		await _animate_move(pgn, mover_id)
		print("Animation of last move complete.")

	# 3. Reconcile State (Draws, Corrections)
	print("Reconciling state...")
	_reconcile_state(game_state)


func _animate_move(pgn: String, mover_id: String) -> void:
	print("Animating move: ", pgn, " by ", mover_id)
	
	# Parse PGN: "7Dx7B+7S" or "7D"
	var parts = pgn.split("x")
	var played_card_code = parts[0]
	var captured_codes = []
	
	if parts.size() > 1:
		captured_codes = parts[1].split("+")

	# --- 1. Identify/Prepare the Played Card Node ---
	var played_card_node: CardUI = null
	
	if mover_id == MY_PLAYER_ID:
		# It's my card, should be in my hand
		if card_nodes.has(played_card_code):
			played_card_node = card_nodes[played_card_code]
	else:
		# It's opponent's card. Pick a random face-down card from their hand.
		var opp_cards = _get_cards_in_anchor(opponent_hand_anchor)
		if not opp_cards.is_empty():
			played_card_node = opp_cards.pick_random()
			# Rename/Retype the card to the real revealed card
			var old_code = played_card_node.name # Likely "OPP..."
			card_nodes.erase(old_code)
			played_card_node.setup(CardData.new(played_card_code))
			card_nodes[played_card_code] = played_card_node
			played_card_node.show_card_front()

	if not is_instance_valid(played_card_node):
		printerr("Could not find card node for animation: ", played_card_code)
		return

	# --- 2. Animate ---
	
	if captured_codes.is_empty():

		var hand_tween = create_tween()

		# --- THROW ---
		var target_anchor = table_anchor
		# 1. Move to table
		# Calculate target position on table
		var table_cards = _get_table_cards()
		var card_width = 95
		var total_width = (table_cards.size() + 1) * card_width
		var start_x = -total_width / 2.0 + card_width / 2
		var target_pos = Vector2(start_x + table_cards.size() * card_width, 0) # Local to table anchor
		hand_tween.tween_property(played_card_node, "global_position", target_anchor.global_position + target_pos, 0.4).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)	
		await hand_tween.finished
		
	else:
		# --- CAPTURE ---
		var capture_pile = player_captured_anchor if mover_id == MY_PLAYER_ID else opponent_captured_anchor
		var captured_nodes: Array[CardUI] = []
		
		# Collect target nodes
		for code in captured_codes:
			if card_nodes.has(code):
				captured_nodes.append(card_nodes[code])
		
		# 1. Played card visits each target
		# We want a sequence: Hand -> Target1 -> Target2 -> ...
		var visit_tween = create_tween()
		for target in captured_nodes:
			visit_tween.tween_property(played_card_node, "global_position", target.global_position, 0.3).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
			visit_tween.tween_interval(0.1)
		
		await visit_tween.finished
		
		# 2. All cards fly to capture pile
		var fly_tween = create_tween().set_parallel(true)
		
		# Reparent safely before flying so they are on top/correct layer? 
		# Actually, for Z-index, usually reparenting to a high-up node or just leaving them is fine.
		# We'll reparent to the capture anchor at the end or begin. 
		# Let's reparent to capture anchor now so coordinates are relative? 
		# No, global_position handles it.
		
		var all_moving = [played_card_node] + captured_nodes
		for c in all_moving:
			c.reparent(capture_pile)
			# Animate to pile (stacking effect)
			fly_tween.tween_property(c, "position", Vector2(0, -2 * (capture_pile.get_child_count() + all_moving.find(c))), 0.4).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_IN_OUT)
		
		await fly_tween.finished
		
		# Add to tracked lists
		if mover_id == MY_PLAYER_ID:
			player_captured_nodes.append_array(all_moving)
		else:
			opponent_captured_nodes.append_array(all_moving)


func _reconcile_state(game_state: Dictionary) -> void:
	# This function ensures the board strictly matches the server state
	# handling draws (new cards) and fixing any drift.
	
	var all_state_codes = []
	
	# 1. My Hand
	var my_hand_codes = game_state.get("players", {}).get(MY_PLAYER_ID, {}).get("hand", [])
	_sync_anchor_group(my_hand_codes, my_hand_anchor)
	all_state_codes.append_array(my_hand_codes)
	
	# 2. Opponent Hand
	var opponent_id = "p2" if MY_PLAYER_ID == "p1" else "p1"
	var opp_hand_codes = game_state.get("players", {}).get(opponent_id, {}).get("hand", [])
	_sync_anchor_group(opp_hand_codes, opponent_hand_anchor)
	all_state_codes.append_array(opp_hand_codes)
	
	# 3. Table
	var table_codes = game_state.get("table", [])
	_sync_anchor_group(table_codes, table_anchor)
	all_state_codes.append_array(table_codes)

	# 4. Deck Count
	var deck_array = game_state.get("deck", [])
	deck_count_label.text = "Deck: %d" % deck_array.size()
	
	# 5. Cleanup (Remove nodes that shouldn't exist anymore)
	_cleanup_anchor(my_hand_anchor, my_hand_codes)
	_cleanup_anchor(opponent_hand_anchor, opp_hand_codes)
	_cleanup_anchor(table_anchor, table_codes)


func _sync_anchor_group(codes: Array, anchor: Node2D) -> void:
	for i in range(codes.size()):
		var code = codes[i]
		# Handle duplicated codes (like multiple "X"s) by appending index if needed, 
		# OR just don't rely on map for "X".
		# Actually, if we have multiple "X"s, the dictionary keys will collide.
		# Ideally we'd have unique IDs. 
		# Workaround: If code is "X", use a special key strategy or just linear scan?
		# Let's assume unique codes for now unless they are hidden.
		# If hidden "X"s are sent, we might have issues.
		# However, for this task, I will trust the user provided codes are usable or I'll just use the code as key.
		
		var card_node: CardUI
		
		# If code is "X", we have a collision problem if we use it as a key.
		# But wait, earlier I was generating "OPP_%d".
		# If the server sends "X", "X", "X", we can't key them by "X".
		# We need a way to map server index to local node?
		# Or just wipe and recreate opponent hand? No, animation.
		
		# Better approach for "X"s:
		# If we have "X"s, check if we have enough "X" nodes in the anchor.
		# But the requested change is to USE _sync_anchor_group.
		# Let's assume for now codes are unique OR we only get one "X" at a time?
		# No, opponent hand has multiple cards.
		# If server sends ["X", "X", "X"], dictionary `card_nodes["X"]` can only hold one.
		
		# If the code is "X", we should probably use a unique local ID.
		# But the server state sends "X".
		# I will stick to the plan: Implement the function as requested.
		
		if card_nodes.has(code) and code != "X":
			card_node = card_nodes[code]
			if card_node.get_parent() != anchor:
				card_node.reparent(anchor)
		elif code == "X":
			# Special handling for X: find an available X node in the anchor or create one
			var existing_x = _find_available_x_node(anchor, i)
			if existing_x:
				card_node = existing_x
			else:
				card_node = _create_card(code, anchor, deck_pos.global_position)
				# We don't store X in card_nodes dictionary to avoid collision, 
				# or we store it in a list?
				# Let's just NOT store "X" in card_nodes map, since it's transient.
		else:
			# New Card (Draw)
			card_node = _create_card(code, anchor, deck_pos.global_position)
			card_nodes[code] = card_node
		
		# Layout calculation
		var card_width = 95
		var total_width = codes.size() * card_width
		var start_x = -total_width / 2.0 + card_width / 2.0
		var target_pos = Vector2(start_x + i * card_width, 0) # Local to anchor
		
		# Always animate for smoothness.
		var current_local = card_node.position
		if current_local.distance_to(target_pos) > 5.0:
			card_node.animate_move(anchor.global_position + target_pos, i * 0.05)


func _find_available_x_node(anchor: Node2D, index: int) -> CardUI:
	# Find the ith card in anchor that is an "X"
	var xs = []
	for c in anchor.get_children():
		if c is CardUI and c.card_data._to_string() == "X":
			xs.append(c)
	
	if index < xs.size():
		return xs[index]
	return null


func _cleanup_anchor(anchor: Node2D, valid_codes: Array) -> void:
	var valid_x_count = valid_codes.count("X")
	var current_x_count = 0
	
	for child in anchor.get_children():
		if child is CardUI:
			if not child.card_data: continue
			var code = child.card_data._to_string()
			
			if code == "X":
				current_x_count += 1
				if current_x_count > valid_x_count:
					child.queue_free()
			elif not valid_codes.has(code):
				card_nodes.erase(code)
				child.queue_free()


func _get_cards_in_anchor(anchor: Node2D) -> Array[CardUI]:
	var list: Array[CardUI] = []
	for c in anchor.get_children():
		if c is CardUI: list.append(c)
	return list

# ------------------------------------------------------------------------------
# --- HELPERS ---
# ------------------------------------------------------------------------------

func _on_start_button_pressed() -> void:
	NetworkManager.start_game()
	start_button.hide()


func _create_card(card_code: String, parent: Node, start_pos: Vector2) -> CardUI:
	var card_data = CardData.new(card_code)
	var card_node: CardUI = CardScene.instantiate()
	parent.add_child(card_node)
	card_node.global_position = start_pos
	card_node.setup(card_data)
	card_node.card_ui_clicked.connect(_on_card_clicked)
	return card_node
