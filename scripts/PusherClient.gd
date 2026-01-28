class_name PusherClient
extends Node

## A Godot client for handling the Pusher protocol over WebSockets,
## compatible with Laravel Reverb.

signal connected
signal event_received(event_name: String, data: Variant)
signal disconnected

enum State {
	DISCONNECTED,
	CONNECTING,
	CONNECTED,
}

const PING_INTERVAL = 25.0 # Seconds

var peer: WebSocketPeer
var current_state: State = State.DISCONNECTED

var _ping_timer: Timer
var _socket_id: String

func _ready() -> void:
	peer = WebSocketPeer.new()
	_ping_timer = Timer.new()
	_ping_timer.wait_time = PING_INTERVAL
	_ping_timer.one_shot = false
	_ping_timer.timeout.connect(_send_ping)
	add_child(_ping_timer)

func _process(delta: float) -> void:
	if current_state == State.DISCONNECTED:
		return
	
	peer.poll()
	var new_state = peer.get_ready_state()
	
	if new_state == WebSocketPeer.STATE_CLOSED:
		_disconnect()
		return

	while peer.get_available_packet_count() > 0:
		var packet = peer.get_packet()
		_parse_message(packet.get_string_from_utf8())


func connect_to_server(url: String) -> void:
	if current_state != State.DISCONNECTED:
		printerr("PusherClient: Already connected or connecting.")
		return
	
	print("PusherClient: Connecting to ", url)
	current_state = State.CONNECTING
	var err = peer.connect_to_url(url)
	if err != OK:
		printerr("PusherClient: Failed to connect to URL.")
		current_state = State.DISCONNECTED


func subscribe(channel_name: String) -> void:
	if current_state != State.CONNECTED:
		printerr("PusherClient: Cannot subscribe, not connected.")
		return
		
	var payload = {
		"event": "pusher:subscribe",
		"data": {
			"channel": channel_name
		}
	}
	_send_json(payload)
	print("PusherClient: Subscribed to channel '", channel_name, "'")

# --- Internal Logic ---

func _send_json(data: Dictionary) -> void:
	var json_string = JSON.stringify(data)
	var err = peer.send_text(json_string)
	if err != OK:
		printerr("PusherClient: Error sending JSON: ", err)

func _parse_message(raw_message: String) -> void:
	var result = JSON.parse_string(raw_message)
	if result == null:
		printerr("PusherClient: Failed to parse incoming JSON: ", raw_message)
		return
	
	var message: Dictionary = result
	var event_name: String = message.get("event", "")
	
	# Pusher-specific protocol events
	match event_name:
		"pusher:connection_established":
			_handle_connection_established(message.get("data"))
		"pusher:ping":
			_send_json({"event": "pusher:pong", "data": {}})
		"pusher_internal:subscription_succeeded":
			print("PusherClient: Subscription succeeded for channel '", message.get("channel"), "'")
		_:
			# This is a user-defined event. The 'data' payload can be a
			# stringified JSON or an actual dictionary.
			var data_field = message.get("data")
			var data_payload: Variant

			if data_field is String:
				data_payload = JSON.parse_string(data_field)
				if data_payload == null:
					printerr("PusherClient: Failed to parse data string for event '", event_name, "': ", data_field)
					return
			elif data_field is Dictionary:
				data_payload = data_field
			else:
				printerr("PusherClient: Unexpected data type for event '", event_name, "': ", typeof(data_field))
				return
			
			emit_signal("event_received", event_name, data_payload)


func _handle_connection_established(data_str: String) -> void:
	var data = JSON.parse_string(data_str)
	if data == null:
		printerr("PusherClient: Failed to parse connection data.")
		_disconnect()
		return
		
	_socket_id = data.get("socket_id")
	var activity_timeout = data.get("activity_timeout", 30)
	_ping_timer.wait_time = activity_timeout - 5 # Send ping before timeout
	_ping_timer.start()
	
	current_state = State.CONNECTED
	emit_signal("connected")
	print("PusherClient: Connection established. Socket ID: ", _socket_id)


func _send_ping() -> void:
	if current_state == State.CONNECTED:
		_send_json({"event": "pusher:ping", "data": {}})


func _disconnect() -> void:
	if current_state == State.DISCONNECTED:
		return
		
	print("PusherClient: Disconnected.")
	peer.close()
	_ping_timer.stop()
	current_state = State.DISCONNECTED
	_socket_id = ""
	emit_signal("disconnected")
