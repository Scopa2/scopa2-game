class_name CardData
extends RefCounted

## A data-only object representing a single card.
## It parses a server code (e.g., "7D") into rank and suit.

var rank: int
var suit: String

# Constructor: takes the server's card code.
func _init(server_code: String):
	if server_code.is_empty():
		printerr("CardData: Received an empty server_code.")
		rank = -1
		suit = "?"
		return

	suit = server_code.right(1)
	var rank_str = server_code.left(server_code.length() - 1)
	
	if not rank_str.is_valid_int():
		printerr("CardData: Invalid rank string '%s' from server_code '%s'" % [rank_str, server_code])
		rank = -1
		return
		
	rank = rank_str.to_int()

## A helper function to get a string representation, similar to the example.
func _to_string() -> String:
	return "%d%s" % [rank, suit]
