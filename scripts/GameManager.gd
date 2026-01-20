class_name GameManager
extends Node

signal game_state_changed()
signal round_ended(player1_score: int, player2_score: int)
signal game_ended(winner: Player)
signal scopa_scored(player: Player)
signal cards_captured(player: Player, cards: Array)
signal turn_changed(current_player: Player)
signal log_message(message: String)

const Card = preload("res://scripts/models/Card.gd")
const Player = preload("res://scripts/models/Player.gd")

const SUITS = ["D", "S", "C", "B"]
const RANKS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
const WINNING_SCORE = 11

var deck: Array[Card] = []
var table_cards: Array[Card] = []
var player1: Player
var player2: Player
var current_player: Player
var last_capturer: Player  # Who captured last (gets remaining table cards)
var player1_total_score: int = 0
var player2_total_score: int = 0

func _init():
	player1 = Player.new("Giocatore", true)
	player2 = Player.new("Computer", false)

func create_deck() -> Array[Card]:
	var new_deck: Array[Card] = []
	var next_id: int = 0
	for suit in SUITS:
		for rank in RANKS:
			var card := Card.new(next_id, rank, suit)
			new_deck.append(card)
			next_id += 1
	new_deck.shuffle()
	return new_deck

func start_new_game() -> void:
	player1_total_score = 0
	player2_total_score = 0
	player1.reset_for_new_round()
	player2.reset_for_new_round()
	start_new_round()

func start_new_round() -> void:
	player1.reset_for_new_round()
	player2.reset_for_new_round()
	deck = create_deck()
	table_cards.clear()
	last_capturer = null
	
	# Deal initial 4 cards to table
	for i in 4:
		var card = deck.pop_back()
		table_cards.append(card)
	
	# Deal 3 cards to each player
	deal_cards_to_players()
	
	# Player 1 starts
	current_player = player1
	emit_signal("turn_changed", current_player)
	emit_signal("game_state_changed")
	emit_signal("log_message", "Nuova mano iniziata!")

func deal_cards_to_players() -> void:
	for i in 3:
		if deck.size() > 0:
			player1.add_to_hand(deck.pop_back())
		if deck.size() > 0:
			player2.add_to_hand(deck.pop_back())
	emit_signal("game_state_changed")

func find_all_capture_combinations(played_card: Card) -> Array:
	# Returns array of possible capture combinations (each is an array of cards)
	var combinations: Array = []
	
	# Check for direct match first (single card with same rank)
	for table_card in table_cards:
		if table_card.rank == played_card.rank:
			combinations.append([table_card])
	
	# Find all combinations that sum to the played card's rank
	var sum_combinations = find_sum_combinations(played_card.rank)
	for combo in sum_combinations:
		if combo.size() > 1:  # Only add if it's a multi-card combination
			combinations.append(combo)
	
	return combinations

func find_sum_combinations(target_sum: int) -> Array:
	var results: Array = []
	var n = table_cards.size()
	
	# Generate all possible subsets of table cards
	for mask in range(1, (1 << n)):
		var subset: Array = []
		var subset_sum = 0
		
		for i in range(n):
			if mask & (1 << i):
				subset.append(table_cards[i])
				subset_sum += table_cards[i].rank
		
		if subset_sum == target_sum:
			results.append(subset)
	
	return results

func play_card(player: Player, card: Card, capture_choice: Array = []) -> Dictionary:
	# Returns result of the play
	var result = {
		"captured": [],
		"is_scopa": false,
		"card_played": card
	}
	
	if player != current_player:
		emit_signal("log_message", "Non è il tuo turno!")
		return result
	
	if not card in player.hand:
		emit_signal("log_message", "Carta non in mano!")
		return result
	
	player.remove_from_hand(card)
	
	var possible_captures = find_all_capture_combinations(card)
	
	if possible_captures.size() > 0:
		var cards_to_capture: Array
		
		# If player specified a capture choice, use it (if valid)
		if capture_choice.size() > 0:
			var valid_choice = false
			for combo in possible_captures:
				if arrays_equal(combo, capture_choice):
					valid_choice = true
					break
			if valid_choice:
				cards_to_capture = capture_choice
			else:
				# Default to first option with direct match priority
				cards_to_capture = get_best_capture(possible_captures, card)
		else:
			# Auto-select: prefer direct match, then largest combination
			cards_to_capture = get_best_capture(possible_captures, card)
		
		# Perform capture
		for table_card in cards_to_capture:
			table_cards.erase(table_card)
		
		# Convert to generic array to avoid type issues
		var capture_array: Array = []
		for c in cards_to_capture:
			capture_array.append(c)
		player.capture_cards(capture_array)
		player.capture_cards([card])  # Also capture the played card
		last_capturer = player
		
		result["captured"] = cards_to_capture
		
		# Check for scopa (table cleared, but not on last card of round)
		if table_cards.size() == 0 and (deck.size() > 0 or has_cards_in_hands()):
			player.add_scopa()
			result["is_scopa"] = true
			emit_signal("scopa_scored", player)
			emit_signal("log_message", "%s fa SCOPA!" % player.player_name)
		
		emit_signal("cards_captured", player, cards_to_capture)
		emit_signal("log_message", "%s cattura %d carte" % [player.player_name, cards_to_capture.size()])
	else:
		# No capture possible, place card on table
		table_cards.append(card)
		emit_signal("log_message", "%s gioca %s sul tavolo" % [player.player_name, card.get_display_name()])
	
	# Switch turns
	switch_turn()
	
	return result

