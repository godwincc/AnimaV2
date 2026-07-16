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
                    OnUsed = enemy => enemy.AiState["IsCharging"] = false,
                },
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => true, // fallback: wind up for next turn's Charging Slam
                    Skill = guardStance,
                    OnUsed = enemy => enemy.AiState["IsCharging"] = true,
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
                new EnemyBehaviorRule
                {
                    Condition = (enemy, state) => true, // always attacks
                    Skill = drainingClaw,
                },
            },
        };
    }
}
