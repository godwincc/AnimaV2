namespace Anima.Core.Data;

using Anima.Core.Enums;
using Anima.Core.Models;

// SAMPLE / REFERENCE DATA — validates the Enemy + EnemyBehaviorRule pattern end-to-end.
// Not the real enemy roster. Replace once encounter design is finalized.
public static class SampleEnemies
{
    // Warden of the Hollow's add-summon cadence -- Rounds between "no add currently active"
    // becoming eligible to summon again, measured from the last add's death (or fight start, if
    // none has spawned yet). Retuned up from 5 -- testing found 5 left the augmented batch still
    // losing every run even at EnrageRound 25; a longer gap gives the team more windows to reach
    // Warden directly without a guard in front of her. Shared by the initial eligibility fallback
    // and both adds' OnDeath reschedule below, so it only needs to change in one place.
    private const int WardenSummonCadenceRounds = 7;

    public static Enemy CreateQuillfang()
    {
        var needleVolley = new Skill
        {
            Name = "Needle Volley",
            Category = SkillCategory.Attack,
            Range = AttackRange.Ranged,
            Target = TargetType.Enemy,
            EnergyCost = 0,
            BaseDamage = 14,
        };

        var retreat = new Skill
        {
            Name = "Retreat",
            Category = SkillCategory.Move,
            Range = AttackRange.NA,
            Target = TargetType.SelfTarget,
            EnergyCost = 0,
            TargetPositionOverride = new[] { 3 },
        };

        return new Enemy
        {
            Name = "Quillfang",
            MaxHp = 40,
            Defense = 4,
            CurrentHp = 40,
            Position = 1,
            Speed = 5,
            BehaviorRules = new List<EnemyBehaviorRule>
            {
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => enemy.CurrentHp < enemy.MaxHp / 2,
                    Skill = retreat,
                },
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => true, // fallback
                    Skill = needleVolley,
                },
            },
        };
    }

    public static Enemy CreateGrovehide()
    {
        var clawSwipe = new Skill
        {
            Name = "Claw Swipe",
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 0,
            BaseDamage = 18,
        };

        return new Enemy
        {
            Name = "Grovehide",
            MaxHp = 60,
            Defense = 8,
            CurrentHp = 60,
            Position = 1,
            Speed = 5,
            BehaviorRules = new List<EnemyBehaviorRule>
            {
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => true, // always attacks
                    Skill = clawSwipe,
                },
            },
        };
    }

    // Elite — telegraphs a big hit: Guard Stance (shield up) one turn, Charging Slam the next.
    public static Enemy CreateSentinel()
    {
        var chargingSlam = new Skill
        {
            Name = "Charging Slam",
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 0,
            BaseDamage = 55,
        };

        var guardStance = new Skill
        {
            Name = "Guard Stance",
            Category = SkillCategory.Buff,
            Range = AttackRange.NA,
            Target = TargetType.SelfTarget,
            EnergyCost = 0,
            BaseShield = 20,
            Duration = DurationType.UntilConsumed,
        };

        return new Enemy
        {
            Name = "The Sentinel",
            MaxHp = 105, // retuned down from 140 -- Elite fights were running 17-28 rounds, vs. 2-7 for a normal Combat-tier fight
            Defense = 12,
            CurrentHp = 105,
            Position = 1,
            Speed = 8,
            // Generous buffer past the round-15 target — only kicks in if the fight is genuinely
            // dragging, not to interfere with the normal Guard Stance / Charging Slam telegraph.
            EnrageRound = 18,
            BehaviorRules = new List<EnemyBehaviorRule>
            {
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => enemy.AiState.TryGetValue("IsCharging", out var v) && v is true,
                    Skill = chargingSlam,
                    // Once Enraged, Guard Stance never fires again to re-arm this flag — so leave it
                    // set once it's already true, letting full-aggro Charging Slam fire every round.
                    OnUsed = enemy =>
                    {
                        if (!enemy.IsEnraged) enemy.AiState["IsCharging"] = false;
                    },
                },
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => true, // fallback: wind up for next turn's Charging Slam
                    Skill = guardStance,
                    OnUsed = enemy => enemy.AiState["IsCharging"] = true,
                    IsDefensive = true,
                },
            },
        };
    }

    // Elite — sustains itself by healing off its own attacks.
    public static Enemy CreateLeechMother()
    {
        var drainingClaw = new Skill
        {
            Name = "Draining Claw",
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 0,
            BaseDamage = 20,
            SelfHealPercentOfDamage = 0.3, // retuned down from 0.5 -- fight length tuning
        };

        // Guard-style summon: Rustling Swarm takes position 1 and Leech Mother is pushed back
        // to position 2, so default front-only targeting hits the add instead of her -- the
        // add actually screens the real threat, rather than just adding a second damage source.
        var spawnBrood = new Skill
        {
            Name = "Spawn Brood",
            Category = SkillCategory.Summon,
            Target = TargetType.SelfTarget,
            EnergyCost = 0,
            SummonFactory = CreateRustlingSwarm,
            SummonInFront = true,
        };

        return new Enemy
        {
            Name = "Leech Mother",
            MaxHp = 115, // retuned down from 160 -- Elite fights were running 17-28 rounds, vs. 2-7 for a normal Combat-tier fight
            Defense = 10,
            CurrentHp = 115,
            Position = 1,
            Speed = 6,
            BehaviorRules = new List<EnemyBehaviorRule>
            {
                // Summon a Rustling Swarm add once, on the first turn Leech Mother actually gets
                // to act at or after Round 5 -- >= rather than == so a Stun/skip landing exactly
                // on Round 5 doesn't silently eat the trigger forever (this rule is only checked
                // when ResolveEnemyTurn runs, which a stunned turn skips entirely; found via the
                // Shade-lockdown matchup, where Leech Mother was stunned on exactly Round 5 in
                // all 5 test fights and Spawn Brood never fired).
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) =>
                        state.RoundNumber >= 5 && enemy.AiState.GetValueOrDefault("HasSummonedBrood") is not true,
                    Skill = spawnBrood,
                    OnUsed = enemy => enemy.AiState["HasSummonedBrood"] = true,
                },
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => true, // fallback: always attacks
                    Skill = drainingClaw,
                },
            },
        };
    }

    // Lightweight stand-in for Rustling Swarm, summoned by Leech Mother's Spawn Brood.
    // Speed isn't specified by the design doc for this placeholder -- set to 6 (matching Leech
    // Mother's own Speed) as a reasonable, clearly-flagged assumption pending real numbers.
    private static Enemy CreateRustlingSwarm()
    {
        var skitterBite = new Skill
        {
            Name = "Skitter Bite",
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 0,
            BaseDamage = 8,
        };

        return new Enemy
        {
            Name = "Rustling Swarm",
            MaxHp = 20,
            Defense = 2,
            CurrentHp = 20,
            Speed = 6,
            BehaviorRules = new List<EnemyBehaviorRule>
            {
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => true, // always attacks
                    Skill = skitterBite,
                },
            },
        };
    }

    // Boss — the game's first. Phase 1 telegraphs the same way Sentinel does (alternating
    // self-Shield wind-up / big hit), but the Phase 2 transition is HP-triggered (below 50%)
    // rather than Round-triggered, and is a one-time flat buff -- NOT the Round-based escalating
    // Enrage safety net (see PhaseTwoHpThreshold/PermanentDamageMultiplier on Enemy).
    //
    // Tuning history: the base Phase design alone produced a 5/5 stalemate/regen-loop (the
    // repeating guard-add cycle protects her behind position 2 too often, and an unkilled
    // DPS-race add could out-heal the team's chip damage entirely) -- added EnrageRound = 20
    // (2 Rounds later than Sentinel's 18, given the added time the add cycles eat into a fight)
    // as the same generic safety net Sentinel uses. That resolved the infinite stalling but just
    // converted every run into a fast LOSS instead (Defense 14 left too little of an AoE-augment's
    // halved chip damage getting through, and the Broodling's 15 HP/turn heal could still fully
    // offset progress pre-Enrage) -- Defense and the Broodling's heal are retuned down below in
    // response.
    public static Enemy CreateWardenOfTheHollow()
    {
        var heavyStrike = new Skill
        {
            Name = "Heavy Strike",
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 0,
            BaseDamage = 35,
        };

        var guardPulse = new Skill
        {
            Name = "Guard Pulse",
            Category = SkillCategory.Buff,
            Range = AttackRange.NA,
            Target = TargetType.SelfTarget,
            EnergyCost = 0,
            BaseShield = 30,
            Duration = DurationType.UntilConsumed,
        };

        var summonAdd = new Skill
        {
            Name = "Summon Add",
            Category = SkillCategory.Summon,
            Target = TargetType.SelfTarget,
            EnergyCost = 0,
            SummonInFront = true,
        };

        var warden = new Enemy
        {
            Name = "Warden of the Hollow",
            MaxHp = 220,
            Defense = 11, // retuned down from 14 -- testing found the augmented team's chip damage too weak to matter before Enrage
            CurrentHp = 220,
            Position = 1,
            // Not specified by the design brief -- placeholder between Leech Mother's 6 and
            // Sentinel's 8, flagged pending real numbers (same pattern as Rustling Swarm's Speed).
            Speed = 7,
            PhaseTwoHpThreshold = 110, // 50% of MaxHp
            PhaseTwoDamageMultiplier = 1.5, // Reckless Fury: +50%, flat and permanent once triggered
            EnrageRound = 25, // pushed up from 20 (via 23) -- 20 got even the augmented batch losing every run 2-8 Rounds past the trigger despite real (Defense/heal-retune-driven) progress; 25 gives the team more runway before the escalation clock cuts a close fight off
            BehaviorRules = new List<EnemyBehaviorRule>(), // filled in below, once `warden` exists for the adds to self-reference
        };

        // Both adds need to reference `warden` (heal target / summon-cooldown reschedule on
        // death), so they're built after `warden` exists rather than inline above.
        summonAdd.SummonFactoryChoices = new Func<Enemy>[]
        {
            () => CreateWardenDpsRaceAdd(warden),
            () => CreateWardenTankAdd(warden),
        };

        warden.BehaviorRules = new List<EnemyBehaviorRule>
        {
            // Summon check takes priority over the attack telegraph below, same ordering as
            // Leech Mother's own Spawn Brood rule -- a due summon pre-empts whatever Phase-1
            // alternation Warden would otherwise be mid-cycle on.
            new EnemyBehaviorRule
            {
                Condition = (enemy, state) =>
                {
                    var addActive = state.EnemyTeam.Any(e => e.CurrentHp > 0 && !ReferenceEquals(e, enemy));
                    var nextEligibleRound = enemy.AiState.TryGetValue("NextEligibleSummonRound", out var v) ? (int)v : WardenSummonCadenceRounds;
                    return !addActive && state.RoundNumber >= nextEligibleRound;
                },
                Skill = summonAdd,
            },
            new EnemyBehaviorRule
            {
                Condition = (enemy, state) =>
                    enemy.PhaseTwoTriggered || (enemy.AiState.TryGetValue("Charging", out var v) && v is true),
                Skill = heavyStrike,
                // Once Phase 2 hits, Charging never gets reset -- same "leave it set" pattern as
                // Sentinel's post-Enrage Charging Slam, letting Heavy Strike fire every Round.
                OnUsed = enemy =>
                {
                    if (!enemy.PhaseTwoTriggered) enemy.AiState["Charging"] = false;
                },
            },
            new EnemyBehaviorRule
            {
                Condition = (enemy, state) => !enemy.PhaseTwoTriggered, // stops firing entirely once Phase 2 hits
                Skill = guardPulse,
                OnUsed = enemy => enemy.AiState["Charging"] = true,
            },
        };

        return warden;
    }

    // Add 1 -- "DPS-race add": a small attacker that also heals Warden 15 HP at the start of
    // each of its own turns while it's alive, creating a genuine choice: ignore it and let Warden
    // slowly out-heal chip damage, or spend actions killing a 25 HP target instead of hitting her.
    private static Enemy CreateWardenDpsRaceAdd(Enemy warden)
    {
        var siphonBite = new Skill
        {
            Name = "Siphon Bite",
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 0,
            BaseDamage = 8,
        };

        return new Enemy
        {
            Name = "Warden's Broodling",
            MaxHp = 25,
            Defense = 3,
            CurrentHp = 25,
            Speed = 6,
            HealsOnTurnStartTarget = warden,
            HealsOnTurnStartAmount = 8, // retuned down from 15 -- testing found an unkilled Broodling could fully out-heal the team's chip damage (the "regen-loop")
            BehaviorRules = new List<EnemyBehaviorRule>
            {
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => true, // always attacks
                    Skill = siphonBite,
                },
            },
            OnDeath = state => warden.AiState["NextEligibleSummonRound"] = state.RoundNumber + WardenSummonCadenceRounds,
        };
    }

    // Add 2 -- "Tank/heal-check add": no gimmick, just a chunkier HP/Defense body that eats
    // several turns of focus fire on its own -- the "is it worth pushing through" counterpart to
    // Add 1's "is it worth racing" choice.
    private static Enemy CreateWardenTankAdd(Enemy warden)
    {
        var crush = new Skill
        {
            Name = "Crush",
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 0,
            BaseDamage = 22,
        };

        return new Enemy
        {
            Name = "Warden's Bulwark",
            MaxHp = 45,
            Defense = 8,
            CurrentHp = 45,
            Speed = 5,
            BehaviorRules = new List<EnemyBehaviorRule>
            {
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => true, // always attacks
                    Skill = crush,
                },
            },
            OnDeath = state => warden.AiState["NextEligibleSummonRound"] = state.RoundNumber + WardenSummonCadenceRounds,
        };
    }
}
