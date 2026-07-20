# Anima (AnimaV2) — Project Context for Claude Code

This file is auto-read by Claude Code at the start of every session.

## What This Project Is

A breeding roguelike deckbuilder ("Anima") inspired by Axie Infinity (breeding/parts) and Slay the Spire (roguelike run structure). Players breed creatures called **Anima** — framed as constructs/"Vessels," not literal animals — to obtain skill combinations, build a 3-Anima team, and run a node-based roguelike dungeon.

**Core design pillar:** reward SMART breeding, smart team composition, smart deckbuilding — not just raw stats.

**Current phase:** The entire backend loop is real and tested end-to-end (140/140 checks passing as of this session). Full art direction is established. All 6 core screens are fully locked (Hub, Weaving, Sanctum, Anima Profile, Delve, Collection). All 4 room-encounter screens are locked (Resource, Treasure, Shop). **The Combat screen is fully designed.** **The match result screen (Victory/Defeat/Retreat) and the new Anima Reveal screen are now also fully designed and backed by real implemented services this session** — see Match Result & Retreat System and Anima Reveal Screen below. Godot client implementation is the confirmed next phase; no more screen-design gates remain before it, though a Run/traversal-state model (see Known TODOs) is still needed to wire result screens to real data.

**Real pixel-art creature portraits are deferred** to a future outsourced pixel artist — Claude has no native raster/pixel image generation (architectural limitation of language models, not a subscription tier issue). Retrofitting real art into Godot later is confirmed easy: sprites are just texture references, so swapping a placeholder for a real PNG requires no structural changes.

## Tech Stack & Architecture

C#/.NET 10 backend, fully built and tested. Planned client: Godot (GDScript). Planned server: ASP.NET Core/SignalR (not started).

**IMPORTANT:** there is NO real `CombatEventBus`. Every "event" is a direct inline check at a known checkpoint.

**Recurring gotcha:** root namespace "Anima" collides with the `Anima` class (CS0118). Prefer `using AnimaUnit = Anima.Core.Models.Anima;` as the default fix everywhere — more reliable than moving `using` above the namespace.

**Recurring gotcha:** `DelveRun.Team` and a Combat node's `CombatState.PlayerTeam` must be the **same `List<Anima>` reference**, never a rebuilt/cloned list — HP attrition across nodes works today only because `CurrentHp` mutations happen in place and propagate for free through shared object references. Any future Combat-node wiring (Godot client, Server project) that constructs `CombatState.PlayerTeam` from a copy of `DelveRun.Team` will silently break attrition. Same caution applies to `CurrentArtifacts`/`WispEarnedSoFar` on `DelveRun`, which are live passthroughs to `RunLedger.Artifacts`/`PersistentLedger` rather than separate copies — do not fork these into independent state that must be kept in sync by hand.

### Solution structure
```
AnimaV2.sln
src/
  Anima.Core/  <- PERMANENT, fully built and tested (Models/Combat/Map/Reforge/Weaving/Economy/Data)
  Anima.ConsoleHarness/  <- THROWAWAY test runner + full Delve simulation (de facto test suite; tests/Anima.Core.Tests does not exist yet)
  Anima.Server/  <- NOT STARTED
tests/Anima.Core.Tests/  <- NOT STARTED
```

## Map Generation (LOCKED, VALIDATED — 500 seeded maps pass every rule)
7×15 grid + Boss. Fixed floors: 1=Combat, 9=Treasure, 15=Shop. **Random odds, Floors 6+ (REBALANCED, prior session): Combat 36%/Elite 16%/Resource 16%/Treasure 16%/Shop 16%/Reforge 0%.** Reforge stays a fully real, coded node type (targeting flow, `RollOffer`/`Accept`, tests) — this is a probability change only, not a removal; it simply won't currently generate on any map. **Confirmed safe:** a 0%-weight entry only works because Reforge is the last entry in the odds array (the weighted-roll loop always resolves to an earlier entry first, since `Random.NextDouble()` never returns 1.0) — this invariant is documented directly in `MapGenerator`'s code so a future reorder doesn't silently break it.

