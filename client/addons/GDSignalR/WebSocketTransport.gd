extends RefCounted
class_name WebSocketTransport

## Thin wrapper around [WebSocketPeer] used by [SignalRClient].
## RefCounted so it tracks its owner's lifetime — [SignalRClient] drives
## [method poll] from its own [code]_process[/code], so no Node behavior
## is needed.

signal connected
signal disconnected
signal data_received(data: String)
signal error_occurred(error_message: String)

var _socket := WebSocketPeer.new()
var _url: String = ""
var _is_open: bool = false
var _last_state: int = WebSocketPeer.STATE_CLOSED


func connect_to_url(url: String, auth_token: String = "", headers: Dictionary = {}) -> void:
	_url = url

	# ASP.NET Core SignalR reads the bearer from the access_token query param
	# on the WebSocket upgrade request, because browser WS APIs cannot set
	# Authorization headers. We follow the same convention.
	if auth_token != "":
		if "?" in _url:
			_url += "&access_token=%s" % auth_token
		else:
			_url += "?access_token=%s" % auth_token

	var header_array: PackedStringArray = []
	for key in headers.keys():
		header_array.append("%s: %s" % [key, str(headers[key])])
	_socket.handshake_headers = header_array

	_is_open = false
	_last_state = WebSocketPeer.STATE_CLOSED

	var err := _socket.connect_to_url(_url)
	if err != OK:
		error_occurred.emit("WebSocket connect_to_url failed with error code %d" % err)


func close() -> void:
	if _socket.get_ready_state() != WebSocketPeer.STATE_CLOSED:
		_socket.close()


func poll() -> void:
	_socket.poll()

	var state := _socket.get_ready_state()

	if state == WebSocketPeer.STATE_OPEN and not _is_open:
		_is_open = true
		connected.emit()
	elif state == WebSocketPeer.STATE_CLOSED and _last_state != WebSocketPeer.STATE_CLOSED:
		if _is_open:
			_is_open = false
		disconnected.emit()

	_last_state = state

	while _socket.get_available_packet_count() > 0:
		var packet := _socket.get_packet()
		var message := packet.get_string_from_utf8()
		data_received.emit(message)


func send(data: String) -> void:
	if _socket.get_ready_state() == WebSocketPeer.STATE_OPEN:
		_socket.send_text(data)
	else:
		error_occurred.emit("Tried to send on closed WebSocket")


func is_open() -> bool:
	return _is_open
