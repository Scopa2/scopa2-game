class_name Card
extends Resource

@export var id: int
@export var rank: int  # 1-10 (1=Asso, 8=Fante, 9=Cavallo, 10=Re)
@export var suit: String  # D=Denari, S=Spade, C=Coppe, B=Bastoni

# Scopa point values for Primiera calculation
const PRIMIERA_VALUES = {
	7: 21,
	6: 18,
	1: 16,  # Asso
	5: 15,
	4: 14,
	3: 13,
	2: 12,
	8: 10,  # Fante
	9: 10,  # Cavallo
	10: 10  # Re
}

func _init(_id: int = 0, _rank: int = 1, _suit: String = "D"):
	id = _id
	rank = _rank
	suit = _suit

func get_primiera_value() -> int:
	return PRIMIERA_VALUES.get(rank, 0)

func is_sette_bello() -> bool:
	return rank == 7 and suit == "D"

func is_denari() -> bool:
	return suit == "D"

func _to_string() -> String:
	var rank_names = {1: "A", 8: "F", 9: "C", 10: "R"}
	var rank_str = rank_names.get(rank, str(rank))
	var suit_names = {"D": "D", "S": "S", "C": "C", "B": "B"}
	return "%s%s" % [rank_str, suit_names.get(suit, suit)]

func get_display_name() -> String:
	var rank_names = {1: "Asso", 2: "Due", 3: "Tre", 4: "Quattro", 5: "Cinque", 
					  6: "Sei", 7: "Sette", 8: "Fante", 9: "Cavallo", 10: "Re"}
	var suit_names = {"D": "Denari", "S": "Spade", "C": "Coppe", "B": "Bastoni"}
	return "%s di %s" % [rank_names.get(rank, str(rank)), suit_names.get(suit, suit)]

# Get sprite sheet position (assuming 10 columns x 4 rows layout)
func get_sprite_frame() -> int:
	var suit_index = ["C", "B", "D", "S"].find(suit)
	var rank_index = rank - 1
	return suit_index * 10 + rank_index
