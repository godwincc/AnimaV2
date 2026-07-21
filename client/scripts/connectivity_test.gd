extends Control

## DEV-ONLY CONNECTIVITY TEST SCENE.
##
## Proves the Godot client -> Anima.Server (GameHub over SignalR) pipe works end to end,
## through both the editor AND a real exported Web build. This is a plumbing milestone only --
## no real screens, no real auth UI. The hardcoded dev-account flow below is TEMPORARY and must
## be replaced once real login/registration screens exist.

const SERVER_HTTP_BASE := "http://localhost:5143"
const SERVER_WS_URL := "ws://localhost:5143/hubs/game"

# TEMPORARY hardcoded dev test account, register-if-not-exists else login. Real login screens
# will replace this entirely.
const DEV_USERNAME := "godot_dev_test"
const DEV_EMAIL := "godot_dev_test@animatest.local"
const DEV_PASSWORD := "DevTest123!"

@onready var _output: RichTextLabel = $OutputLabel

var _hub: HubConnection


func _ready() -> void:
	_log("Booting connectivity test...")
	_run()


func _run() -> void:
	var token := await _get_auth_token()
	if token == "":
		_log("[color=red]FAILED: could not obtain a JWT from either register or login.[/color]")
		return

	_log("Got JWT (%d chars)." % token.length())
	_log("Opening SignalR hub connection to %s ..." % SERVER_WS_URL)

	var ok := await _connect_hub(token)
	if not ok:
		return

	_log("[color=green]Hub connected.[/color] Calling GetRoster()...")
	var roster: Variant = await _hub.invoke("GetRoster", [])
	_log("GetRoster() raw result:")
	_log(JSON.stringify(roster, "  "))
	_log("[color=green]Connectivity test complete.[/color]")


func _get_auth_token() -> String:
	var register_body := {
		"username": DEV_USERNAME,
		"email": DEV_EMAIL,
		"password": DEV_PASSWORD,
	}
	var register_result := await _post_json("/api/auth/register", register_body)
	if register_result.code == 200:
		_log("Registered new dev account '%s'." % DEV_USERNAME)
		return register_result.body.get("token", "")

	if register_result.code == 409:
		_log("Dev account already exists, logging in instead.")
		var login_result := await _post_json("/api/auth/login", {
			"username": DEV_USERNAME,
			"password": DEV_PASSWORD,
		})
		if login_result.code == 200:
			return login_result.body.get("token", "")
		_log("Login failed: HTTP %s %s" % [login_result.code, str(login_result.body)])
		return ""

	_log("Register failed: HTTP %s %s" % [register_result.code, str(register_result.body)])
	return ""


## Posts JSON to the Auth REST API and returns {"code": int, "body": Variant}.
## code == -1 means a connection-level failure (server unreachable, DNS, etc.), not an HTTP
## status -- distinguished from real HTTP error codes so callers can tell the two apart.
func _post_json(path: String, payload: Dictionary) -> Dictionary:
	var http := HTTPRequest.new()
	add_child(http)
	http.timeout = 10.0

	var headers := ["Content-Type: application/json"]
	var body := JSON.stringify(payload)
	var err := http.request(SERVER_HTTP_BASE + path, headers, HTTPClient.METHOD_POST, body)
	if err != OK:
		http.queue_free()
		return {"code": -1, "body": "request() failed with Godot error code %d" % err}

	var response: Array = await http.request_completed
	http.queue_free()

	var result: int = response[0]
	var response_code: int = response[1]
	var response_body: PackedByteArray = response[3]

	if result != HTTPRequest.RESULT_SUCCESS:
		return {"code": -1, "body": "HTTPRequest connection-level result %d" % result}

	var text := response_body.get_string_from_utf8()
	var json := JSON.new()
	var parsed: Variant = text
	if json.parse(text) == OK:
		parsed = json.get_data()

	return {"code": response_code, "body": parsed}


## Opens the GDSignalR hub connection and polls is_connected_to_hub() with a manual 10s
## timeout, rather than a bare `await _hub.connected`, since that signal never fires at all on
## a connection-level failure (the addon only emits error_occurred/disconnected in that case) --
## an unguarded await would hang this scene forever instead of surfacing a clear failure.
func _connect_hub(token: String) -> bool:
	_hub = HubConnection.new()
	add_child(_hub)
	_hub.debug_mode(true)

	var fail_reason := ""
	_hub.error_occurred.connect(func(msg: String):
		fail_reason = msg
		_log("[color=orange]hub error: %s[/color]" % msg))

	_hub.connect_to_url(SERVER_WS_URL, token)

	var elapsed := 0.0
	while not _hub.is_connected_to_hub() and elapsed < 10.0:
		await get_tree().process_frame
		elapsed += get_process_delta_time()

	if not _hub.is_connected_to_hub():
		_log("[color=red]FAILED: hub did not connect within 10s. Last error: %s[/color]" % fail_reason)
		return false

	return true


func _log(text: String) -> void:
	print(text)
	if _output:
		_output.append_text(text + "\n")
