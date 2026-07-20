# Anima (AnimaV2) — Project Context for Claude Code

This file is auto-read by Claude Code at the start of every session.

## What This Project Is

A breeding roguelike deckbuilder ("Anima") inspired by Axie Infinity (breeding/parts) and Slay the Spire (roguelike run structure). Players breed creatures called **Anima** — framed as constructs/"Vessels," not literal animals — to obtain skill combinations, build a 3-Anima team, and run a node-based roguelike dungeon.

**Core design pillar:** reward SMART breeding, smart team composition, smart deckbuilding — not just raw stats.

**Current phase:** The entire backend loop is real and tested end-to-end. Full art direction is established. 6 of 7 core screens are fully locked (Hub, Weaving, Sanctum, Anima Profile, Delve, Collection). 3 of 4 room-encounter screens are locked (Resource, Treasure, Shop). **Only the Combat screen (the actual 3v3 fight UI) remains undesigned** — deliberately saved for last since it's the most complex. Godot client implementation is the confirmed next phase once screens are complete.

**Real pixel-art creature portraits are deferred** to a future outsourced pixel artist — Claude has no native raster/pixel image generation (architectural limitation of language models, not a subscription tier issue). Retrofitting real art into Godot later is confirmed easy: sprites are just texture references, so swapping a placeholder for a real PNG requires no structural changes.

## Tech Stack & Architecture

C#/.NET 10 backend, fully built and tested. Planned client: Godot (GDScript). Planned server: ASP.NET Core/SignalR (not started).

**IMPORTANT:** there is NO real `CombatEventBus`. Every "event" is a direct inline check at a known checkpoint.

**Recurring gotcha:** root namespace "Anima" collides with the `Anima` class (CS0118). Prefer `using AnimaUnit = Anima.Core.Models.Anima;` as the default fix everywhere — more reliable than moving `using` above the namespace.

### Solution structure
```
AnimaV2.sln
src/
  Anima.Core/  <- PERMANENT, fully built and tested (Models/Combat/Map/Reforge/Weaving/Economy/Data)
  Anima.ConsoleHarness/  <- THROWAWAY test runner + full Delve simulation
  Anima.Server/  <- NOT STARTED
tests/Anima.Core.Tests/  <- NOT STARTED
```

## Map Generation (LOCKED, VALIDATED — 500 seeded maps pass every rule)
7×15 grid + Boss. Fixed floors: 1=Combat, 9=Treasure, 15=Shop. Random odds: Combat35%/Elite15%/Resource15%/Treasure15%/Shop15%/Reforge5%. Real data: avg 22.84 Combat/map, avg 13.64 Treasure/map. **Node types (6):** Combat/Elite/Resource(Wisp-only, deliberate)/Treasure/Shop/Reforge/Boss. Elite optional/skippable.

## Reforge (LOCKED, VERIFIED)
Pay Wisp for ONE random part from ANY color. Accept(40/80Wisp)=swap+lose Augment; Decline=free. Run-only.

## Weaving System (LOCKED, VERIFIED — real Axie Infinity mechanics as direct inspiration)
Per part slot (ALL FOUR: Head/Frame/Tail/Crest, no exceptions): pool 6 candidate genes (D/R1/R2 × 2 parents), weighted 37.5%/9.375%/3.125% → winner becomes offspring's new Dominant. Two more rolls fill R1/R2. Mutation: 10%, R1/R2 only. Color: flat 50/50, 100% if same-color parents. Hybrid trigger: both parents pure + correct pairing = 33%.
Sibling restriction (copies Axie): only parent-offspring + full-sibling blocked. Weave Count: max 5, cost curve 50/100/175/275/400. Echo: twin-Vessel, 5% spontaneous or guaranteed via 5 Echo Shards.
**Naming:** players name their own Animas — mandatory prompt on Weave creation. Starter trio (Ember/Boulder/Sprout) keeps hardcoded names, no prompt.

## Resource Economy (LOCKED, VERIFIED)
Persistent: Wisp, Echo Shards, Vessel Shards. Run-only: all 12 Artifacts. **HP persistence across nodes = attrition** (confirmed). Reward flow: Resource=30Wisp+15%chance1Ember, Combat=50Wisp+1guaranteedEmber, Elite=120Wisp+1guaranteedEmber+25%chanceEach2nd/3rdEmber(max3)+25%chance1Shard, Boss=300Wisp+guaranteed1(50/50Echo/Vessel), noEmber.

