# Anima (AnimaV2) — Project Context for Claude Code

This file is auto-read by Claude Code at the start of every session.

## What This Project Is

A breeding roguelike deckbuilder ("Anima") inspired by Axie Infinity (breeding/parts) and Slay the Spire (roguelike run structure). Players breed creatures called **Anima** to obtain skill combinations, build a 3-Anima team, and run a node-based roguelike dungeon.

**Core design pillar:** reward SMART breeding, smart team composition, smart deckbuilding — not just raw stats.

**Current phase: MAJOR MILESTONE — ALL 4 COLORS ARE 100% CODED AND TESTED (12 Archetypes, 48 skills).** The combat engine (`Anima.Core`) is fully implemented, tested, and validated. The Godot client has not yet been started. Next planned steps: a "semi-run" progression test, mixed-Archetype testing, then Weaving/Run structure/Godot.

**Terminology:** "Primitive" is being reframed as "Archetype" conceptually (parts are randomly determined via Weaving, even "pure" Animas mix Primitives) but "Primitive" stays the internal/code term.

## Tech Stack & Architecture

C#/.NET 10 backend logic. Planned client: Godot (GDScript, not started). Planned server: ASP.NET Core/SignalR (not started). No database yet.

### Solution structure
```
AnimaV2.sln
src/
  Anima.Core/  <- PERMANENT, fully built and tested
    Enums/GameEnums.cs
    Models/ (Stats, Skill, Anima, StatusEffectInstance, Enemy, EnemyBehaviorRule, Artifact, ICombatant)
    Combat/ (CombatState, CombatEngine, SkillResolver)
    Data/ (SampleAnimas: Ember/Reaper/Marksman[Crimson], Boulder/Aegis/Warden[Onyx],
           Sprout/Thicket/Lotus[Verdant], Shade/Anchor/Veil[Azure], Bastion[hybrid test];
           SampleEnemies: Grovehide/Quillfang/Sentinel/LeechMother; SampleArtifacts: EmberCore)
  Anima.ConsoleHarness/  <- THROWAWAY test runner, plain text only
  Anima.Server/  <- NOT STARTED
tests/Anima.Core.Tests/  <- NOT STARTED
```

**Git status:** 35+ commits, fully committed and pushed to origin/main.

**IMPORTANT CORRECTION:** earlier docs described a `CombatEventBus` pub/sub system. **This does not exist.** Every "event" (on-hit, on-death, on-Shield-gain/break) is a **direct inline check at a known checkpoint** — e.g. Last Laugh checked at the `PurgeDeadAnimaCards` checkpoint; Retribution checked exactly where Shield hits 0 inside `ApplyDamage`. Works correctly, just don't call it an event bus.

**Other architecture notes:**
- `ICombatant` unifies `Anima`+`Enemy`. Enemies reuse the `Skill` class (`Part`/`Color` nullable).
- `EnemyBehaviorRule`: (Condition,Skill) pairs, top-down first-match, `AiState` dict, `IsDefensive` flag suppressed by Enrage.
- `GrantShield` shared helper (returns actual post-cap amount, cap=50) + `TriggerInspire` wrapper.
- `IsSkillUsableFrom` enforces positional restrictions (was a real gap, fixed).
- `IsFriendlyTargetType` distinguishes `ChosenAny`(ally-side) from `ChosenEnemy`(enemy-side) — watch for this, caused 2 bugs.
- `SkillCategory.Summon`/`Skill.SummonFactory` for mid-fight spawning.
- `TargetMoveOffset`(moves target) distinct from `MoveOffset`(moves actor).
- `ResolveBuff` resolves an actual chosen target (not just caster) — generalized after Safeguard exposed prior hardcoding.
- `BuffKeywords` array + `RemoveOneBuff` powers Purge (dispel).
- `GetEffectiveEnergyCost` handles Clarity's discount (simplifies to flat -1/use since only 1 action/Round exists).
- **Recurring gotcha:** root namespace "Anima" collides with the `Anima` class (CS0118). Inside a namespace block: move `using` above the declaration. At global/top-level scope (`Program.cs`): use a `using` alias instead (`using AnimaUnit = Anima.Core.Models.Anima;`) — reordering does NOT work there.

