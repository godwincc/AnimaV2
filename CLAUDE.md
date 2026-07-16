# Anima (AnimaV2) — Project Context for Claude Code

This file is auto-read by Claude Code at the start of every session. It summarizes the game design and technical decisions made so far so you don't need re-explaining every session.

## What This Project Is

A breeding roguelike deckbuilder ("Anima") inspired by Axie Infinity (breeding/parts) and Slay the Spire (roguelike run structure). Players breed creatures called **Anima** to obtain skill combinations, build a 3-Anima team, and run a node-based roguelike dungeon.

**Target platform:** web-based, PvP support planned. 2D cozy pixel art style (Stardew Valley aesthetic), node-based map (no free-roam walking).

**Current phase:** building a Phase 1 combat prototype — proving the core combat rules work via a console harness, before building the Godot client or ASP.NET Core/SignalR backend.

## Tech Stack & Architecture

- **Backend/logic:** C#, .NET 10 (LTS)
- **Planned client:** Godot (GDScript — Godot 4's C# support cannot export to web, so the client is GDScript, but all real game logic lives in C#)
- **Planned server:** ASP.NET Core + SignalR (for PvP sync), added later
- **Database:** none yet for Phase 1 (in-memory only). PostgreSQL + EF Core planned for Phase 2+ persistence.

### Solution structure
```
AnimaV2.sln
src/
  Anima.Core/            ← REAL game logic (permanent, not throwaway)
    Enums/GameEnums.cs
    Models/ (Stats, Skill, Anima, StatusEffectInstance, Enemy, Artifact)
    Combat/ (CombatState, CombatEngine, SkillResolver, CombatEventBus)
    Data/ (SampleEnemies, SampleArtifacts — reference/sample data, not final content)
  Anima.ConsoleHarness/  ← THROWAWAY test runner, plain text output only, references Anima.Core
  Anima.Server/          ← added later: ASP.NET Core/SignalR backend, references Anima.Core
tests/
  Anima.Core.Tests/      ← added later: unit tests
```

**Key architectural decisions:**
- `Anima.Core` is the permanent foundation — prototype work goes here, not into disposable scaffolding.
- `Anima.ConsoleHarness` is genuinely disposable — deleted once Godot/Server exist. Plain text output only, no graphics/ASCII art — formatted as clear step-by-step logs (damage math shown explicitly) since it doubles as a debugging tool.
- **Event-bus architecture** (`CombatEventBus`) handles reactive triggers (OnHit, OnDamageTaken, OnShieldGained, OnShieldBroken, OnDeath, OnRoundStart, OnRoundEnd) — used by Crests, enemy special behaviors, AND artifacts (general-purpose, not scoped to just one of these). Chosen for extensibility as more content is added, instead of hardcoded per-name checks.
- Exceptions to the event bus: **Providence** (turn-order override) is a direct check in `InitiativePhase`, not an event subscription, since it's turn-order logic not a reaction.
- **Enemies reuse the `Skill` class** for their abilities (same targeting/damage/range system as player skills) — no separate enemy-ability format. `Skill.Part` and `Skill.Color` are nullable since they're only meaningful for player Anima skills (used by the Weaving/breeding system).
- **Enemy AI** uses `EnemyBehaviorRule` — a list of `(Condition, Skill)` pairs checked top-down, first match wins. Free-form `AiState` dictionary on `Enemy` for state (charge-up flags, phase tracking) rather than hardcoded fields per enemy.
- **Artifact** model kept intentionally simple (`OnCombatStart` hook for now) — left open for expansion. Combat-scoped artifacts (Ember Core, Withering Fang, etc.) hook into the event bus; run-scoped artifacts (Wisp Charm, Weaver's Thread) will need a separate hook once the Run layer exists.

## Core Game Rules

### Colors (base stats — Damage/Spirit are multipliers, others are flat values)
| Color | Role | HP | Def | Speed | Damage mult | Spirit mult |
|---|---|---|---|---|---|---|
| Crimson | DPS | 100 | 7 | 10 | 1.3x | 0.7x |
| Onyx | Tank | 130 | 13 | 7 | 1.0x | ~0.8x |
| Verdant | Healer | 100 | 10 | 10 | 0.7x | 1.3x |
| Azure | Utility/Specialist | 70 | 10 | 13 | 1.0x | 1.0x |
| Vulcan (hybrid: Onyx+Crimson) | — | 143 | 10 | 6 | 1.3x | 0.7x |
| Mirage (hybrid: Verdant+Azure) | — | 60 | 10 | 13 | 0.7x | 1.4x |

Every base color has exactly one deliberately-low stat. Hybrids trade a weak stat for two boosted signature stats (see full design doc for reasoning).

### Parts
Each Anima has 4 parts: **Head** (offense default), **Frame** (mobility default), **Tail** (buff default), **Crest** (passive, always active — NOT in the drawable deck). Parts are NOT strictly locked to their default role — any part can hold any skill category.

### Combat Math
- `Final Damage = Skill.BaseDamage × Attacker.DamageMultiplier`
- Shield absorbs first (flat, `Magnitude` reduces 1:1, stacks **additively** when reapplied)
- Then Defense reduces remainder: `max(rawDamage - Defense, 1)` if rawDamage > 0
- `Final Heal = Skill.BaseHeal × Caster.SpiritMultiplier`, capped at MaxHp
- **DOTs (Bleed) bypass both Defense and Shield entirely** — applied directly via status tick, not through `ApplyDamage`
- **No randomness in combat** (no crit, no dodge/miss) — fully deterministic. The one exception is the 27-card deck draw (shuffled), which is an explicit, scoped exception to this rule.

### Positioning
- 3v3, single-lane, positions 1 (front) / 2 (mid) / 3 (back)
- Default targeting: attacks hit enemy position 1 unless the skill says otherwise
- **Attack Range:** only two keywords — Melee (usable from pos 1-2) and Ranged (usable from pos 2-3). Narrower per-skill exceptions (e.g. a skill only usable from pos 3) are handled in that skill's own logic, not new range categories.

### Energy & Turn Structure (4-phase Round)
- Shared team energy pool: 3/Round base, rolls over unused, cap ~6-9 (untuned)
- **Round Start:** energy +3 (capped), draw +3 cards, tick all Durations, reset per-Round flags (e.g. Clarity's discount)
- **Initiative:** sort by Speed descending; Providence-holders act first regardless of Speed; ties resolved by roll (not yet implemented)
- **Action:** each living combatant acts once, in Initiative order, regardless of team; skill fully resolves (CCG/TCG rule) before the next actor's turn
- **Round End:** check win/loss (all 3 Animas/Enemies on a side at 0 HP = that side loses)

### Deck & Draw (an explicit exception to "no randomness in combat")
- 3 active parts (Head/Frame/Tail) × 3 copies × 3 Animas = **27-card shared team deck**. Crests excluded (always active, not drawn).
- Opening hand: 7. Draw +3 per Round. Hand cap: 10.
- Adopted from Axie Infinity's card/deck model, scaled to Anima's smaller part count.

### Status Effects (all use `StatusEffectInstance`, keyed by `Keyword`)
| Keyword | Effect | Duration |
|---|---|---|
| Shield | Absorbs damage before HP, separate resource from Defense | Until-consumed |
| Bleed | DOT, bypasses Defense+Shield | 3 turns |
| Weak | Reduces ALL of target's effects (damage AND healing) | 1 turn |
| Marked | Forces ally attacks onto this target, bypasses Taunt | 1 turn |
| Retaliate | Counter-damage when hit by melee | 1 turn |
| Thorns | Passive reflect on melee hits (distinct from Retaliate) | 1 turn |
| Primed | Next attack deals double damage, dispellable | Until-consumed |
| Taunt | Forces all enemy attacks onto this Anima | 1 turn |

General rule: buffs/debuffs last 1 turn (or until a trigger); HOTs/DOTs last 3 turns.

## Full Skill Sets

All 4 colors are fully designed (48 skills total, 3 "Primitives"/build-variants per color). Full tables live in the project's design doc (ask the user for `Anima_Design_Doc.md` and `Anima_Skills.xlsx` if you need the complete reference — not duplicated here to keep this file manageable). Quick summary of what's implemented so far in code:

**Crimson Primitive 1 (Burst Combo) — currently the only kit wired into the console harness test:**
| Part | Skill | Effect | Energy |
|---|---|---|---|
| Head | Slash | Melee, targets front, 25 base dmg | 2 |
| Frame | Charge | Move + applies Primed (next attack ×2 dmg) | 1 |
| Tail | Execute | Any position, targets lowest-HP enemy, 40 base dmg | 3 |
| Crest | Reckless | +25% dmg when HP < 50% (passive-conditional, not yet wired to event bus) | 0 |

**Enemy example (Quillfang, in `Data/SampleEnemies.cs`):** HP 40, Def 4, Needle Volley (Ranged, 14 dmg, targets front), retreats to position 3 below 50% HP.

**Artifact example (Ember Core, in `Data/SampleArtifacts.cs`):** `OnCombatStart` grants +1 energy.

## Known TODOs / Not Yet Implemented
- Move effect resolution (self-move vs. target-move vs. push/pull) in `SkillResolver`
- `ApplyStatus` for Bleed/Weak/Marked/Primed application
- `TriggerReactiveEffects` (Retaliate/Thorns counter-damage)
- `TickStatusDurations` (duration countdown + DOT damage application)
- `DrawCards` (currently the console harness test hardcodes a starting hand instead of using the real 27-card deck)
- Speed-tie resolution in Initiative Phase (currently falls back to stable sort)
- Most Crests beyond Providence/Clarity aren't wired to the event bus yet
- Reckless's conditional (+25% dmg below 50% HP) not yet implemented

## Working Preferences
- User is a C# backend dev with no frontend experience — comfortable with C#, needs more guidance on tooling/frontend-adjacent things.
- Prefers simple, working solutions now with room to extend later, over building unused generic infrastructure upfront.
- Wants build confirmation after every change.
- Refers to Claude Code as "CC" in conversation with the user's other Claude (claude.ai chat) sessions where game design happens.

## Where Design Decisions Get Made
Full game design (all skills, Weaving/breeding system, Run structure, artifacts, enemies) is worked out in a separate Claude.ai chat conversation, not here. This file is a snapshot of what's been decided and implemented as of the last update. If something seems inconsistent or you need a design decision that isn't covered here, ask the user rather than assuming — they're relaying context from that design conversation.

## If You Need Details Not in This File
This file intentionally does NOT include the full 48-skill tables, the complete Weaving system, or the full Run structure — those live in `Anima_Design_Doc.md` and `Anima_Skills.xlsx`, produced in the separate design conversation. If you need details on a skill, color, or system not summarized above (e.g., you're about to implement Onyx, Verdant, or Azure's kits, or need exact Weaving mechanics), **ask the user to paste the relevant section from those files** rather than guessing or inventing values — this project has very specific, deliberately-designed numbers and mechanics, and an invented substitute will likely conflict with decisions made elsewhere.
