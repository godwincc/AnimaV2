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
            MoveOffset = -1, // move forward 1; now resolved generically by ResolveBuff
            // Applies a "Charge" status via ResolveBuff's generic fallback, but Primed's actual
            // x2-damage-on-next-hit effect still isn't wired into damage resolution -- separate,
            // pre-existing gap, out of scope here.
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
            MoveOffset = -1, // move forward 1; now resolved generically by ResolveBuff
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
            MoveOffset = -1, // move forward 1; now resolved generically by ResolveBuff
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

    // Crimson Primitive 2 (Sustain/DOT). Named "Reaper" rather than reusing "Ember" -- same
    // Crimson base stats, different kit/Archetype.
    public static AnimaUnit CreateReaper()
    {
        var stats = new Stats
        {
            MaxHp = 100,
            Defense = 7,
            Speed = 10,
            DamageMultiplier = 1.3,
            SpiritMultiplier = 0.7,
        };

        var rend = new Skill
        {
            Name = "Rend",
            Part = Part.Head,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 2,
            BaseDamage = 22,
            OnHitStatusKeyword = "Bleed",
            OnHitStatusMagnitude = 8, // Rend is this Archetype's signature DOT tool (higher energy cost, Head slot) -- hits harder than Azure Exploit's secondary-effect Bleed (5)
            OnHitStatusDuration = DurationType.FixedTurn,
            OnHitStatusDurationTurns = 3,
        };

        var lunge = new Skill
        {
            Name = "Lunge",
            Part = Part.Frame,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 2,
            BaseDamage = 20,
            MoveOffset = -1, // "forward" = toward position 1; see CombatEngine.ResolveAttack
        };

        var frenzy = new Skill
        {
            Name = "Frenzy",
            Part = Part.Tail,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Buff,
            Range = AttackRange.NA,
            Target = TargetType.SelfTarget,
            EnergyCost = 2,
            Duration = DurationType.UntilConsumed,
            // Until-Consumed, not Fixed-turn: consumed on the caster's own next action (see
            // CombatEngine.ConsumeFrenzy) -- a Fixed-turn:1 duration would tick down and vanish
            // at the very next Round Start, before that next action (and the Initiative
            // computation that precedes it) ever gets to use it. Same reasoning as Weak/Marked.
        };

        var bloodthirst = new Skill
        {
            Name = "Bloodthirst",
            Part = Part.Crest,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveEvent,
            // Heals 25% of damage dealt on every hit -- see CombatEngine.GetLifestealCrestPercent.
        };

        return new AnimaUnit
        {
            Id = "Reaper",
            Color = AnimaColor.Crimson,
            BaseStats = stats,
            Head = rend,
            Frame = lunge,
            Tail = frenzy,
            Crest = bloodthirst,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }

    // Crimson Primitive 3 (Ranged). Named "Marksman" -- distinct from Ember/Reaper, same
    // Crimson base stats, different kit/Archetype.
    public static AnimaUnit CreateMarksman()
    {
        var stats = new Stats
        {
            MaxHp = 100,
            Defense = 7,
            Speed = 10,
            DamageMultiplier = 1.3,
            SpiritMultiplier = 0.7,
        };

        var snipe = new Skill
        {
            Name = "Snipe",
            Part = Part.Head,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Attack,
            Range = AttackRange.Ranged,
            Target = TargetType.Enemy,
            EnergyCost = 2,
            BaseDamage = 22,
        };

        var retreat = new Skill
        {
            Name = "Retreat",
            Part = Part.Frame,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Buff,
            Range = AttackRange.NA,
            Target = TargetType.SelfTarget,
            EnergyCost = 1,
            MoveOffset = 1, // backward = toward position 3
            BuffMagnitude = 8, // Guarded's flat Defense bonus; matches Courage's rough magnitude. See CombatEngine.ResolveBuff / ApplyDamage.
        };

        // "usable from position 3 only" is narrower than Ranged's normal 2-3 -- UsableFromOverride
        // wins over Range in CombatEngine.IsSkillUsableFrom.
        var markedShot = new Skill
        {
            Name = "Marked Shot",
            Part = Part.Tail,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Attack,
            Range = AttackRange.Ranged,
            UsableFromOverride = new[] { 3 },
            // The design note said "targets Chosen Any", but Marked Shot needs to hit an ENEMY
            // (Exposed only makes sense on a target ally attacks can then pile onto) --
            // TargetType.ChosenAny is actually the ALLY-side "any position" type in this engine
            // (see IsFriendlyTargetType); ChosenEnemy is the enemy-side equivalent Misdirect
            // already uses for the same "any position" reasoning. Using ChosenAny here would
            // debuff a teammate, so this uses ChosenEnemy instead.
            Target = TargetType.ChosenEnemy,
            EnergyCost = 2,
            BaseDamage = 15,
            OnHitStatusKeyword = "Exposed",
            OnHitStatusDuration = DurationType.UntilConsumed,
            // Until-Consumed, not Fixed-turn:1 -- consumed by ApplyDamage the moment it actually
            // affects an incoming hit, same reasoning as Weak/Guarded/Shield.
        };

        var steadyAim = new Skill
        {
            Name = "Steady Aim",
            Part = Part.Crest,
            Color = AnimaColor.Crimson,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveConditional,
            // +15% damage while in position 3 -- see CombatEngine.GetOffensiveCrestMultiplier.
        };

        return new AnimaUnit
        {
            Id = "Marksman",
            Color = AnimaColor.Crimson,
            BaseStats = stats,
            Head = snipe,
            Frame = retreat,
            Tail = markedShot,
            Crest = steadyAim,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }

    // Onyx Primitive 2 (Bulwark/Shield). Named "Aegis" -- distinct from Boulder and the Bastion
    // test hybrid, same Onyx base stats, different kit/Archetype.
    public static AnimaUnit CreateAegis()
    {
        var stats = new Stats
        {
            MaxHp = 130,
            Defense = 13,
            Speed = 7,
            DamageMultiplier = 1.0,
            SpiritMultiplier = 0.8,
        };

        var guardStrike = new Skill
        {
            Name = "Guard Strike",
            Part = Part.Head,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Attack,
            Range = AttackRange.Melee,
            Target = TargetType.Enemy,
            EnergyCost = 2,
            BaseDamage = 15,
            SelfShieldPercentOfDamage = 1.0, // Shield equal to 100% of damage dealt; still capped at 50 via GrantShield
        };

        var fortify = new Skill
        {
            Name = "Fortify",
            Part = Part.Frame,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Buff,
            Range = AttackRange.NA,
            Target = TargetType.SelfTarget,
            EnergyCost = 2,
            BaseShield = 32,
            Duration = DurationType.UntilConsumed,
            // No MoveOffset -- unlike Hardened/Retreat, Fortify is explicitly stationary.
        };

        var shatter = new Skill
        {
            Name = "Shatter",
            Part = Part.Tail,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Attack,
            Range = AttackRange.NA,
            Target = TargetType.Enemy,
            EnergyCost = 3,
            DamageEqualsOwnShield = true, // BaseDamage unused; see CombatEngine.ResolveAttack
        };

        var inspire = new Skill
        {
            Name = "Inspire",
            Part = Part.Crest,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveEvent,
            // Whenever this Anima gains Shield (any source), all other living allies gain 30% of
            // the amount granted -- see CombatEngine.TriggerInspire.
        };

        return new AnimaUnit
        {
            Id = "Aegis",
            Color = AnimaColor.Onyx,
            BaseStats = stats,
            Head = guardStrike,
            Frame = fortify,
            Tail = shatter,
            Crest = inspire,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }

    // Onyx Primitive 3 (Reactive). Named "Warden" -- distinct from Boulder and Aegis, same Onyx
    // base stats, different kit/Archetype.
    public static AnimaUnit CreateWarden()
    {
        var stats = new Stats
        {
            MaxHp = 130,
            Defense = 13,
            Speed = 7,
            DamageMultiplier = 1.0,
            SpiritMultiplier = 0.8,
        };

        // "Position 2 only" (UsableFromOverride) guarantees moving to position 1 is always a
        // -1 relative shift, so this reuses the same MoveOffset field Lunge/Retreat use rather
        // than needing an absolute TargetPositionOverride move.
        var intercept = new Skill
        {
            Name = "Intercept",
            Part = Part.Head,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Buff,
            Range = AttackRange.Melee,
            UsableFromOverride = new[] { 2 },
            Target = TargetType.SelfTarget,
            EnergyCost = 2,
            MoveOffset = -1,
            BuffMagnitude = 20, // Retaliate's flat counter-damage; see CombatEngine.ResolveBuff
        };

        var bristle = new Skill
        {
            Name = "Bristle",
            Part = Part.Frame,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Buff,
            Range = AttackRange.NA,
            Target = TargetType.SelfTarget,
            EnergyCost = 1,
            BuffMagnitude = 12, // Thorns' flat counter-damage; see CombatEngine.ResolveBuff
        };

        var disarm = new Skill
        {
            Name = "Disarm",
            Part = Part.Tail,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Debuff,
            Range = AttackRange.NA,
            Target = TargetType.ChosenEnemy,
            EnergyCost = 1,
            // PvE stand-in for disabling the target's Head ability: stuns the target for their
            // next turn, same mechanism as Azure's Pin -- see CombatEngine.ResolveDebuff.
        };

        var vengeance = new Skill
        {
            Name = "Vengeance",
            Part = Part.Crest,
            Color = AnimaColor.Onyx,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveConditional,
            // +25% damage dealt while this Anima's own HP is below 50% -- already handled
            // generically alongside Reckless in CombatEngine.GetOffensiveCrestMultiplier.
        };

        return new AnimaUnit
        {
            Id = "Warden",
            Color = AnimaColor.Onyx,
            BaseStats = stats,
            Head = intercept,
            Frame = bristle,
            Tail = disarm,
            Crest = vengeance,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }

    // Verdant Primitive 2 (Sustain). Named "Thicket" -- distinct from its own Tail skill "Bloom",
    // same Verdant base stats as Sprout, different kit/Archetype.
    public static AnimaUnit CreateThicket()
    {
        var stats = new Stats
        {
            MaxHp = 100,
            Defense = 10,
            Speed = 10,
            DamageMultiplier = 0.7,
            SpiritMultiplier = 1.3,
        };

        // Entire effect IS the HOT -- BaseHeal is left at 0, so ResolveHeal skips the instant-heal
        // branch and only applies/refreshes the Renew status. See CombatEngine.ApplyOrRefreshHot.
        var renew = new Skill
        {
            Name = "Renew",
            Part = Part.Head,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Heal,
            Target = TargetType.Ally,
            EnergyCost = 2,
            HotKeyword = "Renew",
            HotMagnitude = 13, // flat per-turn healing, not scaled by Spirit -- same convention as Bleed's flat per-turn damage
            HotDurationTurns = 3,
        };

        var healingRain = new Skill
        {
            Name = "Healing Rain",
            Part = Part.Frame,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Heal,
            Target = TargetType.AllAllies,
            EnergyCost = 2,
            BaseHeal = 20,
        };

        var bloom = new Skill
        {
            Name = "Bloom",
            Part = Part.Tail,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Attack,
            Range = AttackRange.Ranged,
            Target = TargetType.Enemy,
            EnergyCost = 1,
            BaseDamage = 8,
            RefreshesHotKeyword = "Renew", // support/combo piece: extends an ally's existing Renew, doesn't apply a fresh one -- same spirit as Exploit refreshing Bleed
            RefreshesHotDurationTurns = 3,
        };

        var providence = new Skill
        {
            Name = "Providence",
            Part = Part.Crest,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveConditional,
            // Always acts first in Initiative regardless of Speed -- a direct check in
            // InitiativePhase, not an event subscription, since it's turn-order logic rather
            // than a reaction. See CombatEngine.HasProvidence.
        };

        return new AnimaUnit
        {
            Id = "Thicket",
            Color = AnimaColor.Verdant,
            BaseStats = stats,
            Head = renew,
            Frame = healingRain,
            Tail = bloom,
            Crest = providence,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }

    // Verdant Primitive 3 (Cleansing). Named "Lotus" -- distinct from Sprout/Thicket, same
    // Verdant base stats, different kit/Archetype. Tail reuses Cleanse as-is from the Bastion
    // hybrid test (see CreateBastion) -- this is that skill's first real home on a pure-Verdant
    // Anima, now paired with its own Head/Frame/Crest instead of riding on an Onyx base.
    public static AnimaUnit CreateLotus()
    {
        var stats = new Stats
        {
            MaxHp = 100,
            Defense = 10,
            Speed = 10,
            DamageMultiplier = 0.7,
            SpiritMultiplier = 1.3,
        };

        var purge = new Skill
        {
            Name = "Purge",
            Part = Part.Head,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Attack,
            Range = AttackRange.NA,
            Target = TargetType.ChosenEnemy,
            EnergyCost = 2,
            BaseDamage = 8,
            RemovesBuff = true, // dispel -- the offensive mirror of Cleanse's RemovesDebuff; see CombatEngine.RemoveOneBuff
        };

        var silence = new Skill
        {
            Name = "Silence",
            Part = Part.Frame,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Attack,
            Range = AttackRange.NA,
            Target = TargetType.ChosenEnemy,
            EnergyCost = 1,
            BaseDamage = 7,
            // PvE stand-in for disabling the target's Frame ability: stuns the target, skipping
            // their next turn entirely -- same mechanism as Onyx's Disarm / Azure's Pin, just
            // paired with damage here instead of being a pure Debuff-category skill. See
            // CombatEngine.ApplyOnHitStatus / ConsumeStun.
            OnHitStatusKeyword = "Stunned",
            OnHitStatusDuration = DurationType.UntilConsumed,
        };

        var cleanse = new Skill
        {
            Name = "Cleanse",
            Part = Part.Tail,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Heal,
            Target = TargetType.LowestHpAlly,
            EnergyCost = 2,
            BaseHeal = 33,
            RemovesDebuff = true,
        };

        var clarity = new Skill
        {
            Name = "Clarity",
            Part = Part.Crest,
            Color = AnimaColor.Verdant,
            Category = SkillCategory.Passive,
            Target = TargetType.SelfTarget,
            Trigger = TriggerType.PassiveConditional,
            // -1 energy cost (min 0) on the skill this Anima plays each Round -- see
            // CombatEngine.GetEffectiveEnergyCost.
        };

        return new AnimaUnit
        {
            Id = "Lotus",
            Color = AnimaColor.Verdant,
            BaseStats = stats,
            Head = purge,
            Frame = silence,
            Tail = cleanse,
            Crest = clarity,
            CurrentHp = stats.MaxHp,
            Position = 1,
        };
    }
}