func get_best_capture(combinations: Array, played_card: Card) -> Array:
	# Prefer direct match (same rank single card)
	for combo in combinations:
		if combo.size() == 1 and combo[0].rank == played_card.rank:
			return combo
	
	# Otherwise return the largest combination
	var best = combinations[0]
	for combo in combinations:
		if combo.size() > best.size():
			best = combo
	return best

func arrays_equal(a: Array, b: Array) -> bool:
	if a.size() != b.size():
		return false
	for item in a:
		if not item in b:
			return false
	return true

func has_cards_in_hands() -> bool:
	return player1.has_cards() or player2.has_cards()

func switch_turn() -> void:
	# Check if we need to deal more cards
	if not player1.has_cards() and not player2.has_cards():
		if deck.size() > 0:
			deal_cards_to_players()
			emit_signal("log_message", "Nuove carte distribuite!")
		else:
			# Round is over
			end_round()
			return
	
	# Switch to other player
	current_player = player2 if current_player == player1 else player1
	emit_signal("turn_changed", current_player)
	emit_signal("game_state_changed")

func end_round() -> void:
	# Give remaining table cards to last capturer
	if last_capturer != null and table_cards.size() > 0:
		var remaining: Array = []
		for c in table_cards:
			remaining.append(c)
		last_capturer.capture_cards(remaining)
		emit_signal("log_message", "%s prende le carte rimaste" % last_capturer.player_name)
		table_cards.clear()
	
	# Calculate scores
	var round_scores = calculate_round_scores()
	player1_total_score += round_scores["player1"]
	player2_total_score += round_scores["player2"]
	
	emit_signal("round_ended", round_scores["player1"], round_scores["player2"])
	emit_signal("game_state_changed")
	
	# Check for winner
	if player1_total_score >= WINNING_SCORE or player2_total_score >= WINNING_SCORE:
		var winner = player1 if player1_total_score > player2_total_score else player2
		emit_signal("game_ended", winner)
	else:
		emit_signal("log_message", "Punteggio: %s %d - %s %d" % [
			player1.player_name, player1_total_score,
			player2.player_name, player2_total_score
		])

func calculate_round_scores() -> Dictionary:
	var p1_score = 0
	var p2_score = 0
	var breakdown = []
	
	# 1. Carte (most cards) - 1 point
	var p1_cards = player1.get_captured_count()
	var p2_cards = player2.get_captured_count()
	if p1_cards > p2_cards:
		p1_score += 1
		breakdown.append("%s: Carte (%d vs %d)" % [player1.player_name, p1_cards, p2_cards])
	elif p2_cards > p1_cards:
		p2_score += 1
		breakdown.append("%s: Carte (%d vs %d)" % [player2.player_name, p2_cards, p1_cards])
	
	# 2. Denari (most Denari cards) - 1 point
	var p1_denari = player1.get_denari_count()
	var p2_denari = player2.get_denari_count()
	if p1_denari > p2_denari:
		p1_score += 1
		breakdown.append("%s: Denari (%d vs %d)" % [player1.player_name, p1_denari, p2_denari])
	elif p2_denari > p1_denari:
		p2_score += 1
		breakdown.append("%s: Denari (%d vs %d)" % [player2.player_name, p2_denari, p1_denari])
	
	# 3. Sette Bello (7 of Denari) - 1 point
	if player1.has_sette_bello():
		p1_score += 1
		breakdown.append("%s: Sette Bello" % player1.player_name)
	elif player2.has_sette_bello():
		p2_score += 1
		breakdown.append("%s: Sette Bello" % player2.player_name)
	
	# 4. Primiera (best card from each suit) - 1 point
	var p1_primiera = player1.calculate_primiera_score()
	var p2_primiera = player2.calculate_primiera_score()
	var p1_has_all = player1.has_all_suits_for_primiera()
	var p2_has_all = player2.has_all_suits_for_primiera()
	
	if p1_has_all and p2_has_all:
		if p1_primiera > p2_primiera:
			p1_score += 1
			breakdown.append("%s: Primiera (%d vs %d)" % [player1.player_name, p1_primiera, p2_primiera])
		elif p2_primiera > p1_primiera:
			p2_score += 1
			breakdown.append("%s: Primiera (%d vs %d)" % [player2.player_name, p2_primiera, p1_primiera])
	elif p1_has_all:
		p1_score += 1
		breakdown.append("%s: Primiera (avversario incompleto)" % player1.player_name)
	elif p2_has_all:
		p2_score += 1
		breakdown.append("%s: Primiera (avversario incompleto)" % player2.player_name)
	
	# 5. Scope points
	p1_score += player1.scope_count
	p2_score += player2.scope_count
	if player1.scope_count > 0:
		breakdown.append("%s: %d Scopa(e)" % [player1.player_name, player1.scope_count])
	if player2.scope_count > 0:
		breakdown.append("%s: %d Scopa(e)" % [player2.player_name, player2.scope_count])
	
	for line in breakdown:
		emit_signal("log_message", line)
	
	return {
		"player1": p1_score,
		"player2": p2_score,
		"breakdown": breakdown
	}

func get_table_cards() -> Array[Card]:
	return table_cards

func get_deck_count() -> int:
	return deck.size()

func is_game_over() -> bool:
	return player1_total_score >= WINNING_SCORE or player2_total_score >= WINNING_SCORE