**Ember is momentary, NOT persistent** — genuinely per-color but never banked. Each drop (Combat/Elite/Resource win, Marked Coin's roll, or a Shop Wares purchase) is resolved immediately, one at a time: **Augment now** (spend it via AugmentService on a same-color team skill) or **Convert to Wisp** (flat 15 Wisp, `EmberService.ConvertToWisp`). Shop Wares also sells 1 Ember outright for 25 Wisp (`EmberService.TryBuyEmber`, Ember Core discount applies) — bought Ember is spent immediately through the same Augment-page flow.

## All 12 Artifacts (LOCKED, VERIFIED, icons finalized)
Twin Flame, Wisp Charm (+20% Wisp), Barrier Stone (+5 Shield team/Round), Vanguard's Bell (+1 Energy Rd1), Weaver's Thread (+1 hand, 5→6), Marked Coin (random resource on pickup), Withering Fang (consumed any node, executes lowest-HP to 1HP if combat), Focusing Lens (every 4th Attack = 2x dmg), Silent Chime (extra action, single-use/Delve), Ember Core (20% Shop discount), Sapling Charm (heal 10% max HP any node, no revive), **Sifting Stone** (before each Round's top-up draw, discard any number of hand cards — the top-up replaces them along with whatever was played).

**Hard cap: 3 Artifacts held per Delve, no swap mechanic** (`ArtifactService.MaxArtifactsPerDelve`). Treasure at 3/3 = reward skipped/lost entirely, no substitute (intentional punish). Shop at 3/3 = the Wares Artifact slot doesn't roll at all that visit. Boss reward is unaffected (Wisp + Shard only, never in the Artifact pool).

## AugmentService (LOCKED, VERIFIED)
4 types: Increase Effect(+20%) / AoE Damage(50%) / Decrease Cost(-1, unclamped=refund) / Extend(+1 Charges). Max 3/part. Cost = 1 Ember (color must match the SKILL's own archetype color, e.g. Ember/Reaper/Marksman = Crimson) + a Wisp tier: 15/30/50 for the 1st/2nd/3rd Augment on that skill. Ember Core's 20% discount applies to the Wisp tier too.

**Eligibility is keyed on the skill's own archetype color, never the owning Anima's body Color** — a single Anima's 4 parts (Head/Frame/Tail/Crest) can each require a different Ember color if they came from mixed-color breeding (verified via Bastion: Onyx-bodied, but its Cleanse Tail is a Verdant part and only takes a Verdant Ember).

## Core Combat Rules
### Colors
| Color | HP | Def | Speed | Dmg mult | Spirit mult |
|---|---|---|---|---|---|
| Crimson(DPS) | 100 | 7 | 10 | 1.3x | 0.7x |
| Onyx(Tank) | 130 | 13 | 7 | 1.0x | ~0.8x |
| Verdant(Healer) | 100 | 10 | 10 | 0.7x | 1.3x |
| Azure(Utility) | 70 | 10 | 13 | 1.0x | 1.0x |

3v3, positions 1(front)/2(mid)/3(back). Any status affecting "target's next action" MUST use Until-Consumed, never Fixed-turn:1.

## Hand/Deck (LOCKED, VERIFIED — persistent hand + top-up draw)
Shared team deck (Head/Frame/Tail x3 copies each, Crests excluded). **Hand is persistent, never force-discarded** — unplayed cards stay in hand across Rounds. **Hand max = 5 (6 with Weaver's Thread)**; the opening hand AND every Round Start's draw both just top the hand back up to this number (draw count = hand max − cards currently held), replacing the old flat "+3 cards/Round" design. Played cards go to the discard pile; when the draw pile runs dry mid-draw, the discard pile reshuffles back into it and drawing continues (pre-existing behavior, confirmed still correct under the new model). Pairs with Energy banking (confirmed: +3/Round, capped at 9, `CombatEngine.RoundStartPhase` — locked, not a discrepancy) so a player can hold and wait on an expensive card.

**Sifting Stone** (12th Artifact) taps into this: before the top-up draw resolves each Round, discard any number of hand cards — the top-up then refills to hand max, replacing both played AND voluntarily-discarded cards.

## All 4 Colors — 48 skills, 12 Archetypes (fully coded & tested)
**CRIMSON:** Ember(Slash/Charge/Execute/Reckless), Reaper(Rend/Lunge/Frenzy/Bloodthirst), Marksman(Snipe/Retreat/Marked Shot/Steady Aim).
**ONYX:** Boulder(Bash/Hardened/Taunt/Courage), Aegis(Guard Strike/Fortify/Shatter/Inspire), Warden[sample, distinct from Boss](Intercept/Bristle/Disarm/Vengeance).
**VERDANT:** Sprout(Smite/Guiding Light/Lifebloom/Soul Link), Thicket(Renew/Healing Rain/Bloom/Providence), Lotus(Purge/Silence/Cleanse/Clarity).
**AZURE:** Shade(Pin/Exploit/Misdirect/Ambush), Anchor(Shove/Enfeeble/Hook/Last Laugh), Veil(Deflect/Safeguard/Ward/Retribution).

## Enemy Roster — ALL VALIDATED
Grovehide/Quillfang (Basic). The Sentinel (Elite DPS-check). The Leech Mother (Elite Sustain-check — proved redirect mechanics need genuine targeting ambiguity). **Warden of the Hollow** (Boss, HP220/Def11, FINAL — starter trio has no guard-bypass tool and can't win regardless of stats; ~40% win rate for un-bred teams is intentional).

## ART DIRECTION — Fully Established

**Creature art:** Animas are CONSTRUCTS ("Vessels") — Axie-modular aesthetic (visible seams) over Pokemon-style cohesive design. Deep portrait art is paused/deferred (see top of file); placeholders in use. Warden of the Hollow should visually reference Slay the Spire's Bronze Automaton.

**Tone:** pixel art, cozy+nostalgic but "a bit dark, like STS." **Moonlighter is a genuine structural reference** — both the Delve and Weaving loops were directly modeled on its day/night dual-loop structure.

**THE VISUAL THEME (all 6 core screens):** warm dark "sanctuary/workshop" — `radial-gradient(ellipse at 30% 20%, #4a3a2e 0%, #2b2018 45%, #1a130e 100%)` backdrop, semi-transparent dark cards (`rgba(30,22,16,0.75)`, border `rgba(201,184,158,0.2)`), warm cream text (`#e8cf9a`/`#f0e4d4`), amber accent (`#e8a03a`), horizontal amber-gradient stat bars.

**THE ANIMA LOGO** (also the unofficial game logo / the "enter a Delve" button): glowing amber magic-circle portal — concentric rings, rune-mark dots on the true diagonals, a central diamond gem (**also the symbol for the Crest skill type** — used instead of "star"), and four subtle sigils at the cardinal points, each in a unified dark medallion (deliberately NOT distinctly colored per-color), thin strokes (width 1, opacity 0.7):
- **North = Crimson:** simple inverted-V spike
- **East = Onyx:** open bracket-line, straight centered vertical middle
- **South = Verdant:** trunk + 2 branch offshoots
- **West = Azure:** two parallel curved waves

**Skill-type icon set:** sword=Attack, heart=Heal, shield=Shield-granting, bolt=other Buff, **diamond=passive Crest**.

**"Parts list" pattern** (every Anima card, every screen): portrait + name, then the 4 parts by name+icon, always visible — no hover/click needed.

**Wisp iconography rule (important, applies everywhere):** Wisp is an ethereal/magical resource (will-o'-the-wisp themed name) — NEVER use a coin/currency icon for it, use sparkles/a glowing orb instead.

### The 6 core screens (ALL fully designed, ALL warm-themed)
1. **Hub** — resource bar top. 3 destination cards (Sanctum/Weaving/Collection). Team (parts-list cards) left + mini Anima-logo portal as Delve button right. "Last delve" summary at bottom.
2. **Weaving** — two **"Strand"** slots (parent-selection term). Wide cards, portrait-left/details-right: name+color+Gen+Weave Count, parts list, and Hidden Threads for ALL 4 parts (including Crest) always visible. Reagent slot (Echo Shards) + Weave Cost readout, mini Weave portal button, result panel at bottom.
3. **Sanctum** — grid of Anima cards (portrait, name, color, Gen, parts list, Weave Count progress bar). Active-team members get an amber border + "In team" badge. Cards link to Profile.
4. **Anima Profile** — portrait (rename icon), color/Gen/Weave Count, parts list, "Threads" section (Dominant per part, dot-accent) with a **"Show hidden" toggle** (matches Axie's real hidden-gene-by-default precedent), a **"Lineage" section** (Parents/Siblings/Echo Twin — all clickable links to that Anima's own Profile), Delve History.
5. **Delve screen** — map is LARGE/primary focus, horizontal (2 distinct starting nodes per the real algorithm, Boss far right). Real icons: sword=Combat, skull=Elite(bigger), coin=Resource, building-store=Shop, gift=Treasure, hammer=Reforge, crown=Boss(biggest). **Shape encodes risk: Combat/Elite/Boss = circles (sized by threat), safe types (Shop/Resource/Treasure) = diamond outlines.** Scroll+pinch-zoom. Team (parts-list) left, resources+Artifacts (show as X/3, the hard cap) grouped right below the map.
6. **Collection** — top: persistent resource summary with counts+descriptions. Below: "Artifacts (X of 12 discovered)" vertical list — unlocked shows icon+name+description+"Delves won with: X"; locked shows a dim silhouette ("Undiscovered").

### Room-encounter screens (3 of 4 locked, differentiated backgrounds per type)
- **Resource** (LOCKED) — golden radial bg, glowing sparkle/wisp-orb centerpiece, "A quiet cache" flavor text, +30 Wisp shown prominently, single "Collect" button.
- **Treasure** (LOCKED) — richer purple/magenta gem-tone bg, "A forgotten chest" flavor text, reveals the actual Artifact offered in a highlighted card, single "Claim" button.
- **Shop** (LOCKED) — warm amber bg (closest to Hub's tone), "A weathered stall" flavor text, **only 2 sections: Rest and Wares** — there is NO standalone "Augment a skill" menu; Augmenting only ever triggers from an Ember actually in hand (a node drop, or a Wares Ember purchase below), never from browsing a list. **Rest**: heal 40% max HP for Wisp. **Wares**: fresh independent stock rolled every Shop visit (no shared/depleting pool across multiple Shops in one Delve) — 3 Ember for sale (25 Wisp each, random color per slot, independent rolls, duplicates allowed) + 1 Artifact for sale (random from the 12, excluding any the player currently holds; slot doesn't appear at all if the player is at the 3-Artifact cap). Buying an Ember immediately opens the shared Augment-page flow (same screen a node-dropped Ember uses) scoped to that slot's color. "Leave" link to exit without buying.
  - **PENDING (design not yet built): the shared Augment-page flow itself** — the picker screen a player lands on after choosing "Augment now" on any Ember (node drop or Wares purchase). Needs to show all 3 team Animas as horizontal cards, each listing all 4 skills (grouped/filtered to the Ember's own color) with current Augments + an "apply" action, reusable identically from every entry point.
- **Combat/Elite/Boss rooms** — NOT YET DESIGNED. Planned: darker/tenser background with a subtle red undertone for Combat/Elite, darkest/most dramatic for Boss.

### The 12 Artifact Icons (finalized)
Twin Flame=flame, Wisp Charm=sparkles, Barrier Stone=shield, Vanguard's Bell=custom clean bell outline (no clapper), Weaver's Thread=custom 3 slanted diagonal lines, Marked Coin=custom coin outline + sparkle-star inside, Withering Fang=custom sharp pointed tooth shape, Focusing Lens=custom magnifying glass (no plus sign), Silent Chime=asterisk (placeholder), Ember Core=sun, Sapling Charm=leaf, **Sifting Stone=ti-recycle (stock Tabler icon, no custom shape)**.

### NOT yet designed: Combat screen (the actual 3v3 fight UI — the last major screen)

## Known TODOs
1. **Design the shared Augment-page flow** — the picker reached from "Augment now" on any Ember (node drop or Wares purchase): all 3 team Animas as horizontal cards, 4 skills each (filtered to the Ember's color) + current Augments + an "apply" action. Not Shop-specific — same screen from every entry point.
2. **Design the Combat screen** — biggest remaining piece, deliberately saved for last.
3. **Design Combat/Elite/Boss room backgrounds** once the Combat screen itself is done.
4. Godot client and ASP.NET Core/SignalR server: not started.
5. Cross-color hybrid PvE combat value: correctly deprioritized (PvP-relevant, out of scope).
6. No partial-death/revival concept — a wipe just ends the Delve. Wisp reward amounts are first-pass, need tuning — including the new Wares Artifact price (200 Wisp, picked, not specified anywhere).

## Working Preferences
- User is a C# backend dev, no frontend experience — Claude does all coding, user handles deployment.
- Trusts Claude's design/implementation calls once discussed — flesh out design in chat first, then give a ready-to-send CC prompt (no need to ask "want me to send this").
- Highly values honest reporting of bugs/gaps/inconclusive results, and honest correction when Claude makes a mistake (e.g. a wrong icon choice).
- Calls Claude Code "CC" in the separate design conversation where all game design decisions get made.