## Core Game Rules

### Colors
| Color | HP | Def | Speed | Dmg mult | Spirit mult |
|---|---|---|---|---|---|
| Crimson(DPS) | 100 | 7 | 10 | 1.3x | 0.7x |
| Onyx(Tank) | 130 | 13 | 7 | 1.0x | ~0.8x |
| Verdant(Healer) | 100 | 10 | 10 | 0.7x | 1.3x |
| Azure(Utility) | 70 | 10 | 13 | 1.0x | 1.0x |
| Vulcan(hybrid) | 143 | 10 | 6 | 1.3x | 0.7x |
| Mirage(hybrid) | 60 | 10 | 13 | 0.7x | 1.4x |

### Combat Math
- `Final Dmg = Base x DamageMult`. Modifier stacking: additive first, then multiplicative sequential; attacker's own mods first, opponent's(Weak) last.
- Shield absorbs first, additive, **capped at 50**. Defense: `max(raw-Defense,1)`.
- `Final Heal = Base x SpiritMult`, capped at MaxHp. DOTs bypass Defense+Shield.
- No randomness except the 27-card deck shuffle.
- Reactive counters (Retaliate/Thorns) respect crest multipliers (fix was needed).

### Positioning & Turns
3v3, positions 1(front)/2(mid)/3(back). Default targeting=front. Melee(1-2)/Ranged(2-3) enforced via `IsSkillUsableFrom`.
4-phase Round: RoundStart(energy+3,draw+3,tick Durations) -> Initiative(Speed desc, Providence-first verified, PvE Speed-ties=player-team-first-then-position) -> Action(resolve fully in order) -> RoundEnd(win/loss).

### Deck (fully verified)
27-card shared deck (3 parts x3 copies x3 Animas, Crests excluded). Hand7, draw+3/Round, cap10. Fisher-Yates shuffle. Dead combatants' cards purged immediately.

### Status Effects — CRITICAL PATTERN
**Any status affecting "target's next action" MUST use Until-Consumed, NEVER Fixed-turn:1.** Fixed-turn ticks at Round Start, before Action Phase — dies before a slower-applier's faster target ever acts. Found/fixed on Weak, Taunt, AND Retaliate/Thorns.

| Keyword | Effect | Duration |
|---|---|---|
| Shield | Absorbs dmg, additive, max50 | Until-consumed |
| Bleed | DOT, bypasses Def+Shield | Fixed-turn:3 |
| Weak | Reduces ALL effects | Until-consumed |
| Marked | Forces attacks onto target, single-slot/team, Taunt=self-Marked | Until-consumed |
| Retaliate/Thorns | Counter-dmg, respects crest mults | Until-consumed |
| Primed | Next atk x2dmg | Until-consumed |
| Frenzy | +20%Spd/+20%Dmg on consume | Until-consumed |
| Guarded | +8Def, single-hit | Until-consumed |
| Exposed | +25%incoming, single-hit | Until-consumed |

## ALL 4 COLORS — FULLY CODED

**CRIMSON:** P1["Ember"]:Slash(25dmg,2en)/Charge(move+Primed,1en)/Execute(any-pos,lowest-HP,40dmg,3en)/Reckless(+25%dmg HP<50%). P2["Reaper"]:Rend(22dmg+Bleed@8,2en)/Lunge(20dmg+move,2en)/Frenzy(+20%Spd/Dmg,2en)/Bloodthirst(heal25% dmg dealt). P3["Marksman"]:Snipe(22dmg,2en)/Retreat(move back+Guarded,1en)/Marked Shot(pos3-only,15dmg+Exposed,2en)/Steady Aim(+15%dmg pos3).

**ONYX:** P1["Boulder"]:Bash(13dmg+Weaken,2en)/Hardened(move+Shield18,1en)/Taunt(Marked-self,2en)/Courage(-20%dmg taken pos1,pre-Defense). P2["Aegis"]:Guard Strike(15dmg+Shield=100%dealt,2en)/Fortify(+32Shield,2en)/Shatter(dmg=Shield,removes it,3en)/Inspire(allies get30% ACTUAL Shield gained). P3["Warden"]:Intercept(pos2-only,move pos1+Retaliate20,2en)/Bristle(Thorns12,1en)/Disarm(stun,1en)/Vengeance(+25%dmg HP<50%,boosts counters).

