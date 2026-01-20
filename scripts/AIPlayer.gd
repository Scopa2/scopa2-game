class_name AIPlayer
extends Node

const Card = preload("res://scripts/models/Card.gd")
const Player = preload("res://scripts/models/Player.gd")

enum Difficulty { EASY, MEDIUM, HARD }

var difficulty: Difficulty = Difficulty.MEDIUM

func _init(_difficulty: Difficulty = Difficulty.MEDIUM):
	difficulty = _difficulty

func choose_play(player: Player, table_cards: Array, game_manager) -> Dictionary:
	# Returns {card: Card, capture: Array}
	match difficulty:
		Difficulty.EASY:
			return choose_random_play(player, table_cards, game_manager)
		Difficulty.MEDIUM:
			return choose_smart_play(player, table_cards, game_manager)
		Difficulty.HARD:
			return choose_optimal_play(player, table_cards, game_manager)
	
	return choose_random_play(player, table_cards, game_manager)

func choose_random_play(player: Player, table_cards: Array, game_manager) -> Dictionary:
	# Just pick a random card and let the game auto-select capture
	var hand = player.hand
	if hand.size() == 0:
		return {}
	
	var random_card = hand[randi() % hand.size()]
	return {"card": random_card, "capture": []}

func choose_smart_play(player: Player, table_cards: Array, game_manager) -> Dictionary:
	var best_play = {"card": null, "capture": [], "score": -1000}
	
	for card in player.hand:
		var captures = game_manager.find_all_capture_combinations(card)
		
		if captures.size() > 0:
			# Evaluate each capture option
			for capture in captures:
				var score = evaluate_capture(card, capture, table_cards, game_manager)
				if score > best_play["score"]:
					best_play = {"card": card, "capture": capture, "score": score}
		else:
			# No capture possible - evaluate placing on table
			var score = evaluate_placement(card, table_cards)
			if score > best_play["score"]:
				best_play = {"card": card, "capture": [], "score": score}
	
	if best_play["card"] == null and player.hand.size() > 0:
		best_play["card"] = player.hand[0]
	
	return best_play

func choose_optimal_play(player: Player, table_cards: Array, game_manager) -> Dictionary:
	# Same as smart but with more aggressive scopa-seeking
	var best_play = choose_smart_play(player, table_cards, game_manager)
	
	# Extra weight for potential scopas
	for card in player.hand:
		var captures = game_manager.find_all_capture_combinations(card)
		for capture in captures:
			# Check if this would clear the table
			if capture.size() == table_cards.size() and table_cards.size() > 0:
				return {"card": card, "capture": capture}
	
	return best_play

func evaluate_capture(card: Card, capture: Array, table_cards: Array, game_manager) -> int:
	var score = 0
	
	# Base score for number of cards captured
	score += capture.size() * 10
	
	# Bonus for capturing Denari
	for captured_card in capture:
		if captured_card.is_denari():
			score += 15
		if captured_card.is_sette_bello():
			score += 50  # Sette Bello is very valuable
		if captured_card.rank == 7:
			score += 20  # Sevens are important for Primiera
	
	# Huge bonus for Scopa (clearing table)
	if capture.size() == table_cards.size():
		score += 100
	
	# Bonus for using a lower-value card to capture
	score += (10 - card.rank) * 2
	
	return score

func evaluate_placement(card: Card, table_cards: Array) -> int:
	var score = -50  # Base penalty for not capturing
	
	# Avoid placing valuable cards
	if card.is_sette_bello():
		score -= 100  # Never place Sette Bello if possible
	elif card.is_denari():
		score -= 30
	elif card.rank == 7:
		score -= 40  # Sevens are valuable for Primiera
	
	# Prefer placing cards that are hard to capture
	# Higher ranks are generally safer as they need exact matches or large combos
	score += card.rank * 2
	
	# Avoid creating easy scopa opportunities
	var total_on_table = 0
	for tc in table_cards:
		total_on_table += tc.rank
	
	# If placing this card would make table sum ≤ 10, opponent might scopa
	if total_on_table + card.rank <= 10:
		score -= 30
	
	return score

func think_delay() -> float:
	# Return a delay time to simulate "thinking"
	return randf_range(0.5, 1.5)
