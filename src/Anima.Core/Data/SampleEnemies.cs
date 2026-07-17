namespace Anima.Core.Data;

using Anima.Core.Enums;
using Anima.Core.Models;

// SAMPLE / REFERENCE DATA — validates the Enemy + EnemyBehaviorRule pattern end-to-end.
// Not the real enemy roster. Replace once encounter design is finalized.
public static class SampleEnemies
{
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
            MaxHp = 140,
            Defense = 12,
            CurrentHp = 140,
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
            SelfHealPercentOfDamage = 0.5,
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
            MaxHp = 160,
            Defense = 10,
            CurrentHp = 160,
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
}
