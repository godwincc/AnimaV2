extends Node
class_name SignalRClient

## Low-level SignalR client. Most users should use [HubConnection] instead.
##
## Manages the SignalR JSON Hub Protocol handshake, message parsing, and
## the underlying [WebSocketTransport]. Tracks pending invocations so that
## [code]await[/code] can resolve on the matching Completion message.

signal connected
signal disconnected
signal error_occurred(error_message: String)
## Emitted when the server sends an Invocation (type 1) message.
signal message_received(method_name: String, arguments: Array)
## Emitted when the server sends a Completion (type 3) for an awaited invocation.
signal completion_received(invocation_id: String, result: Variant, error: Variant)

const RECORD_SEPARATOR := char(0x1e)

var _handshake := HandshakeProtocol.new()
var _handshake_sent := false
var _handshake_confirmed := false

var _parser := SignalRMessageParser.new()
var _transport := WebSocketTransport.new()

var _invocation_counter := 0
var _url: String
var _auth_token: String = ""
var _headers: Dictionary = {}

var debug_logging := false


func _ready() -> void:
	_transport.connected.connect(_on_transport_connected)
	_transport.disconnected.connect(_on_transport_disconnected)
	_transport.data_received.connect(_on_transport_data_received)
	_transport.error_occurred.connect(_on_transport_error)


func connect_to_signalr(url: String, auth_token: String = "", headers: Dictionary = {}) -> void:
	_url = url
	_auth_token = auth_token
	_headers = headers
	_handshake_sent = false
	_handshake_confirmed = false
	_transport.connect_to_url(url, auth_token, headers)


func disconnect_from_signalr() -> void:
	_transport.close()


func is_open() -> bool:
	return _handshake_confirmed and _transport.is_open()


func _process(_delta: float) -> void:
	_transport.poll()


## Sends an Invocation message (type 1). Fire-and-forget — does not wait
## for a Completion. Returns the invocation id used (mostly useful for tests).
func send_message(method: String, arguments: Array) -> String:
	_invocation_counter += 1
	var invocation_id := str(_invocation_counter)
	var msg := {
		"type": 1,
		"target": method,
		"arguments": arguments,
		"invocationId": invocation_id,
	}
	_send_object(msg)
	return invocation_id


## Returns the next invocation id without sending. Callers building their own
## payloads (e.g. [HubConnection.invoke]) use this to pre-allocate an id so
## they can register a listener before the message is sent.
func next_invocation_id() -> String:
	_invocation_counter += 1
	return str(_invocation_counter)


## Sends a raw message object as a serialized SignalR frame.
func _send_object(obj: Dictionary) -> void:
	var json := JSON.stringify(obj)
	if debug_logging:
		print("[SignalR] send: ", json)
	_transport.send(json + RECORD_SEPARATOR)


## Sends an Invocation with a caller-supplied invocation id. Used by
## [HubConnection.invoke] so it can register an awaiter before transmission.
func send_invocation_with_id(method: String, arguments: Array, invocation_id: String) -> void:
	var msg := {
		"type": 1,
		"target": method,
		"arguments": arguments,
		"invocationId": invocation_id,
	}
	_send_object(msg)


func _parse_message(obj: Dictionary) -> void:
	if not obj.has("type"):
		return

	match int(obj["type"]):
		1:  # Invocation
			if obj.has("target") and obj.has("arguments"):
				message_received.emit(obj["target"], obj["arguments"])
		3:  # Completion
			var invocation_id: String = str(obj.get("invocationId", ""))
			var result: Variant = obj.get("result")
			var err: Variant = obj.get("error")
			completion_received.emit(invocation_id, result, err)
		6:  # Ping — keep-alive
			pass
		7:  # Close
			var close_err: Variant = obj.get("error")
			if close_err != null:
				error_occurred.emit("Server closed connection: " + str(close_err))
			else:
				error_occurred.emit("Connection closed by server")


func _on_transport_connected() -> void:
	if not _handshake_sent:
		var handshake_msg := _handshake.get_handshake_message()
		_transport.send(handshake_msg)
		_handshake_sent = true
		if debug_logging:
			print("[SignalR] handshake sent")


func _on_transport_data_received(message: String) -> void:
	if not _handshake_confirmed:
		if _handshake.is_handshake_acknowledged(message):
			_handshake_confirmed = true
			if debug_logging:
				print("[SignalR] handshake confirmed")
			connected.emit()
		return
	var parsed_messages := _parser.parse_raw_messages(message)
	for parsed in parsed_messages:
		_parse_message(parsed)


func _on_transport_disconnected() -> void:
	_handshake_confirmed = false
	disconnected.emit()


func _on_transport_error(msg: String) -> void:
	error_occurred.emit(msg)
