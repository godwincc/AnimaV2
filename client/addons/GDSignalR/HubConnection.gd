extends Node
class_name HubConnection

## High-level SignalR Hub connection. This is the main entry point of GDSignalR.
##
## Wraps a [SignalRClient] with handler registration ([method on]), awaited
## invocations ([method invoke]), and automatic reconnection.
##
## [b]Example[/b]:
## [codeblock]
## var hub := HubConnection.new()
## add_child(hub)
## hub.on("ReceiveMessage", func(user, text): print(user, ": ", text))
## hub.connect_to_url("ws://localhost:5000/chatHub")
## await hub.connected
## hub.send("SendMessage", ["Godot", "Hello!"])
## var token: String = await hub.invoke("Login", ["alice", "pw"])
## [/codeblock]

signal connected
signal disconnected
signal error_occurred(message: String)
## Emitted when [method connect_to_url] gives up after [member _max_attempts].
signal reconnect_failed

const VERSION := "1.0.0"

var _retry_interval_seconds: float = 3.0
var _max_attempts: int = 5
var _should_retry: bool = true
var _retry_attempts: int = 0
var _reconnect_timer: Timer
var _was_ever_connected: bool = false

var debug_logging: bool = false

var _target_url: String = ""
var _target_token: String = ""
var _target_headers: Dictionary = {}

var _client: SignalRClient
var _handlers: Dictionary = {}
var _pending_invocations: Dictionary = {}


func _init() -> void:
	_client = SignalRClient.new()
	add_child(_client)

	_client.connected.connect(_on_connected)
	_client.disconnected.connect(_on_disconnected)
	_client.error_occurred.connect(_on_error)
	_client.message_received.connect(_on_message_received)
	_client.completion_received.connect(_on_completion_received)

	_reconnect_timer = Timer.new()
	_reconnect_timer.wait_time = _retry_interval_seconds
	_reconnect_timer.one_shot = true
	_reconnect_timer.timeout.connect(_try_reconnect)
	add_child(_reconnect_timer)

	if debug_logging:
		print("[SignalR] HubConnection v" + VERSION + " initialized")


## Connects to a SignalR hub. The URL should use [code]ws://[/code] or
## [code]wss://[/code] and point at the mapped hub path (e.g.
## [code]ws://localhost:5000/chatHub[/code]).
##
## [param auth_token] is appended as the [code]access_token[/code] query
## parameter (the convention ASP.NET Core SignalR uses for WebSocket auth).
## Additional HTTP headers can be sent via [param headers].
func connect_to_url(url: String, auth_token: String = "", headers: Dictionary = {}) -> void:
	_target_url = url
	_target_token = auth_token
	_target_headers = headers
	_retry_attempts = 0
	_should_retry = true
	_client.debug_logging = debug_logging
	_client.connect_to_signalr(url, auth_token, headers)


## Disconnects from the hub and prevents further automatic reconnects.
func stop() -> void:
	_should_retry = false
	if not _reconnect_timer.is_stopped():
		_reconnect_timer.stop()
	_client.disconnect_from_signalr()


## Returns [code]true[/code] once the SignalR handshake has been confirmed
## and the underlying WebSocket is open.
func is_connected_to_hub() -> bool:
	return _client.is_open()


func debug_mode(status: bool) -> void:
	debug_logging = status
	_client.debug_logging = status


## Sends a fire-and-forget Invocation message. The server method may return
## a value, but it will be discarded. Use [method invoke] if you need the
## result.
func send(method: String, args: Array = []) -> void:
	_client.send_message(method, args)


## Sends an Invocation and awaits the server's Completion message.
## Returns the server's return value, or [code]null[/code] if the call
## failed (in which case [signal error_occurred] is emitted with the error).
##
## [b]Usage[/b]:
## [codeblock]
## var token = await hub.invoke("Login", ["alice", "password"])
## if token == null:
##     return # error_occurred already fired
## [/codeblock]
##
## [param timeout_seconds] cancels the wait after the given duration and
## returns [code]null[/code]. Set to [code]0[/code] to wait forever.
func invoke(method: String, args: Array = [], timeout_seconds: float = 30.0) -> Variant:
	var invocation_id := _client.next_invocation_id()
	var awaiter := _InvocationAwaiter.new()
	_pending_invocations[invocation_id] = awaiter

	_client.send_invocation_with_id(method, args, invocation_id)

	if timeout_seconds > 0.0:
		var timeout_timer := get_tree().create_timer(timeout_seconds)
		timeout_timer.timeout.connect(func():
			if _pending_invocations.has(invocation_id):
				awaiter.completed.emit(null, "Invocation timed out after %s seconds" % timeout_seconds)
		)

	var response: Array = await awaiter.completed
	_pending_invocations.erase(invocation_id)

	var result: Variant = response[0]
	var err: Variant = response[1]
	if err != null:
		error_occurred.emit("Invocation '%s' failed: %s" % [method, str(err)])
		return null
	return result


## Registers a handler for a server-pushed method call. Replaces any
## previously-registered handler for the same method name.
func on(method_name: String, handler_func: Callable) -> void:
	_handlers[method_name] = handler_func


## Removes a previously-registered handler.
func off(method_name: String) -> void:
	_handlers.erase(method_name)


func set_retry(enabled: bool, interval: float = 3.0, max_attempts: int = 5) -> void:
	_should_retry = enabled
	_retry_interval_seconds = interval
	_max_attempts = max_attempts
	_reconnect_timer.wait_time = _retry_interval_seconds


func _on_connected() -> void:
	_was_ever_connected = true
	_retry_attempts = 0
	if debug_logging:
		print("[SignalR] connected")
	connected.emit()


func _on_disconnected() -> void:
	if debug_logging:
		print("[SignalR] disconnected")
	_fail_pending_invocations("Connection lost")
	disconnected.emit()
	_maybe_schedule_reconnect()


func _on_error(msg: String) -> void:
	if debug_logging:
		print("[SignalR] error: ", msg)
	error_occurred.emit(msg)
	if not _client.is_open():
		_maybe_schedule_reconnect()


func _on_message_received(method_name: String, args: Array) -> void:
	if _handlers.has(method_name):
		var handler: Callable = _handlers[method_name]
		handler.callv(args)
	elif debug_logging:
		print("[SignalR] unhandled method '", method_name, "' args=", args)


func _on_completion_received(invocation_id: String, result: Variant, err: Variant) -> void:
	if _pending_invocations.has(invocation_id):
		var awaiter: _InvocationAwaiter = _pending_invocations[invocation_id]
		awaiter.completed.emit(result, err)


func _maybe_schedule_reconnect() -> void:
	if not _should_retry:
		return
	if _retry_attempts >= _max_attempts:
		if debug_logging:
			print("[SignalR] reconnect attempts exhausted")
		reconnect_failed.emit()
		return
	if _reconnect_timer.is_stopped():
		if debug_logging:
			print("[SignalR] reconnecting in %ss (attempt %d/%d)" % [_retry_interval_seconds, _retry_attempts + 1, _max_attempts])
		_reconnect_timer.start()


func _try_reconnect() -> void:
	_retry_attempts += 1
	_client.connect_to_signalr(_target_url, _target_token, _target_headers)


func _fail_pending_invocations(reason: String) -> void:
	for id in _pending_invocations.keys():
		var awaiter: _InvocationAwaiter = _pending_invocations[id]
		awaiter.completed.emit(null, reason)
	_pending_invocations.clear()


## Internal helper used by [method invoke] to bridge SignalR Completion
## messages into [code]await[/code]-able signals.
class _InvocationAwaiter extends RefCounted:
	signal completed(result: Variant, error: Variant)
