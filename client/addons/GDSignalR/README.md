# GDSignalR

A lightweight **ASP.NET Core SignalR** client for **Godot 4.3+**, written in pure GDScript.

Connects to a SignalR Hub over a WebSocket using the official [JSON Hub Protocol](https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/HubProtocol.md). No native modules, no third-party DLLs — works on every Godot export target that supports `WebSocketPeer` (desktop, mobile, web).

## Features

- ✅ JSON Hub Protocol over WebSocket (skips negotiate; works against any ASP.NET Core hub that allows WebSockets, which is the default).
- ✅ Fire-and-forget `send(method, args)`
- ✅ Awaited `invoke(method, args)` — gets the server's return value via the Completion (type 3) message.
- ✅ Server-pushed callbacks via `on(method_name, callable)`
- ✅ Bearer-token auth (passed as `access_token` query param — ASP.NET Core's convention for WebSocket auth)
- ✅ Custom HTTP headers on the upgrade request
- ✅ Automatic reconnect with configurable retry policy

## Installation

### From the AssetLib (recommended once published)
In Godot: *AssetLib* tab → search **GDSignalR** → *Download* → enable in *Project Settings → Plugins*.

### Manually
1. Copy the `addons/GDSignalR/` folder into your project's `addons/` directory.
2. In *Project Settings → Plugins*, enable **GDSignalR**.

That's it. The plugin registers the global classes `HubConnection`, `SignalRClient`, `WebSocketTransport`, `HandshakeProtocol`, and `SignalRMessageParser` — you can use them from any script.

## Quick start

```gdscript
extends Node

var hub: HubConnection

func _ready() -> void:
    hub = HubConnection.new()
    add_child(hub)

    # Server-pushed message handler
    hub.on("ReceiveMessage", func(user: String, text: String):
        print("[%s] %s" % [user, text]))

    hub.connected.connect(func(): print("Connected!"))
    hub.error_occurred.connect(func(msg): push_warning(msg))

    hub.connect_to_url("ws://localhost:5000/chatHub")
    await hub.connected

    # Fire-and-forget
    hub.send("SendMessage", ["Godot", "Hello from the engine!"])

    # Request/response — server method returning Task<T>
    var token: Variant = await hub.invoke("Login", ["alice", "pw"])
    if token == null:
        # error_occurred has already fired with the reason
        return
    print("Got token: ", token)
```

## API reference

### `HubConnection`

The main class you'll interact with.

| Member | Description |
|---|---|
| `connect_to_url(url, auth_token := "", headers := {})` | Connect to a hub. URL must use `ws://` or `wss://`. `auth_token` is appended as `?access_token=...`. |
| `stop()` | Closes the connection and disables auto-reconnect. |
| `is_connected_to_hub() -> bool` | True once the SignalR handshake has been confirmed. |
| `send(method, args := [])` | Fire-and-forget invocation. |
| `invoke(method, args := [], timeout_seconds := 30.0) -> Variant` | Sends an invocation and awaits the server's Completion message. Returns the server's return value, or `null` if the call failed/timed out (in which case `error_occurred` is emitted). |
| `on(method_name, callable)` | Register a handler for a server-pushed method. The callable receives the same positional arguments the server sent. |
| `off(method_name)` | Remove a handler. |
| `set_retry(enabled, interval := 3.0, max_attempts := 5)` | Configure the auto-reconnect behavior. |
| `debug_mode(status)` | Toggle verbose logging. |

#### Signals

| Signal | Args | When |
|---|---|---|
| `connected` | — | SignalR handshake confirmed. |
| `disconnected` | — | WebSocket closed (auto-reconnect may follow). |
| `error_occurred` | `(message: String)` | Any error: transport, server-side, or failed invocation. |
| `reconnect_failed` | — | All retry attempts exhausted. |

### Lower-level classes

Use these only if you need to bypass `HubConnection`:

- **`SignalRClient`** — handles handshake + parsing + the underlying transport. Emits `message_received(method, args)` and `completion_received(id, result, error)`.
- **`WebSocketTransport`** — `WebSocketPeer` wrapper.
- **`HandshakeProtocol`** — builds and validates the handshake frame.
- **`SignalRMessageParser`** — splits raw bytes on the `0x1E` record separator and JSON-parses each frame.

## Authentication

ASP.NET Core SignalR follows this convention for token-based auth over WebSocket: the JWT goes in the `access_token` **query string**, not in an `Authorization` header (browsers can't set headers on a WS upgrade). GDSignalR follows the same convention:

```gdscript
hub.connect_to_url("wss://example.com/secureHub", my_jwt_token)
```

On the server side, configure your JWT bearer middleware to read the query string for hub paths — see the `login-register` example for a working template.

If you need to send other arbitrary headers (e.g. on a custom transport), pass them as a third argument:

```gdscript
hub.connect_to_url(url, "", { "X-Client-Version": "1.0.0" })
```

## Reconnection

By default, GDSignalR retries up to 5 times at 3-second intervals after any disconnect or error. Configure with:

```gdscript
hub.set_retry(true, 5.0, 10)   # 5s interval, 10 attempts
hub.set_retry(false)            # disable
```

Auth tokens and headers are preserved across retries.

## Limitations

This is a minimal client. It supports the message types you need for a typical game: **Invocation (1)**, **Completion (3)**, **Ping (6)**, **Close (7)**.

Not implemented:
- **Streaming hub methods** (types 2, 4, 5) — server `IAsyncEnumerable<T>` / client streams.
- **MessagePack** transport — JSON only.
- **Transport negotiation** — connects directly via WebSocket, which is supported by every modern ASP.NET Core SignalR deployment but means you can't fall back to Server-Sent Events or Long Polling.

PRs welcome.

## Server requirements

Any ASP.NET Core 3.1+ SignalR hub will work. The included `examples/` folder ships full server projects for **.NET 8** covering the three common patterns:

- `examples/chat-room/` — broadcast + history
- `examples/guess-word/` — turn-based game with structured responses
- `examples/login-register/` — JWT auth

## License

MIT. See [LICENSE](LICENSE).