**Guaranteed Elite + Early-Game Elite Exclusion (LOCKED THIS SESSION, STS-inspired):**
1. **Floors 1-5: Elite excluded entirely.** Uses a separate `EarlyFloorTypeOdds` table (Combat 40%/Resource 20%/Treasure 20%/Shop 20%, Elite/Reforge omitted, not zero-weighted) instead of the Floor-6+ table. **Shop's 20% entry is intentionally left inert (LOCKED, confirmed deliberate — not a bug):** Shop is *also* independently banned on Floors 1-5 by the pre-existing `EliteShopMinFloorIndex` rule (a joint Elite+Shop floor gate from an earlier session, untouched by this one), so that 20% never actually manifests on a real map — the real, observable Floors-1-5 distribution is Combat/Resource/Treasure only, renormalized among just those three. Same precedent as Reforge's 0% entry in the main `TypeOdds` table: correct-on-paper dead weight is kept rather than removed, because it costs nothing and documents the "fair" redistribution intent even where it's practically moot.
2. **Floors 6-14: guaranteed ≥1 Elite.** Before the normal weighted roll runs anywhere, one random eligible node is force-assigned Elite directly (bypassing the odds table entirely). This is a floor, not a cap — the normal roll still includes Elite at 16% for every other node afterward, so a map can have more than 1 Elite, never fewer. **Flagged deviation from the literal brief:** eligibility is Floor 6-13, *not* Floor 6-14 as specified — Floor 14 had to be excluded because every Floor 14 node connects directly into Floor 15 (entirely fixed Shop), and Elite-Shop direct adjacency is already banned by the existing `NoAdjacentGroup` rule (the same rule that already makes Floor 14 unable to roll Elite under the normal weighted path today). Forcing Elite onto Floor 14 would have produced a guaranteed, unavoidable rule violation. **Sanity-checked against the Reforge-0%-safety invariant (asked for explicitly):** no interaction — the forced assignment is a direct `.Type =` write on one node, never touching `TypeOdds`, `allowed`, or the weighted-roll loop; it can only ever produce Elite, never Reforge, and doesn't change either odds array's order or contents.

Real re-validated data at the final odds (n=500 seeds, permanent batch validator built into the harness — 0 rule violations, 0 Elite occurrences on Floors 1-5, every map has ≥1 Elite, all reconfirmed not assumed): **avg/map — Combat 23.00, Elite 4.93, Resource 10.48, Treasure 14.37, Shop 7.70, Reforge 0.00, Boss 1.00.**

**Node types (6):** Combat/Elite/Resource(Wisp-only, deliberate)/Treasure/Shop/Reforge/Boss. Elite optional/skippable (still true from Floor 6 on — the guarantee is a minimum count, not a forced route through it).

## Reforge (LOCKED, VERIFIED — targeting flow + screen identity added this session)
Pay Wisp for ONE random part from ANY color. Accept(40/80Wisp)=swap+lose Augment; Decline=free. Run-only (swap lasts only for the current Delve).

**Targeting flow (NEW THIS SESSION — mechanically essential, not optional, since Accept previously had no defined target to apply the swap to):**
1. Enter Reforge, one random part is rolled (random color + random skill within that color) and revealed.
2. Player picks **which of their 3 Anima**, then **which of that Anima's slots** (Head/Frame/Tail/Crest — Crest included, even though it's excluded from the deck, since Reforge swaps the actual Thread and Crest still has a real passive Thread to overwrite) to socket it into.
3. Accept commits the swap into that slot for the rest of this Delve only; Decline is free, nothing changes.
**Open question, not yet resolved:** what differentiates the 40 vs 80 Wisp cost tiers — needs a decision before this is fully spec-complete.

**Screen identity (NEW THIS SESSION — nice-to-have, deferred): white/pale visual theme** — distinct from Treasure's purple-magenta and Shop's amber, chosen because Reforge alters an existing Thread rather than granting a new resource. Not yet mocked up; same priority tier as the Elite/Boss Combat-room background palettes.

## Weaving System (LOCKED, VERIFIED — real Axie Infinity mechanics as direct inspiration)
Per part slot (ALL FOUR: Head/Frame/Tail/Crest, no exceptions): pool 6 candidate genes (D/R1/R2 × 2 parents), weighted 37.5%/9.375%/3.125% → winner becomes offspring's new Dominant. Two more rolls fill R1/R2. Mutation: 10%, R1/R2 only. Color: flat 50/50, 100% if same-color parents. Hybrid trigger: both parents pure + correct pairing = 33%.
Sibling restriction (copies Axie): only parent-offspring + full-sibling blocked. Weave Count: max 5, cost curve 50/100/175/275/400. Echo: twin-Vessel, 5% spontaneous or guaranteed via 5 Echo Shards.
**Naming:** players name their own Animas — mandatory prompt on Weave creation. Starter trio (Ember/Boulder/Sprout) keeps hardcoded names, no prompt.

## Resource Economy (UPDATED THIS SESSION)
**Persistent:** Wisp, Echo Shards, Vessel Shards. **Ember is NO LONGER persistent** — see Ember & Augment System below.
Run-only: all 12 Artifacts (see below), capped at 3 held simultaneously (see Artifact Cap below). **HP persistence across nodes = attrition** (confirmed).
Reward flow: Resource=30Wisp/no guaranteed Ember (15% chance of 1 bonus Ember), Combat=50Wisp + 1 guaranteed Ember (pickup-choice, see below), Elite=120Wisp + 1 guaranteed Ember with 25% chance each for a 2nd and 3rd (max 3) + **25% chance 1 Vessel Shard (Vessel Shard only, no Echo — REWORKED THIS SESSION)**, Boss=300Wisp + **guaranteed hatched Anima (via Anima Reveal screen, REWORKED THIS SESSION — see Boss Reward & Anima Reveal Screen below) + guaranteed Echo Shard (no longer a 50/50 — Vessel Shard is off the table for Boss now that its old "Vessel" resource grant is retired)**, no Artifact reward at Boss (never was in the Artifact pool).

