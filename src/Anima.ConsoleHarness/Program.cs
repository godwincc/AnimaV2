using Anima.Core.Combat;
using Anima.Core.Data;
using Anima.Core.Enums;
using Anima.Core.Models;
// "Anima" (the model class) shares its name with the root "Anima" namespace, so a bare
// reference resolves to the namespace, not the type — alias it to sidestep the clash.
using AnimaUnit = Anima.Core.Models.Anima;

// ---- Player Anima: Crimson, Primitive 1 (Burst Combo) kit ----

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

var playerAnima = new AnimaUnit
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

// ---- Opponent ----

var quillfang = SampleEnemies.CreateQuillfang();

// ---- Combat setup ----

var state = new CombatState
{
    PlayerTeam = new List<AnimaUnit> { playerAnima },
    EnemyTeam = new List<Enemy> { quillfang },
    // Full 27-card deck/draw system isn't built yet — hardcode the 3 unique deck skills into the hand.
    PlayerHand = new List<Skill> { slash, charge, execute },
};

var engine = new CombatEngine(state)
{
    OnLog = Console.WriteLine,
    ChoosePlayerSkill = (anima, combatState) =>
    {
        // Hardcoded priority for now: always Slash if it's in hand and affordable, otherwise pass.
        var candidate = combatState.PlayerHand.FirstOrDefault(s => s.Name == "Slash");
        return candidate != null && combatState.SharedEnergy >= candidate.EnergyCost ? candidate : null;
    },
};

while (state.PlayerTeam.Any(a => a.CurrentHp > 0) && state.EnemyTeam.Any(e => e.CurrentHp > 0))
{
    engine.RunRound();
    Console.WriteLine();
}

var winner = state.PlayerTeam.Any(a => a.CurrentHp > 0) ? "Player" : "Enemy";
Console.WriteLine($"Winner: {winner}");