**VERDANT:** P1["Sprout"]:Smite(10dmg+heal26,2en)/Guiding Light(move+heal20,1en)/Lifebloom(heal46,3en)/Soul Link(+25%heal HP>50%). P2["Thicket"]:Renew(HOT13x3,2en)/Healing Rain(AoE heal20,2en)/Bloom(8dmg+refresh HOT[no-op if none],1en)/Providence(always first, VERIFIED). P3["Lotus"]:Purge(8dmg+remove buff,2en)/Silence(7dmg+disable Frame,1en)/Cleanse(heal33[caster'sSpirit]+remove debuff,2en)/Clarity(-1energy first skill).

**AZURE:** P1["Shade"]:Pin(stun,1en)/Exploit(8dmg+Bleed@5,refreshes existing,1en)/Misdirect(applies Marked,2en)/Ambush(2xdmg acting LAST). P2["Anchor"]:Shove(15dmg+push,2en)/Enfeeble(10dmg+Weak,2en)/Hook(pull,no dmg,2en)/Last Laugh(on-death:refresh enemy debuffs). P3["Veil"]:Deflect(15dmg+self Shield12,2en)/Safeguard(move ally+Shield15,2en)/Ward(AllAllies Shield10,2en)/Retribution(on FULL Shield break only[confirmed],Weak to attacker).

**Disable family:** Onyx Disarm(Tail)->enemy Head; Verdant Silence(Frame)->enemy Frame; Azure Pin(Head)->enemy Tail. Full chain needs 3-color hybrid; Mirage=ideal chassis.

## Validated Test Results
- **Match length RESOLVED:** Sentinel HP140->105(now7-18rds). Leech Mother HP160->115+self-heal50%->30%(now mostly13-17rds).
- **Sentinel/starter-trio validated:** naked trio LOSES(rd21). With2 stacked augments+correct AI, trio WINS(rd17,1rd before Enrage).
- **BIGGEST FINDING — Leech Mother/redirect mechanics:** Spawn Brood's guard needed to spawn at **position1**(not2) to actually protect her. Once fixed, Misdirect proven decisively valuable for the first time(83/91 attacks redirected past guard). **KEY TAKEAWAY: redirect/Marked/Taunt are no-ops in single-target-boss fights w/ no targeting ambiguity — only matter with genuine ambiguity (e.g. a guard).**
- **Hybrid/cross-color-breeding-value still OPEN/UNVALIDATED** — Bastion vs Boulder tests too noisy at small samples.

## Enemy Roster
Grovehide(HP60/Def8/Spd~5,tested,2-7rds-good). Quillfang(HP40/Def4/Spd5,retreats<50%HP,tested,good). The Sentinel(Elite,HP105retuned/Def12/Spd8,Enrage@rd18=+75%dmg+stops defense,extensively tested). The Leech Mother(Elite,HP115retuned/Def10/Spd6,self-heal30%retuned,SpawnBrood@>=rd5 guard-at-pos1,extensively tested). Warden of the Hollow(Boss,HP220/Def14,2phases,NOT CODED).

## Known TODOs
Warden boss, Weaving system(0% coded), Run structure(0% coded), Godot client, ASP.NET Core/SignalR server, semi-run progression test(planned next), mixed-Archetype pure Anima test(planned next), Murkbind/standalone Rustling Swarm.

## Working Preferences
User=C# backend dev, no frontend exp, Claude does all coding. Prefers simple solutions but approves worthwhile refactors closing real gaps. Wants build confirmation after every change. Highly values honest bug/gap reporting — has praised catching real issues rather than silently patching. Appreciates regression tests after touching shared code. Calls Claude Code "CC" in the separate design conversation.

## Where Design Decisions Get Made
Full game design lives in a separate Claude.ai chat. This file is a snapshot. If you need details not summarized here (exact Weaving mechanics, Run structure specifics), ask the user to paste from `Anima_Design_Doc.md` rather than inventing values.