## Ember & Augment System (REWORKED THIS SESSION — replaces prior persistent-bank model)
Ember is no longer a stored/banked resource. It is a momentary "chance to augment," resolved individually at pickup or purchase.

**Drops:**
- Combat: 1 Ember, random color, guaranteed.
- Elite: 1 Ember guaranteed + independent 25% chance each for a 2nd and 3rd Ember (max 3).
- Resource: 15% chance of 1 bonus Ember, additive to the base 30 Wisp.

**Pickup flow (per Ember, sequential if multiple dropped — never batched):**
- **Augment now** — opens the Augment page scoped to that Ember's color. Player picks a skill whose own archetype color matches the Ember's color (see Augment Eligibility below — this is skill color, not Anima body color). Consumes the Ember, charges the Wisp tier for that skill's next augment slot.
- **Convert to Wisp** — flat 15 Wisp, Ember discarded, no augment.

**Augment cost** (replaces the old 2/4/7 Ember curve entirely): 1 Ember (consumed) + a Wisp tier based on how many augments that specific skill already has — 1st augment: 15 Wisp, 2nd: 30 Wisp, 3rd: 50 Wisp. Max 3 augments/skill unchanged. The 4 augment types (Increase Effect +20%, AoE Damage 50%, Decrease Cost -1, Extend +1 Charges) unchanged.

**Augment eligibility (REWORKED):** checked against the skill's OWN archetype color (Crimson: Ember/Reaper/Marksman, Onyx: Boulder/Aegis/Warden, Verdant: Sprout/Thicket/Lotus, Azure: Shade/Anchor/Veil), NOT the Anima's body color. This means a single Anima's 4 skills (Head/Frame/Tail/Crest) can each require a different Ember color if bred from mixed-color parts (hybrids Vulcan/Mirage, or any atypical part lineage). This is the correct general rule, not a hybrid-specific patch — it retires an earlier "either parent color works for hybrids" proposal that's no longer needed.

## Artifact Cap (NEW THIS SESSION)
Hard cap of **3 held Artifacts per Delve**, no swap mechanic. Treasure node: if at 3/3 when an Artifact reward triggers, the reward is skipped/lost — no substitute (intentional punish for a wasted node). Boss: unaffected, was never in the Artifact pool. Shop: if at 3/3, the Artifact-for-sale slot doesn't roll that visit.

## All 12 Artifacts (LOCKED, icons finalized — Sifting Stone added this session)
Twin Flame, Wisp Charm (+20% Wisp), Barrier Stone (+5 Shield team/Round), Vanguard's Bell (+1 Energy Rd1), Weaver's Thread (+1 hand, so hand max becomes 6), Marked Coin (random resource on pickup), Withering Fang (consumed any node, executes lowest-HP to 1HP if combat), Focusing Lens (every 4th Attack = 2x dmg), Silent Chime (extra action, single-use/Delve), Ember Core (20% Shop discount), Sapling Charm (heal 10% max HP any node, no revive), **Sifting Stone (NEW — at Round end, discard any number of cards from hand before the top-up draw; icon: ti-recycle, no custom shape needed)**.

## AugmentService (cost model reworked — see Ember & Augment System above)
4 augment types: Increase Effect(+20%) / AoE Damage(50%) / Decrease Cost(-1, unclamped=refund) / Extend(+1 Charges). Max 3/part. Cost is now 1 Ember + Wisp tier (15/30/50), NOT the old flat 2/4/7 Ember-only curve.

## Hand/Deck System (REWORKED THIS SESSION)
- **Deck composition (CLARIFIED THIS SESSION — resolves a prior undocumented gap): 27-card deck = 3 active parts × 3 cards each, per Anima × 3 Anima on the team.** Only Head/Frame/Tail contribute cards — **Crest is passive and contributes zero cards to the deck** (consistent with Crest already being the sole diamond-icon/passive skill type). The deck is fully auto-derived from team selection; there is no separate player-facing deckbuilding screen, and none is needed.
- Hand max: **5 (base), 6 with Weaver's Thread**.
- Hand is persistent — unplayed cards remain in hand at Round end (this was already true in the prior implementation; confirmed, not newly added).
- Round Start draw is a **top-up**, not a full reshuffle: draw count = hand max − cards currently held. This replaced a flat "+3 cards/Round" draw.
- Played cards go to discard. Draw pile reshuffles from discard when empty (already existed, confirmed working under the new model).
- Sifting Stone (see Artifacts) lets the player additionally discard any number of held cards at Round Start before the top-up resolves.

