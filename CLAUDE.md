# Anima (AnimaV2) — Project Context for Claude Code

This file is auto-read by Claude Code at the start of every session.

## What This Project Is

A breeding roguelike deckbuilder ("Anima") inspired by Axie Infinity (breeding/parts) and Slay the Spire (roguelike run structure). Players breed creatures called **Anima** to obtain skill combinations, build a 3-Anima team, and run a node-based roguelike dungeon.

**Core design pillar:** reward SMART breeding, smart team composition, smart deckbuilding — not just raw stats. Actively validated through combat testing (especially the Boss — see below).

**Current phase:** MASSIVE PROGRESS THIS SESSION. All 4 colors (48 skills) fully coded and tested. Both Elites and the Boss (Warden of the Hollow) fully validated and locked. Real Slay-the-Spire-style branching map generation implemented and validated (500 seeded maps pass every rule). A new "Reforge" node mechanic implemented. The full Weaving (breeding) system — including the core genetics algorithm, Sibling restriction, Weave Count, Echo (twin-Vessel), and a persistent resource economy (Wisp/Echo Shard/Vessel Shard ledger) — is implemented and verified. **Godot client not yet started** — next major step per the user's own stated priority.

**Terminology:** "Primitive" is being reframed as "Archetype" conceptually (parts are randomly Weaving-determined even for "pure" Animas) but "Primitive" stays the internal/code term (like Run vs. Delve).

## Tech Stack & Architecture

C#/.NET 10 backend. Planned client: Godot (GDScript, not started). Planned server: ASP.NET Core/SignalR (not started).

### Solution structure
```
AnimaV2.sln
src/
  Anima.Core/  <- PERMANENT, fully built and tested
    Enums/GameEnums.cs
    Models/ (Stats, Skill, Anima [now has ParentAId/ParentBId/WeaveCount], StatusEffectInstance, Enemy, EnemyBehaviorRule, Artifact, ICombatant)
    Combat/ (CombatState, CombatEngine, SkillResolver)
    Map/ (MapNodeType, MapNode, DungeonMap, MapGenerator) — real STS-style branching map generation
    Reforge/ (ReforgeCandidate, ReforgePartPool, ReforgeOffer, ReforgeService)
    Weaving/ (GeneSource, PartGenome, PartResolution, AnimaGenome, WeavingResult, SkillPool, GenomeFactory, WeavingService, WeaveRejectionReason, WeaveAttemptResult)
    Economy/ (ResourceType, PersistentLedger, RunLedger)
    Data/ (SampleAnimas: Ember/Reaper/Marksman[Crimson], Boulder/Aegis/Warden[Onyx],
           Sprout/Thicket/Lotus[Verdant], Shade/Anchor/Veil[Azure], Bastion[hybrid test];
           SampleEnemies: Grovehide/Quillfang/Sentinel/LeechMother/WardenOfTheHollow;
           PrimitiveRoster.cs — shared 12-Primitive list used by both Weaving and Reforge)
  Anima.ConsoleHarness/  <- THROWAWAY test runner, plain text only, now also does ASCII map printing/validation
  Anima.Server/  <- NOT STARTED
tests/Anima.Core.Tests/  <- NOT STARTED
```

**Git status:** 40+ commits. Map generation, Reforge, and Weaving's core algorithm have each been committed as separate logical commits per the established pattern. **Check current status — Sibling restriction/Weave Count/Echo and the resource-ledger Economy work may still be pending commit as of this file's last update.**

**IMPORTANT CORRECTION:** there is NO real `CombatEventBus` pub/sub system. Every "event" (on-hit, on-death, on-Shield-gain/break) is a **direct inline check** at a known checkpoint (e.g. Last Laugh at the `PurgeDeadAnimaCards` checkpoint, Retribution where Shield hits 0 inside `ApplyDamage`).

