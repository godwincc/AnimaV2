extends RefCounted
class_name HandshakeProtocol

## Builds and validates the SignalR JSON Hub Protocol handshake.
## See: https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/HubProtocol.md

const RECORD_SEPARATOR := char(0x1e)


func get_handshake_message() -> String:
	var message := {"protocol": "json", "version": 1}
	return JSON.stringify(message) + RECORD_SEPARATOR


## A successful handshake response is an empty JSON object: [code]{}\x1E[/code].
## A failure response has an [code]error[/code] field. We accept the former
## and treat the latter as not-acknowledged (the error message is surfaced
## via the regular error channel).
func is_handshake_acknowledged(message: String) -> bool:
	var clean := message.strip_edges()
	if clean.ends_with(RECORD_SEPARATOR):
		clean = clean.substr(0, clean.length() - 1)

	var json := JSON.new()
	if json.parse(clean) != OK:
		return false

	var obj: Variant = json.get_data()
	if typeof(obj) != TYPE_DICTIONARY:
		return false

	return not obj.has("error")
