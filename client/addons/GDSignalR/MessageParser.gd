extends RefCounted
class_name SignalRMessageParser

## Parses raw bytes from the WebSocket into individual SignalR Hub messages.
## Hub messages are framed by the 0x1E record separator.

const RECORD_SEPARATOR := char(0x1e)

enum MessageType {
	INVOCATION = 1,
	STREAM_ITEM = 2,
	COMPLETION = 3,
	STREAM_INVOCATION = 4,
	CANCEL_INVOCATION = 5,
	PING = 6,
	CLOSE = 7,
}


func parse_raw_messages(raw_data: String) -> Array:
	var messages: Array = []
	var parts := raw_data.split(RECORD_SEPARATOR, false)

	for part in parts:
		if part.strip_edges() == "":
			continue
		var json := JSON.new()
		if json.parse(part) == OK:
			messages.append(json.get_data())
		else:
			push_error("SignalR: failed to parse frame: " + part)

	return messages
