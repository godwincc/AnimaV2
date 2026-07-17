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
            OnHitStatusDuration = DurationType.UntilConsumed, // consumed by the target's next skill, not a Round tick
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
            // Duration/DurationTurns aren't used -- CombatEngine special-cases "Taunt" to apply
            // Marked instead, which is always Until-Consumed. See CombatEngine.ApplyMarked.
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

    // Hybrid test build — same Onyx base/Head/Frame/Crest as Boulder, but Tail is swapped for
    // Verdant's Cleanse instead of Taunt. Not a real Primitive; exists to compare a self-sustain
    // build (no forced redirect, but self-sufficient healing) against Boulder's aggro-tank build.
    // Per the locked scaling rule, a part's Color is just its origin/flavor — the Anima's own
    // stats (Onyx's Spirit multiplier here, not Verdant's) still govern the skill's scaling.
    public static AnimaUnit CreateBastion()
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
            OnHitStatusDuration = DurationType.UntilConsumed,
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
        };

        var cleanse = new Skill
        {
            Name = "Cleanse",
            Part = Part.Tail,
            Color = AnimaColor.Verdant, // part's own origin color; scaling still uses Bastion's own Spirit multiplier
            Category = SkillCategory.Heal,
            Target = TargetType.LowestHpAlly,
            EnergyCost = 2,
            BaseHeal = 33,
            RemovesDebuff = true,
        };

        var courage = new Skill
        {
            Name = "Courage",
            Part = Part.Crest,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveConditional,
            // -20% damage taken while in position 1 — see CombatEngine.GetCourageMultiplier.
        };

        return new AnimaUnit
        {
            Id = "Bastion",
            Color = AnimaColor.Onyx,
            BaseStats = stats,
            Head = bash,
            Frame = hardened,
            Tail = cleanse,
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

    // Azure Primitive 1 (Rogue/Debuffer). Named "Shade" rather than "Wisp" to avoid colliding
    // with the (planned, not yet coded) Wisp Charm run-scoped artifact.
    public static AnimaUnit CreateShade()
    {
        var stats = new Stats
        {
            MaxHp = 70,
            Defense = 10,
            Speed = 13,
            DamageMultiplier = 1.0,
            SpiritMultiplier = 1.0,
        };

        var pin = new Skill
        {
            Name = "Pin",
            Part = Part.Head,
            Color = AnimaColor.Azure,
            Category = SkillCategory.Debuff,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 1,
            // No damage -- PvE behavior stands in for the PvP "disable Tail" effect: stuns the
            // target, skipping its next turn entirely. See CombatEngine.ResolveDebuff.
        };

        var exploit = new Skill
        {
            Name = "Exploit",
            Part = Part.Frame,
            Color = AnimaColor.Azure,
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 1,
            BaseDamage = 8,
            OnHitStatusKeyword = "Bleed",
            OnHitStatusMagnitude = 5, // small DOT tick -- exact value not spec'd, chosen small relative to Exploit's own 8 base
            OnHitStatusDuration = DurationType.FixedTurn,
            OnHitStatusDurationTurns = 3,
        };

        var misdirect = new Skill
        {
            Name = "Misdirect",
            Part = Part.Tail,
            Color = AnimaColor.Azure,
            Category = SkillCategory.Debuff,
            // ChosenEnemy, not Enemy: TargetType.Enemy's SelectTarget gives Marked-holders
            // targeting priority, which would make Misdirect always re-select whichever enemy
            // is ALREADY Marked -- unable to ever redirect Marked onto a different target. Found
            // via the standalone sanity check. ChosenEnemy skips that priority (stands in for a
            // real player-chosen target, same idea as Execute's LowestHpEnemy).
            Target = TargetType.ChosenEnemy,
            EnergyCost = 2,
            // Applies Marked to its target via the unified Marked mechanism (single-slot per
            // team, Until-Consumed) -- overrides any existing Marked, including a self-applied
            // Taunt. See CombatEngine.ResolveDebuff / ApplyMarked.
        };

        var ambush = new Skill
        {
            Name = "Ambush",
            Part = Part.Crest,
            Color = AnimaColor.Azure,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveConditional,
            // Double damage when this Anima acts LAST in the Round's Initiative order --
            // see CombatEngine.GetOffensiveCrestMultiplier.
        };

        return new AnimaUnit
        {
            Id = "Shade",
            Color = AnimaColor.Azure,
            BaseStats = stats,
            Head = pin,
            Frame = exploit,
            Tail = misdirect,
            Crest = ambush,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }
}
