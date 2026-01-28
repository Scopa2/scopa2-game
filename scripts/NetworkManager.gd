extends Node

## Manages all HTTP communication with the Laravel Scopa backend.
## Emits the state_updated signal whenever a new game state is received.

signal state_updated(state: Dictionary)

# The base URL of the backend API.
const BASE_URL = "http://localhost:8000/api"

# We'll store the game ID once a game is started.
var _game_id: String = ""

# We use an HTTPRequest node to handle the requests.
var _http_request: HTTPRequest

func _ready() -> void:
	# Create the HTTPRequest node and connect its completion signal.
	_http_request = HTTPRequest.new()
	add_child(_http_request)
	_http_request.request_completed.connect(_on_request_completed)

## Starts a new game by calling the backend.
func start_game() -> void:
	# For now, we assume a simple POST request starts a game with default settings.
	var error = _http_request.request(BASE_URL + "/games", [], HTTPClient.METHOD_POST)
	if error != OK:
		printerr("NetworkManager: An error occurred in HTTPRequest.start_game().")

## Sends a player action to the server (e.g., playing a card).
func send_action(action: String) -> void:
	if _game_id.is_empty():
		printerr("NetworkManager: Cannot send action, game ID is not set.")
		return

	var url = "%s/games/%s/action" % [BASE_URL, _game_id]
	
	# The action is sent as a JSON payload.
	var headers = ["Content-Type: application/json"]
	var body = JSON.stringify({"action": action})
	
	var error = _http_request.request(url, headers, HTTPClient.METHOD_POST, body)
	if error != OK:
		printerr("NetworkManager: An error occurred in HTTPRequest.send_action().")

## Callback for when any HTTP request finishes.
func _on_request_completed(_result: int, response_code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	if response_code < 200 or response_code >= 300:
		printerr("NetworkManager: Request failed with code %d. Body: %s" % [response_code, body.get_string_from_utf8()])
		return

	var json = JSON.parse_string(body.get_string_from_utf8())
	if json == null:
		printerr("NetworkManager: Failed to parse JSON response.")
		return

	var data: Dictionary = json
	
	# If this is the initial response from POST /games, it only contains the game_id.
	# A full game state should have a "players" dictionary.
	if data.has("game_id") and not data.has("players"):
		# Store the game id if we don't have it yet.
		if _game_id.is_empty():
			_game_id = data.get("game_id")
		
		# Now, immediately fetch the full game state.
		_fetch_game_state()
		# Return early; do not emit the partial state.
		return

	# If we are here, we should have a full game state.
	# Let's ensure the game_id is stored.
	if data.has("game_id") and _game_id.is_empty():
		_game_id = data.get("game_id")
	
	# We emit the new, full state for the rest of the game to consume.
	emit_signal("state_updated", data)


## Fetches the full game state from the server.
func _fetch_game_state() -> void:
	if _game_id.is_empty():
		printerr("NetworkManager: Cannot fetch state, game ID is not set.")
		return
	
	var url = "%s/games/%s" % [BASE_URL, _game_id]
	var error = _http_request.request(url, [], HTTPClient.METHOD_GET)
	if error != OK:
		printerr("NetworkManager: An error occurred in HTTPRequest._fetch_game_state().")
