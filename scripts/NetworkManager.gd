extends Node

const PusherClient = preload("res://scripts/PusherClient.gd")

signal state_updated(state: Dictionary)

# --- Config ---
const BASE_URL = "http://localhost:8000/api"
const REVERB_URL = "ws://127.0.0.1:6001/app/app-key?protocol=7&client=Godot&version=1.0.0"

# --- State ---
var _game_id: String = ""
var _player_id: String = "p1"

# --- Nodes ---
var _http_request: HTTPRequest
var _pusher_client: PusherClient


func _ready() -> void:
	# Setup HTTP client (always needed for actions)
	_http_request = HTTPRequest.new()
	add_child(_http_request)
	_http_request.request_completed.connect(_on_request_completed)
	
	# Setup WebSocket client if enabled
	print("NetworkManager: WebSocket mode enabled.")
	_pusher_client = PusherClient.new()
	add_child(_pusher_client)
	_pusher_client.event_received.connect(_on_pusher_event_received)
	_pusher_client.connect_to_server(REVERB_URL)


# ------------------------------------------------------------------------------
# --- Public API ---
# ------------------------------------------------------------------------------

func start_game() -> void:
	var headers = ["Accept: application/json"]
	var error = _http_request.request(BASE_URL + "/games", headers, HTTPClient.METHOD_POST)
	if error != OK:
		printerr("NetworkManager: Error in HTTPRequest.start_game().")

func send_action(action: String) -> void:
	print("NetworkManager: Sending action: ", action)
	if _game_id.is_empty():
		printerr("NetworkManager: Cannot send action, no game ID.")
		return
	var url = "%s/games/%s/action?player=%s" % [BASE_URL, _game_id, _player_id]
	var headers = ["Content-Type: application/json", "Accept: application/json"]
	var body = JSON.stringify({"action": action})
	var error = _http_request.request(url, headers, HTTPClient.METHOD_POST, body)
	if error != OK:
		printerr("NetworkManager: Error in HTTPRequest.send_action().")


# ------------------------------------------------------------------------------
# --- Signal Handlers & Timers ---
# ------------------------------------------------------------------------------

func _on_request_completed(_result: int, response_code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	if response_code < 200 or response_code >= 300:
		printerr("NetworkManager: HTTP request failed with code %d. Body: %s" % [response_code, body.get_string_from_utf8()])
		return

	var json = JSON.parse_string(body.get_string_from_utf8())
	if json == null:
		printerr("NetworkManager: Failed to parse HTTP JSON response.")
		return
	var data: Dictionary = json
	
	# Handle the initial response from POST /games
	if data.has("game_id") and not data.has("state"):
		if _game_id.is_empty():
			_game_id = data.get("game_id")
			print("NetworkManager: Game created with ID: ", _game_id)
			#_pusher_client.subscribe("game." + _game_id)
			_pusher_client.subscribe("games")
			_fetch_http_game_state() # Fetch initial state
			return

	# On first game load, get the state of the game from the response
	if data.has("state") && data["state"]["turnIndex"] == 1:
		emit_signal("state_updated", data)


func _on_pusher_event_received(event_name: String, data: Variant) -> void:
	print("NetworkManager: WebSocket event received '", event_name, "' with data: ", data)
	if event_name == "game_sate_updated" and data is Dictionary:
		emit_signal("state_updated", data)

# ------------------------------------------------------------------------------
# --- Private Helpers ---
# ------------------------------------------------------------------------------

## Fetches the full game state from the server via HTTP.
func _fetch_http_game_state() -> void:
	if _game_id.is_empty():
		printerr("NetworkManager: Cannot fetch state, no game ID.")
		return
	
	var url = "%s/games/%s?player=%s" % [BASE_URL, _game_id, _player_id]
	var headers = ["Accept: application/json"]
	var error = _http_request.request(url, headers, HTTPClient.METHOD_GET)
	if error != OK:
		printerr("NetworkManager: Error fetching game state via HTTP.")
