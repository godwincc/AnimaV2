class_name ApiClient
extends RefCounted

## Shared REST helper for talking to Anima.Server's Auth endpoints. Factored out of
## connectivity_test.gd's own _post_json (same request/response shape) so login.gd doesn't
## reinvent it -- connectivity_test.gd itself is left untouched, since it's a standing proven
## smoke test, not something to refactor mid-pass.

const SERVER_HTTP_BASE := "http://localhost:5143"


## Posts JSON to path (relative to SERVER_HTTP_BASE) and returns {"code": int, "body": Variant}.
## code == -1 means a connection-level failure (server unreachable, DNS, timeout, etc.), not a
## real HTTP status -- distinguished from actual HTTP error codes so callers can tell the two
## apart and word their error messages accordingly. `requester` is the Node the HTTPRequest child
## is attached to (needs a live tree to await the signal); callers pass `self` from a Control/Node.
static func post_json(requester: Node, path: String, payload: Dictionary) -> Dictionary:
	var http := HTTPRequest.new()
	requester.add_child(http)
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
