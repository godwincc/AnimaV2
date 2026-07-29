extends RefCounted
class_name UiTheme

## Project-wide font-size baseline (NEW, readability pass) -- introduced after confirming no global
## Godot Theme resource or [gui]/default_theme project setting existed anywhere in this project (every
## screen was independently hardcoding its own add_theme_font_size_override literals, so the same
## "text too small" complaint was guaranteed to resurface screen by screen). Named tiers here are the
## single source of truth going forward; existing call sites are being migrated incrementally as each
## screen's readability is actually raised as an issue (Delve's Legend/Resources/Team-skill-chip labels
## this round), not rewritten in one blanket pass, since most of this codebase's existing sizes were
## deliberate (e.g. HP readouts, position numbers) and not all flagged as hard to read.
const SIZE_MICRO := 10     ## augment "+" badge, other decorative micro-labels
const SIZE_SMALL := 11     ## secondary/muted small text
const SIZE_BODY := 15      ## standard readable body text -- chip/legend/resource-label tier
const SIZE_LABEL := 16     ## slightly emphasized labels (primary readouts, e.g. resource counts)
const SIZE_SUBHEADER := 18
const SIZE_HEADER := 20
