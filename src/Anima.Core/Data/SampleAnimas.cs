namespace Anima.Core.Data;

using Anima.Core.Enums;
using Anima.Core.Models;
using AnimaUnit = Anima.Core.Models.Anima;

// SAMPLE / REFERENCE DATA — the starter trio, one Primitive per color, used to validate
// the combat engine end-to-end. Not the final roster.
public static class SampleAnimas
{
    // Crimson Primitive 1 (Burst Combo)
    public static AnimaUnit CreateEmber()
    {
        var stats = new Stats
        {
            MaxHp = 100,
            Defense = 7,
            Speed = 10,
            DamageMultiplier = 1.3,
            SpiritMultiplier = 0.7,
        };

        var slash = new Skill
        {
            Name = "Slash",
            Part = Part.Head,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 2,
            BaseDamage = 25,
        };

        var charge = new Skill
        {
            Name = "Charge",
            Part = Part.Frame,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Buff,
            Target = TargetType.SelfTarget,
            EnergyCost = 1,
            Duration = DurationType.UntilConsumed,
        };

        var execute = new Skill
        {
            Name = "Execute",
            Part = Part.Tail,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Attack,
            Target = TargetType.LowestHpEnemy,
            EnergyCost = 3,
            BaseDamage = 40,
        };

        var reckless = new Skill
        {
            Name = "Reckless",
            Part = Part.Crest,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveConditional,
            // TODO: +25% damage when HP < 50% isn't wired into damage resolution yet.
        };

        return new AnimaUnit
        {
            Id = "Ember",
            Color = AnimaColor.Crimson,
            BaseStats = stats,
            Head = slash,
            Frame = charge,
            Tail = execute,
            Crest = reckless,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }

    // Onyx Primitive 1 (tank/control)
    public static AnimaUnit CreateBoulder()
    {
        var stats = new Stats
        {
            MaxHp = 130,
            Defense = 13,
            Speed = 7,
            DamageMultiplier = 1.0,
            SpiritMultiplier = 0.8,
        };

        var bash = new Skill
        {
            Name = "Bash",
            Part = Part.Head,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 2,
            BaseDamage = 18, // +40% Increase Effect augment (13 -> 18), earned mid-run
            OnHitStatusKeyword = "Weak",
            OnHitStatusMagnitude = 20,
            OnHitStatusDuration = DurationType.FixedTurn,
            OnHitStatusDurationTurns = 1,
        };

        var hardened = new Skill
        {
            Name = "Hardened",
            Part = Part.Frame,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Buff,
            Target = TargetType.SelfTarget,
            EnergyCost = 1,
            BaseShield = 18,
            Duration = DurationType.UntilConsumed,
            // TODO: the "move forward 1" half isn't resolved yet — relative movement
            // isn't implemented (same gap as Ember's Charge).
        };

        var taunt = new Skill
        {
            Name = "Taunt",
            Part = Part.Tail,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Buff,
            Target = TargetType.SelfTarget,
            EnergyCost = 2,
            Duration = DurationType.FixedTurn,
            DurationTurns = 1,
        };

        var courage = new Skill
        {
            Name = "Courage",
            Part = Part.Crest,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveConditional,
            // TODO: +20% HP while in position 1 isn't wired into resolution yet.
        };

        return new AnimaUnit
        {
            Id = "Boulder",
            Color = AnimaColor.Onyx,
            BaseStats = stats,
            Head = bash,
            Frame = hardened,
            Tail = taunt,
            Crest = courage,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }

    // Verdant Primitive 1 (healer)
    public static AnimaUnit CreateSprout()
    {
        var stats = new Stats
        {
            MaxHp = 100,
            Defense = 10,
            Speed = 10,
            DamageMultiplier = 0.7,
            SpiritMultiplier = 1.3,
        };

        var smite = new Skill
        {
            Name = "Smite",
            Part = Part.Head,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Attack,
            Range = AttackRange.Ranged,
            Target = TargetType.Enemy,
            EnergyCost = 2,
            BaseDamage = 14, // +40% Increase Effect augment (10 -> 14), earned mid-run
            BaseHeal = 26,
            SecondaryTarget = TargetType.LowestHpAlly,
        };

        var guidingLight = new Skill
        {
            Name = "Guiding Light",
            Part = Part.Frame,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Heal,
            Target = TargetType.SelfTarget,
            EnergyCost = 1,
            BaseHeal = 20,
            // TODO: the "move" half isn't resolved yet — same gap as Ember's Charge.
        };

        var lifebloom = new Skill
        {
            Name = "Lifebloom",
            Part = Part.Tail,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Heal,
            Target = TargetType.LowestHpAlly,
            EnergyCost = 3,
            BaseHeal = 46,
        };

        var soulLink = new Skill
        {
            Name = "Soul Link",
            Part = Part.Crest,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveConditional,
            // TODO: +25% healing while HP > 50% isn't wired into resolution yet.
        };

        return new AnimaUnit
        {
            Id = "Sprout",
            Color = AnimaColor.Verdant,
            BaseStats = stats,
            Head = smite,
            Frame = guidingLight,
            Tail = lifebloom,
            Crest = soulLink,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }
}
