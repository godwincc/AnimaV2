extends Node

## Autoload singleton (see project.godot's [autoload] section). Carries just enough state to
## survive a change_scene_to_file() call between screens that need a parameter -- Godot has no
## built-in way to pass arguments to a freshly-loaded scene, and every other real screen in this
## client is parameter-less, so this is the first screen transition that needs one. Same "tiny,
## in-memory-only singleton" shape as AuthState, just for navigation instead of session data.

var selected_anima_id: String = ""