## Energy System (CONFIRMED THIS SESSION — no change made)
+3 Energy at the start of each Round, capped at **9** (not 6 — an earlier in-session assumption of a 6 cap was corrected against the actual tested implementation and intentionally not changed). Vanguard's Bell grants an additional +1 on Round 1 specifically. No other Energy sources currently exist. Energy gates which hand cards are playable; insufficient Energy blocks a card.

## Core Combat Rules
### Colors
| Color | HP | Def | Speed | Dmg mult | Spirit mult |
|---|---|---|---|---|---|
| Crimson(DPS) | 100 | 7 | 10 | 1.3x | 0.7x |
| Onyx(Tank) | 130 | 13 | 7 | 1.0x | ~0.8x |
| Verdant(Healer) | 100 | 10 | 10 | 0.7x | 1.3x |
| Azure(Utility) | 70 | 10 | 13 | 1.0x | 1.0x |

3v3, positions 1(front)/2(mid)/3(back) per side, teams face each other horizontally (player left, enemy right — Axie/STS/Darkest Dungeon-inspired layout). PvE only currently designed; PvP (if ever built) would likely use random placement, deprioritized/not blocking. Any status affecting "target's next action" MUST use Until-Consumed, never Fixed-turn:1.

### Turn Resolution (LOCKED THIS SESSION — Darkest Dungeon-style, not STS batch-commit)
Turn order for the Round is rolled once at Round start (all 6 combatants), shown as a queue. Combatants act **one at a time in that rolled order**, interleaving player and enemy turns — NOT a "queue all your actions, then watch the round resolve" STS-style batch. This was a deliberate choice: the skill kit has heavy reactive/interrupt design (Intercept, Guard Strike, Deflect, Ward, Retribution, Vengeance, Last Laugh) that only makes sense if the player can see what already happened this Round before acting. Enemy intent telegraphing (STS-style "show what the enemy will do before they do it") was explicitly REMOVED given one-by-one resolution — the player sees the real action play out live instead.

Player can **Pass** a given Anima's turn (opts out of playing anything that turn) via an explicit Pass action.

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

**THE VISUAL THEME (all core screens):** warm dark "sanctuary/workshop" — `radial-gradient(ellipse at 30% 20%, #4a3a2e 0%, #2b2018 45%, #1a130e 100%)` backdrop, semi-transparent dark cards (`rgba(30,22,16,0.75)`, border `rgba(201,184,158,0.2)`), warm cream text (`#e8cf9a`/`#f0e4d4`), amber accent (`#e8a03a`), horizontal amber-gradient stat bars.

**THE ANIMA LOGO** (also the unofficial game logo / the "enter a Delve" button): glowing amber magic-circle portal — concentric rings, rune-mark dots on the true diagonals, a central diamond gem (**also the symbol for the Crest skill type** — used instead of "star"), and four subtle sigils at the cardinal points, each in a unified dark medallion (deliberately NOT distinctly colored per-color), thin strokes (width 1, opacity 0.7):
- **North = Crimson:** simple inverted-V spike
- **East = Onyx:** open bracket-line, straight centered vertical middle
- **South = Verdant:** trunk + 2 branch offshoots
- **West = Azure:** two parallel curved waves

**Skill-type icon set:** sword=Attack, heart=Heal, shield=Shield-granting, bolt=other Buff, **diamond=passive Crest**.

**"Parts list" pattern** (every Anima card, every screen): portrait + name, then the 4 parts by name+icon, always visible — no hover/click needed.

