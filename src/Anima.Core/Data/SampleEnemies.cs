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
}
