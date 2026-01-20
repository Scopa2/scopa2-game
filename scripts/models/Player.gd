class_name Player
extends Resource

@export var player_name: String
@export var is_human: bool
@export var hand: Array[Card] = []
@export var captured_cards: Array[Card] = []
@export var scope_count: int = 0

signal card_played(card: Card)
signal turn_ended()

func _init(_name: String = "Player", _is_human: bool = true):
	player_name = _name
	is_human = _is_human
	hand = []
	captured_cards = []
	scope_count = 0

func add_to_hand(card: Card) -> void:
	hand.append(card)

func remove_from_hand(card: Card) -> void:
	hand.erase(card)

func capture_cards(cards: Array) -> void:
	for card in cards:
		if card is Card:
			captured_cards.append(card)

func add_scopa() -> void:
	scope_count += 1

func get_hand_size() -> int:
	return hand.size()

func has_cards() -> bool:
	return hand.size() > 0

func clear_hand() -> void:
	hand.clear()

func get_captured_count() -> int:
	return captured_cards.size()

func get_denari_count() -> int:
	var count = 0
	for card in captured_cards:
		if card.is_denari():
			count += 1
	return count

func has_sette_bello() -> bool:
	for card in captured_cards:
		if card.is_sette_bello():
			return true
	return false

func get_primiera_best_per_suit() -> Dictionary:
	var best = {"D": null, "S": null, "C": null, "B": null}
	for card in captured_cards:
		if best[card.suit] == null or card.get_primiera_value() > best[card.suit].get_primiera_value():
			best[card.suit] = card
	return best

func calculate_primiera_score() -> int:
	var best = get_primiera_best_per_suit()
	var total = 0
	for suit in best:
		if best[suit] != null:
			total += best[suit].get_primiera_value()
	return total

func has_all_suits_for_primiera() -> bool:
	var best = get_primiera_best_per_suit()
	for suit in best:
		if best[suit] == null:
			return false
	return true

func reset_for_new_round() -> void:
	hand.clear()
	captured_cards.clear()
	scope_count = 0