**Wisp iconography rule (important, applies everywhere):** Wisp is an ethereal/magical resource (will-o'-the-wisp themed name) — NEVER use a coin/currency icon for it, use sparkles/a glowing orb instead.

### The 6 core screens (ALL fully designed, ALL warm-themed) — unchanged this session
1. **Hub** — resource bar top. 3 destination cards (Sanctum/Weaving/Collection). Team (parts-list cards) left + mini Anima-logo portal as Delve button right. "Last delve" summary at bottom.
2. **Weaving** — two **"Strand"** slots (parent-selection term). Wide cards, portrait-left/details-right: name+color+Gen+Weave Count, parts list, and Hidden Threads for ALL 4 parts (including Crest) always visible. Reagent slot (Echo Shards) + Weave Cost readout, mini Weave portal button, result panel at bottom.
3. **Sanctum** — grid of Anima cards (portrait, name, color, Gen, parts list, Weave Count progress bar). Active-team members get an amber border + "In team" badge. Cards link to Profile.
4. **Anima Profile** — portrait (rename icon), color/Gen/Weave Count, parts list, "Threads" section (Dominant per part, dot-accent) with a **"Show hidden" toggle** (matches Axie's real hidden-gene-by-default precedent), a **"Lineage" section** (Parents/Siblings/Echo Twin — all clickable links to that Anima's own Profile), Delve History.
5. **Delve screen** — map is LARGE/primary focus, horizontal (2 distinct starting nodes per the real algorithm, Boss far right). Real icons: sword=Combat, skull=Elite(bigger), coin=Resource, building-store=Shop, gift=Treasure, hammer=Reforge, crown=Boss(biggest). **Shape encodes risk: Combat/Elite/Boss = circles (sized by threat), safe types (Shop/Resource/Treasure) = diamond outlines.** Scroll+pinch-zoom. Team (parts-list) left, resources+Artifacts grouped right below the map.
6. **Collection** — top: persistent resource summary with counts+descriptions. Below: "Artifacts (X of 12 discovered)" vertical list — unlocked shows icon+name+description+"Delves won with: X"; locked shows a dim silhouette ("Undiscovered"). Note: count updated 11→12 for Sifting Stone.

### Room-encounter screens — mechanics locked for all 5 node types; visual identity locked for 3, deferred for 2
- **Resource** (LOCKED) — golden radial bg, glowing sparkle/wisp-orb centerpiece, "A quiet cache" flavor text, +30 Wisp shown prominently (+15% chance of 1 bonus Ember, new this session), single "Collect" button.
- **Treasure** (LOCKED) — richer purple/magenta gem-tone bg, "A forgotten chest" flavor text, reveals the actual Artifact offered in a highlighted card (skipped/lost if player at 3/3 Artifact cap), single "Claim" button.
- **Shop** (LOCKED, REVISED THIS SESSION) — warm amber bg (closest to Hub's tone), "A weathered stall" flavor text, now only 2 sections: **Rest** (heal 40% max HP for Wisp) and **Wares**. The standalone "Augment a skill" section was REMOVED — there is no browsable Augment UI at the Shop; Augmenting only ever triggers from acquiring an Ember (combat/elite/resource pickup, or a Wares purchase below). **Wares** sells 3 Ember (random color per slot, duplicates allowed) + 1 Artifact (random from the 12, excluding currently-held, skipped if at 3/3 cap) — each Shop node rolls its own independent stock on entry, no shared pool across multiple Shops in a Delve. Buying an Ember immediately opens the same Augment-page pickup flow used for combat drops. "Leave" link to exit without buying.
- **Reforge** (targeting flow LOCKED this session, screen identity DEFERRED — see Reforge section above) — mechanically complete (roll → pick Anima+slot → Accept/Decline), visual treatment (white/pale bg) proposed but not mocked up.
- **Combat/Elite/Boss rooms** — Combat screen mechanics/layout DESIGNED (see Combat Screen Design below), and **all 3 room BACKGROUND palettes are now implemented and mocked up (LOCKED THIS SESSION)**, background-only swap, everything else on the screen identical across all three: Normal = warm outer gradient (`#4a3a2e`/`#2b2018`/`#1a130e`) + `rgba(0,0,0,0.42)` arena inset; Elite = `radial-gradient(ellipse at 30% 20%, #5a2e3a 0%, #341f2b 45%, #180d12 100%)` + `rgba(20,0,10,0.48)` arena inset; Boss = `radial-gradient(ellipse at 30% 20%, #6b1a1a 0%, #2b0d0d 45%, #0a0505 100%)` + `rgba(0,0,0,0.6)` arena inset + `box-shadow: inset 0 0 120px rgba(139,0,0,0.35)` crimson vignette on the arena panel.

### The 12 Artifact Icons (finalized, Sifting Stone added this session)
Twin Flame=flame, Wisp Charm=sparkles, Barrier Stone=shield, Vanguard's Bell=custom clean bell outline (no clapper), Weaver's Thread=custom 3 slanted diagonal lines, Marked Coin=custom coin outline + sparkle-star inside, Withering Fang=custom sharp pointed tooth shape (NOT a stock Tabler icon — `ti-tooth` does not exist, must be a custom SVG shape), Focusing Lens=custom magnifying glass (no plus sign), Silent Chime=asterisk (placeholder), Ember Core=sun, Sapling Charm=leaf, **Sifting Stone=ti-recycle (stock Tabler icon, no custom shape needed)**.

## Combat Screen Design (NEW THIS SESSION — fully designed, not yet implemented in Godot)

**Layout, top to bottom:**
1. HUD row: Round counter (also shows node type, e.g. "Round 3 — Combat"), Wisp count (sparkle icon, never a coin), Artifacts row (icons, hover for description — see tooltip pattern below).
2. Divider line.
3. **Arena**: darker inset panel (`rgba(0,0,0,0.42)` over the base gradient) clearly separated from the rest of the HUD — this was an explicit request to make the arena "pop" visually. Player team left, enemy team right, no "VS" divider text. Each side's 3 Anima/enemies arranged front-to-back with front nearest the center gap (position 1=front closest to center, 2=mid, 3=back, matching the existing position table).
4. Each combatant card: portrait, name, HP bar (color-coded: green >50%, amber/gold 25–50%, red ≤25%), HP numbers, a buff/debuff icon row (FIXED HEIGHT reserved always, even when empty, so...), a **position-number line (1/2/3) that sits at the same vertical height for every combatant regardless of buff count** — this required reserving fixed height for the buff row rather than letting it collapse when empty. Dead/removed combatants show as an empty slot at the same total height (position held, portrait gone), with a darkened line and dim position number — no dashed placeholder box.
5. The currently-active combatant's position line is highlighted gold, tied to the turn order queue's highlighted entry.
6. Divider, then a **message area** (NOT a floating tooltip, NOT a top banner) — a single docked line of text used for THREE things: (a) buff/debuff and Artifact hover descriptions, (b) the "choose a target" instruction when a targeted card is selected, (c) the Confirm/Cancel prompt before a card resolves. This consolidation was deliberate — floating tooltips/popups positioned near edge-of-screen elements (e.g. the rightmost enemy, or the Artifacts row in the top-right corner) kept overflowing the viewport with center- or edge-anchored CSS; a single area that's part of normal document flow cannot overflow by construction. Hovering something temporarily overrides the message area's content and reverts to whatever the "current state" message was (targeting instruction, confirm prompt, or blank) on mouse-leave.
7. Divider, then: turn order queue (LEFT column, vertical list of full names — not initials — showing the FULL Round's rolled order, gold left-border-accent for player-side entries, coal-gray (`#8a8175`, matching enemy portrait color) for enemy-side, current/next actor highlighted) alongside Energy pips + Deck/Discard counts + Pass button (all in one row) + the hand of cards.
8. Hand cards: vertical card layout, cost pip (circle, color-coded to the skill's archetype color), placeholder art block, skill name, effect text. Unaffordable cards (insufficient Energy) are dimmed with a red X overlay and not clickable. Hand max is 5 (6 with Weaver's Thread) — deck/discard counts sit inline with Energy (not as a side column next to the hand) specifically to leave room for a 6th card without crowding.
9. Combat log: bottom of screen, scrollable (deliberate exception to the general "no nested scrolling" UI convention used elsewhere — a combat log needs full history, not just the last few lines), logs ALL combat events including AI/enemy turns and system events (Round starts, Enrage escalating), color-coded by event type (damage/heal/shield/neutral).

**Targeting/confirm flow:**
- Clicking a hand card: if self-targeting, goes straight to the Confirm prompt (in the message area) using the acting Anima as the implicit target. If it needs an enemy/ally target, outlines ONLY the legal target set on the arena (not all combatants) and shows a "choose a target" instruction in the message area.
- Clicking a legal target opens the Confirm prompt in the message area: "Play [skill] on [target]? Confirm / Cancel." Nothing resolves until Confirm is pressed. Cancel fully backs out of targeting mode. This confirm step was explicitly requested as a misclick-prevention measure.
- On Confirm: a floating +/- number rises and fades above the target's portrait, plus a brief colored flash on the portrait (red=damage, green=heal, silver-blue=shield). IMPORTANT implementation note: in the real client this feedback must fire only after the server/combat engine confirms the action's actual resolved result (including anything a reactive skill like Deflect/Vengeance changes), not optimistically on click — the mockup fires it optimistically only because there's no real backend behind it.

**Enrage display:** shown as a buff/debuff icon (flame) in the affected combatant's own status row, NOT a separate standalone HUD badge — this matters because Enrage is per-combatant (typically Boss/Elite), not a screen-wide state.

**Hover tooltips (buffs/debuffs, Artifacts):** originally floating positioned popups, retired in favor of the docked message area (see point 6 above) specifically because floating tooltips centered on edge-of-screen elements overflowed the viewport and a per-element edge-detection fix (right-align near the right edge, left-align near the left edge) proved fragile in testing. The docked message area is the intended real pattern — worth reusing anywhere else in the UI that has this same edge-tooltip-overflow risk (Collection's Artifact list, Sanctum cards, etc.).

**Formerly deferred here, now resolved (stale cross-reference cleaned up this session):** the win/loss transition and Elite Ember-pickup placement questions that used to be listed as "not yet designed" were both resolved by the Match Result & Retreat System section below — see there instead.

## Match Result & Retreat System (NEW THIS SESSION — fully designed AND implemented)

**Victory Screen — two tiers, one shared component, gated by `isBoss`:**
- **Combat/Elite (compact, fast to proceed):** small header ("Enemy Defeated"/"Elite Defeated"), Wisp reward, Ember pickup sequence (only if ≥1 dropped, reuses existing EmberService/AugmentService flow as-is), Vessel Shard pop-in (Elite only, 25%), Continue → back to Delve map, node cleared. **No team HP strip on this tier** — HP is already visible on the map screen itself.
- **Boss (full ceremony):** big header ("Warden of the Hollow Defeated"), team status strip (post-combat HP, same color coding as Combat screen), Wisp reward, Ember sequence if any, guaranteed Echo Shard, **guaranteed hatched Anima via the Anima Reveal screen** (see below). Continue → **Delve Complete** state: same component + appended summary block (floors reached, Anima used, total Wisp earned) → Return to Hub.

**Defeat/Wipe Screen:** header "Delve Ended" (not "You Died" — cozy-but-dark tone, not punishing). **Wisp penalty (LOCKED): keep 50% of Wisp earned *this run only*** — Wisp already banked from prior runs is untouched. Echo/Vessel Shards always kept in full, no penalty ever. Artifacts (run-only) are lost. Summary shows the math explicitly ("Wisp earned this run: 340 → kept: 170"), plus floor/node reached and nodes cleared. Single "Return to Hub" button, no retry-from-here (matches "no partial-death/revival").

**Retreat (mid-run voluntary exit, NEW):** button on the Delve map screen, visible whenever standing on the map between nodes (NOT available mid-combat/inside a node). Confirm modal (misclick protection, same pattern as Combat's Confirm/Cancel): "Retreat from this Delve? You'll keep your Wisp and Shards, but lose any held Artifacts. Retreat / Cancel." **Penalty (LOCKED): 0% Wisp penalty — keep 100% of this-run Wisp.** Artifacts still lost (run-only). This is deliberately better than a wipe (50% keep) so Retreat reads as a genuine "cash out" decision, not a soft-fail. Result screen reuses Defeat's layout, header changed to "Delve Retreated."

**Backend implemented:** `RunLedger.WispAtDelveStart` (snapshot at Delve start, so end-of-run math never touches pre-run banked Wisp) + `DelveEndService.ResolveDefeat`/`ResolveRetreat` (50%-keep and 100%-keep resolution), both returning a shared `DelveEndResult` shape for the one result-screen component.

## Boss Reward & Anima Reveal Screen (NEW THIS SESSION — fully designed AND implemented)

**Boss reward, final structure:** guaranteed Wisp (300) + guaranteed hatched Anima (resolves via Anima Reveal screen, not a banked resource — the old `ResourceType.Vessel` concept is fully retired, removed from the codebase entirely) + guaranteed Echo Shard.

**Boss-hatch Anima roll logic (no lineage/parents involved), implemented in `BossHatchService.Roll`:**
1. Roll body color, flat 25% each, Crimson/Onyx/Verdant/Azure. **No hybrids** — Vulcan/Mirage excluded entirely.
2. Roll each of the 4 parts' color independently, weighted **55% toward the Step 1 body color, 15% each for the other 3.**
3. Roll each part's Dominant, R1, and R2 as three independent 55/15/15/15-weighted rolls (via `SkillPool.RollRandom(Part, AnimaColor, Random)`), then pick the actual skill from that rolled color's pool. This makes Boss-hatch genomes **structurally identical to Weave-produced ones** — `IsFullyPure`, `WeavingService`, and materialization all work with zero special-casing (tested: `IsFullyPure` lands at the expected 0.55⁴ ≈ 9.15%).
4. Gen is fixed at **Gen 1** (wild origin point, same tier as starter trio conceptually, but goes through the normal mandatory naming prompt — starter trio remains the sole hardcoded-name exception).

**Anima Reveal Screen — shared component, two triggers:** (a) Boss Victory, when the guaranteed Anima resolves, (b) Weaving screen, on Weave completion. Layout: portrait reveal → Name/Color/Gen line → Threads section (Dominant/R1/R2 per part with the same "Show hidden" toggle default as Profile — **uniform display for both triggers, no Boss-hatch special case**, since Boss-hatch genomes are now structurally complete) → mandatory naming prompt (reuses Weave-creation naming pattern) → Confirm/Continue. Confirm commits the name, adds the Anima to the Sanctum roster, and returns to the triggering flow (Boss Victory → Delve Complete; Weaving → normal screen state).

**Backend implemented:**
- `Models.Anima` gains required `Name` (mutable, distinct from Id — `DisplayName` now returns `Name`) and `Gen` (int) fields. All 13 `SampleAnimas` factories updated (Name = Id, Gen = 1).
- `AnimaMaterializationService` (new, shared by both triggers): Weave overload computes `Gen = max(parentA.Gen, parentB.Gen) + 1` and records lineage; Boss-hatch overload fixes `Gen = 1`, no parents, looks up Stats via `WeavingService.ColorStats`. Both mint a fresh Guid Id and add the result to `SanctumRoster`.
- `SanctumRoster` (new) — the actual owned-Anima storage; this was a real confirmed gap, nothing tracked owned Animas anywhere before this session. Minimal `List<Anima>` + `FindById`, in-memory only (same no-save/load caveat as `PersistentLedger`).

**Future economy note:** Boss's guaranteed-Anima drop is currently a placeholder economy sink for the pre-trading era. If a future trading/marketplace system (Axie/Pokémon-style player-to-player exchange) makes Animas abundant, revisit disabling or reducing the guaranteed Boss drop so Weaving/trading remain the primary Anima sources rather than Boss-farming. See "Open Design Threads" in Anima_Design_Doc.md for the full list of deferred/open items.

## DelveRun — Run/Traversal State Model (NEW THIS SESSION — implemented)

`Anima.Core.Run.DelveRun` is the missing glue between node visits — the "save file for a single in-progress run." Scope was deliberately kept minimal (in-memory only, no save/resume, no mid-combat state):

- **`CurrentNode`/`AvailableNodes`/`TryMoveTo`** — map position on the 7×15+Boss grid. Boss surfaces automatically as an available node with no special-casing needed.
- **`ClearedNodes`** — caller-driven (not automatic on move), so the map can render visited vs. available.
- **`Team`** — a plain `List<Anima>`, the same list a Combat node's `CombatState.PlayerTeam` is built from. HP attrition needed zero separate tracking as a result — `CurrentHp` mutations persist for free through the shared reference, **as long as nothing ever rebuilds/clones this list** (see gotcha above).
- **`CurrentArtifacts`/`WispEarnedSoFar`** — live passthroughs to `RunLedger.Artifacts`/`PersistentLedger`, not separate copies, to avoid forking state that has to be hand-synced.
- **`DelveRun.Start(...)`** — the one real call site that sets `RunLedger.WispAtDelveStart` from the current `PersistentLedger` balance. **Bugfix note:** `WispAtDelveStart` existed since the Match Result & Retreat session but nothing outside test code ever actually set it — every real Delve would have diffed Wisp-earned against a stale 0 before this fix.

**Explicitly out of scope for this pass (by design, not oversight):** no mid-combat state (an active Combat node owns its own state; `DelveRun` just receives the result on resolution), no save/resume across app restarts (matches `SanctumRoster`/`PersistentLedger`'s existing no-save/load caveat), no multiplayer/concurrency handling (single active `DelveRun` at a time).

## Known TODOs
1. ~~No Run/traversal-state model exists anywhere~~ — RESOLVED: `DelveRun` implemented, unblocking real data for Defeat/Retreat/Delve-Complete summary screens.
2. ~~Design Combat/Elite/Boss room BACKGROUND palettes~~ — RESOLVED this session: all 3 implemented and mocked up (see Room-encounter screens above).
3. Godot client and ASP.NET Core/SignalR server: not started. **Accounts/Auth/Server-persistence is decided as the confirmed next priority (browser-only via Godot Web export, username+password login, email required for password-reset only) — prompt drafted but deliberately not yet sent to CC; was queued behind the Elite mechanic change, which is now resolved (see item 11), so this is unblocked whenever CC is ready to send it.** `SanctumRoster`/`PersistentLedger`/`DelveRun` will need to move from in-memory-only to real per-account database persistence once this starts.
4. Cross-color hybrid PvE combat value: correctly deprioritized (PvP-relevant, out of scope).
5. No partial-death/revival concept — a wipe just ends the Delve. Wisp reward amounts are first-pass, need tuning.
6. Boss's guaranteed-Anima drop is a placeholder economy sink — revisit if a future trading/marketplace system makes Animas abundant (see Boss Reward & Anima Reveal Screen above).
7. ~~Finish the Shop room's Augment section~~ — RESOLVED: the standalone Augment section was removed entirely rather than finished; Augmenting now only happens via Ember pickup/purchase flow.
8. ~~Energy cap discrepancy~~ — RESOLVED: confirmed +3/Round capped at 9 is correct as originally implemented, no change needed.
9. ~~Design the match result screen~~ — RESOLVED: Victory (compact/Boss tiers), Defeat (50% Wisp keep), Retreat (100% Wisp keep), and Anima Reveal screen all designed and implemented.
10. **`ReforgeService.Accept` only implements Head/Frame/Tail and throws on Crest**, despite the locked design (see Reforge section above) treating all 4 slots as valid Reforge targets. Real discrepancy, flagged by CC, not yet fixed. **Deliberately deprioritized alongside the rest of Reforge's open items (Wisp-tier question, screen visual identity)** since Reforge is at 0% map odds and currently unreachable in normal play — revisit together whenever Reforge's odds are reintroduced.
11. ~~Guaranteed-minimum-1-Elite-per-Delve + STS-inspired floor-1–5 Elite exclusion~~ — RESOLVED this session: both rules implemented in `MapGenerator`, re-validated against a fresh 500-seed batch (0 violations), new real averages captured (see Map Generation above). One deviation from the literal brief was required (Floor 14 excluded from Elite-guarantee eligibility, adjacency-rule conflict) — see that section for why.

## Working Preferences
- User is a C# backend dev, no frontend experience — Claude does all coding, user handles deployment.
- Trusts Claude's design/implementation calls once discussed — flesh out design in chat first, then give a ready-to-send CC prompt (no need to ask "want me to send this").
- Highly values honest reporting of bugs/gaps/inconclusive results, and honest correction when Claude makes a mistake (e.g. a wrong icon choice, or a discrepancy between an assumption and actual implemented behavior).
- Calls Claude Code "CC" in the separate design conversation where all game design decisions get made.
- Values token/effort economy — willing to skip mocking up every variant (e.g. Elite/Boss palettes) when a verbal description is sufficient to confirm direction.