**Key architecture notes (combat engine):**
- `ICombatant` unifies `Anima`+`Enemy`. `IsSkillUsableFrom` enforces positional restrictions. `IsFriendlyTargetType` distinguishes `ChosenAny`(ally-side) from `ChosenEnemy`(enemy-side) — watch for this, has caused real bugs.
- `SkillCategory.Summon`/`Skill.SummonFactory`/`Skill.SummonFactoryChoices` for mid-fight enemy spawning, resolved via `ResolveSummon` — random pick uses CombatEngine's shared `_random`.
- `Enemy.EnrageTriggeredRound` + `GetEnrageMultiplier` — generic DOUBLING escalation Enrage (baseline boost at trigger Round, then the bonus itself doubles every subsequent Round). Reusable across any Elite/Boss.
- `Enemy.PhaseTwoHpThreshold`/`PhaseTwoDamageMultiplier`/`PermanentDamageMultiplier` — a SEPARATE one-time, flat, HP-triggered buff (Warden's Phase 2), deliberately NOT wired through Enrage.
- Real AoE Attack support: `ResolveAttack` branches on `TargetType.AllEnemies` via an extracted `ResolveSingleTargetAttack` helper.
- `Skill.Clone()` — required so Reforge/Weaving hand out independent copies of pooled skills, not shared references (an Augment on one copy must never corrupt another Anima's copy of the "same" skill).
- **Recurring gotcha:** root namespace "Anima" collides with the `Anima` class (CS0118). Inside a namespace block: move `using` above the declaration. At global/top-level scope (`Program.cs`): use an alias instead (`using AnimaUnit = Anima.Core.Models.Anima;`).

## Map Generation (LOCKED, IMPLEMENTED, VALIDATED — 500 seeded maps pass every rule)

Real Slay-the-Spire-style algorithm, ported from a detailed community write-up of STS's actual generation logic (not guessed at):
- **Grid:** 7 nodes wide, 15 floors tall, plus a Boss node added after.
- **Path generation:** pick a random Floor 1 starting node, connect it to 1 of the 3 closest nodes on the next floor, repeat this process 6 separate times (6 independent path-chains). Rules: the first 2 Floor 1 starting nodes must differ from each other; paths can never cross geometrically.
- **Cleanup:** remove any node with zero paths connecting to it.
- **Fixed floors (assigned BEFORE the random pass, not after):** Floor 1 = all Combat, Floor 9 = all Treasure, Floor 15 = all Shop.
- **Random floors:** everything else gets a type via locked odds — **Combat 35% / Elite 15% / Resource 15% / Treasure 15% / Shop 15% / Reforge 5%**.
- **Override rules:** Elite/Shop/Reforge can't appear below Floor 6. Elite/Shop/Reforge can't be directly path-connected to each other (adjacency rule). A node with 2+ outgoing paths must lead to unique-type destinations — **except** branching into Floor 9 or 15, which are single-type by rule, making uniqueness mathematically impossible there (a known, documented, unavoidable exemption — not a bug). Shop can't be on Floor 14.
- **Boss:** added last, connects from every Floor 15 node.

**Node types (6 total):** Combat / Elite / Resource / Treasure / Shop / Reforge / Boss.
- **Resource** is currently a placeholder for a future richer "Events" system (like STS's "?" nodes) — for now it just grants flat Wisp, intentionally with NO Ember (this is what keeps Combat meaningfully better than pure safe-farming).
- **Elite is optional/skippable** (matches real STS) — players can route around Elites if they choose. Reward tier ladder (intentional, to make risk worth it): Resource (Wisp only, zero risk) < Combat (Wisp + Ember chance) < Elite (more Wisp/Ember + guaranteed/high-chance Shard fragment) < Boss (guaranteed Vessel + most Wisp + guaranteed Shard fragment, the one mandatory encounter).

## Reforge Node (LOCKED, IMPLEMENTED, VERIFIED)

Pay Wisp for ONE random part rolled from ANY color's Archetypes (a genuine cross-color option — echoes the core rule that parts can carry a different color than their Anima's base color). Player sees the roll before committing:
- **Accept** (40 Wisp base, or 80 Wisp if they chose which color the roll draws from): swap happens on a chosen team Anima. Any Augment on the skill being replaced is LOST (not refunded — a real, meaningful tradeoff).
- **Decline:** free, no change.
- Change is **run-only** — reverts when the Delve ends, never touches the Anima's permanent data.

## Weaving System (LOCKED, IMPLEMENTED, VERIFIED — inspired directly by real Axie Infinity mechanics, confirmed via research)

**Core resolution algorithm**, per part slot (Head/Frame/Tail/Crest), independently:
1. Each parent has their own Dominant/R1/R2 genes for that slot. Pool all 6 (3 per parent) and roll weighted by **37.5% / 9.375% / 3.125%** (per parent — sums to 100% across both). The winner becomes the **offspring's own new Dominant** for that slot (what actually manifests).
2. Roll twice more from the remaining pool to fill the offspring's own hidden R1/R2 for that slot.
3. **Mutation:** 10% independent chance on R1/R2 rolls ONLY (never the Dominant) — replaces with a fully random skill from that slot's pool across all 4 colors.

**Color:** flat 50/50 either parent's color; 100% if both parents already share the same color (direct analog to how Axie's class inheritance works — confirmed Axie's class has NO hidden gene layer, unlike body parts, just a flat roll).

**Hybrid trigger:** if both parents are fully pure and match a locked pairing (Onyx+Crimson → Vulcan, Verdant+Azure → Mirage), 33% chance it overrides normal resolution entirely. Hybrid part composition currently reuses the same normal weighted-roll logic (placeholder — hybrid-specific breeding behavior not yet designed, flagged as such).

**Sibling restriction** (copies Axie's real rule exactly — NOT deep lineage checking): `Anima.ParentAId`/`ParentBId` (nullable, null = founder). Reject a Weave if either candidate is a direct parent of the other, OR both share the exact same two parents (full siblings). Grandparents/cousins/half-siblings are all fine.

**Weave Count:** `Anima.WeaveCount` (starts 0). Cost = Parent A's cost-at-their-count + Parent B's cost-at-their-count, using the curve **50/100/175/275/400**. Both parents increment by 1 on a successful Weave. Reject entirely if either parent is already at 5.

**Echo** (a twin-Vessel outcome — two offspring from one attempt — original to Anima, no direct Axie equivalent): two trigger paths —
1. Spontaneous: 5% base chance, checked AFTER the hybrid-trigger check on the same roll (a hybrid and spontaneous Echo can't both come from the same single roll — this gating is intentional).
2. Guaranteed: spending 5 Echo Shards (`spendEchoShards` — the real path; `forceEcho` also still exists as a pure test hook, untouched by the economy).

When Echo triggers, **both twins run the ENTIRE independent Weaving algorithm separately** (same 2 parents, own full roll each, own hybrid-check) — twins can genuinely differ, are never duplicated. **Confirmed intentional:** a twin CAN itself come back hybrid via its own independent roll, even though hybrid+spontaneous-Echo can't happen together on a single non-Echo roll.

## Resource Economy (LOCKED, IMPLEMENTED, VERIFIED)

Two ledger types:
- **`PersistentLedger`** — account-level, survives between Delves. Tracks `ResourceType` (extensible enum: Wisp, EchoShard, VesselShard — adding future materials is just adding an enum value). `GetBalance`/`CanAfford`/`Add`/`TrySpend` (afford-then-commit, never partial-spends).
- **`RunLedger`** — Delve-scoped, resets each run. Currently just holds a `List<Artifact>` (Artifacts aren't wired to anything yet, but there's a home for them). Reforge's temporary part swaps are deliberately NOT tracked here — they're already Anima-instance-scoped.

**What's persistent (survives between runs):** Wisp, Echo Shards, Vessel Shards, any future crafting materials. **What's run-only (resets each Delve):** Artifacts, temporary Reforge swaps, in-Delve HP state.

Both `WeavingService.AttemptWeave` and `ReforgeService.Accept` now take a `PersistentLedger` and actually check affordability + deduct on success (previously they only priced actions without charging anything — this is now closed).

## Core Game Rules (Combat)

### Colors
| Color | HP | Def | Speed | Dmg mult | Spirit mult |
|---|---|---|---|---|---|
| Crimson(DPS) | 100 | 7 | 10 | 1.3x | 0.7x |
| Onyx(Tank) | 130 | 13 | 7 | 1.0x | ~0.8x |
| Verdant(Healer) | 100 | 10 | 10 | 0.7x | 1.3x |
| Azure(Utility) | 70 | 10 | 13 | 1.0x | 1.0x |
| Vulcan(hybrid, Onyx+Crimson) | 143 | 10 | 6 | 1.3x | 0.7x |
| Mirage(hybrid, Verdant+Azure) | 60 | 10 | 13 | 0.7x | 1.4x |

### Combat Math
`Final Dmg = Base x DamageMult`. Modifier stacking: additive first, then multiplicative sequential; attacker's own mods first, opponent's(Weak) last. Shield absorbs first, additive, capped at 50. `Final Heal = Base x SpiritMult`, capped at MaxHp. DOTs bypass Defense+Shield. No randomness except deck shuffle, enemy 50/50 summon rolls, and Weaving's own probability system.

### Status Effects — CRITICAL PATTERN
**Any status affecting "target's next action" MUST use Until-Consumed, NEVER Fixed-turn:1.** Found/fixed on Weak, Taunt, AND Retaliate/Thorns.

## ALL 4 COLORS — FULLY CODED (48 skills, 12 Archetypes)

**CRIMSON:** P1["Ember"]:Slash/Charge/Execute/Reckless. P2["Reaper"]:Rend/Lunge/Frenzy/Bloodthirst. P3["Marksman"]:Snipe/Retreat/Marked Shot/Steady Aim.
**ONYX:** P1["Boulder"]:Bash/Hardened/Taunt/Courage. P2["Aegis"]:Guard Strike/Fortify/Shatter/Inspire. P3["Warden"](*name collides with the Boss "Warden of the Hollow" — different entities*):Intercept/Bristle/Disarm/Vengeance.
**VERDANT:** P1["Sprout"]:Smite/Guiding Light/Lifebloom/Soul Link. P2["Thicket"]:Renew/Healing Rain/Bloom/Providence. P3["Lotus"]:Purge/Silence/Cleanse/Clarity.
**AZURE:** P1["Shade"]:Pin/Exploit/Misdirect/Ambush. P2["Anchor"]:Shove/Enfeeble/Hook/Last Laugh. P3["Veil"]:Deflect/Safeguard/Ward/Retribution.

Full skill details (damage numbers, energy costs, exact effects) are in `Anima_Design_Doc.md` — ask the user to paste the relevant section if needed rather than guessing.

## Enemy Roster — ALL FULLY VALIDATED

- **Grovehide/Quillfang** (Basic): tested, good pacing.
- **The Sentinel** (Elite, DPS-check): HP105/Def12/Spd8. Enrage@Round18. Naked trio loses, augmented trio wins consistently.
- **The Leech Mother** (Elite, Sustain-check): HP115/Def10/Spd6. No Enrage needed (structurally can't stall). **This fight proved Marked/redirect mechanics only matter with genuine targeting ambiguity** (her guard must spawn at position 1, not 2, to create real choice) — this lesson directly shaped Warden's design too.
- **Warden of the Hollow** (Boss): HP220, Defense 11 (FINAL — do not nerf further without checking with the user; there was an extensive tuning arc that discovered the real issue was team composition, not her stats — see below). Two summonable adds create genuine targeting ambiguity. EnrageRound=20, doubling escalation. **~40% win rate for a "primitive"/un-bred team is INTENTIONAL** — ties directly to the "smart breeding" pillar; players who Weave better builds should out-farm this baseline. The starter trio (no Azure) literally cannot win regardless of her stats since it has no guard-bypass tool — swapping in Shade (Misdirect) immediately produced wins. **Key lesson: always verify you're testing with the right team before concluding a fight's balance is wrong.**

## Known TODOs / Not Yet Implemented
- Echo Shard/Vessel Shard *earning* from combat (the ledger exists and can be spent, but nothing grants these yet from Elite/Boss wins).
- Artifacts (10 locked, run-only): not wired to anything yet, though `RunLedger` has a home for them.
- Boss loot table wiring (design locked: guaranteed Vessel + larger Wisp + guaranteed Shard fragment).
- Healing-between-nodes wiring (~30-40% max HP, not full — tentative).
- HP persistence across a real Delve (attrition vs. reset — undecided).
- Cross-color hybrid combat-value question still technically open (old Bastion-vs-Boulder tests were inconclusive) — now that real Weaving exists, this could finally be retested properly with genuinely-bred hybrids.
- Godot client: not started — this is the user's stated next major direction once Weaving is sufficiently complete.
- ASP.NET Core/SignalR server: not started.
- Murkbind, standalone Rustling Swarm: designed, not coded.

## Working Preferences
- User is a C# backend dev, no frontend experience — Claude does all coding, user handles deployment.
- Wants build confirmation after every change.
- Highly values honest reporting of bugs/gaps/inconclusive results — has repeatedly praised catching real issues and refusing to fabricate results (e.g. correctly declining to assert a fake "expected" statistic for a conditional probability distribution during Weaving verification) rather than being told what they want to hear.
- Appreciates regression tests after touching shared/core code, and is comfortable running larger sample sizes when small batches prove noisy.
- Calls Claude Code "CC" in the separate design conversation where all game design decisions actually get made — if you need details not summarized here, ask the user to paste from `Anima_Design_Doc.md` rather than inventing values.
