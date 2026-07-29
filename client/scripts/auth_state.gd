extends Node

## Autoload singleton (see project.godot's [autoload] section). Holds the current session's JWT
## + account info in memory only -- no persistent storage yet, that's a later concern (see
## CLAUDE.md's Login / Create Account Screen section). Cleared back to empty on sign_out(), which
## nothing currently calls (no sign-out UI exists yet), but the method exists so login.gd doesn't
## need to reach into this singleton's fields directly to reset them.

var token: String = ""
var account_id: String = ""
var username: String = ""
var expires_at_utc: String = ""


func is_authenticated() -> bool:
	return token != ""


## Called by login.gd on a successful Login or Create Account response -- both flows return the
## same AuthResponse shape (AccountId, Username, Token, ExpiresAtUtc), so one setter covers both.
func set_session(p_account_id: String, p_username: String, p_token: String, p_expires_at_utc: String) -> void:
	account_id = p_account_id
	username = p_username
	token = p_token
	expires_at_utc = p_expires_at_utc


func sign_out() -> void:
	account_id = ""
	username = ""
	token = ""
	expires_at_utc = ""
