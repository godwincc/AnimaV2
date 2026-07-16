using Anima.Core.Combat;
using Anima.Core.Data;
using Anima.Core.Models;
// "Anima" (the model class) shares its name with the root "Anima" namespace, so a bare
// reference resolves to the namespace, not the type — alias it to sidestep the clash.
using AnimaUnit = Anima.Core.Models.Anima;

// ---- Player team: the starter trio, one Primitive per color ----

var ember = SampleAnimas.CreateEmber();
ember.Position = 1;

var boulder = SampleAnimas.CreateBoulder();
boulder.Position = 2;

var sprout = SampleAnimas.CreateSprout();
sprout.Position = 3;

// ---- Opponent ----

var sentinel = SampleEnemies.CreateSentinel();

// ---- Combat setup ----

var state = new CombatState
{
    PlayerTeam = new List<AnimaUnit> { ember, boulder, sprout },
    EnemyTeam = new List<Enemy> { sentinel },
    // Full 27-card deck/draw system isn't built yet — hardcode the 9 unique deck skills
    // (Head/Frame/Tail x3 Animas; Crests are never drawn) into the hand.
    PlayerHand = new List<Skill>
    {
        ember.Head, ember.Frame, ember.Tail,
        boulder.Head, boulder.Frame, boulder.Tail,
        sprout.Head, sprout.Frame, sprout.Tail,
    },
};

var engine = new CombatEngine(state)
{
    OnLog = Console.WriteLine,
    ChoosePlayerSkill = (anima, combatState) => anima.Id switch
    {
        "Ember" => ChooseEmberSkill(combatState),
        "Boulder" => ChooseBoulderSkill(combatState),
        "Sprout" => ChooseSproutSkill(combatState),
        _ => null,
    },
};

// Safety valve: a prototype AI/priority combo can produce a genuine stalemate (e.g. a
// stacking Shield outpacing the remaining damage output) — cap rounds so the harness
// reports that instead of hanging forever.
const int maxRounds = 50;
while (state.RoundNumber <= maxRounds
    && state.PlayerTeam.Any(a => a.CurrentHp > 0)
    && state.EnemyTeam.Any(e => e.CurrentHp > 0))
{
    engine.RunRound();
    Console.WriteLine();
}

if (state.EnemyTeam.All(e => e.CurrentHp <= 0))
{
    Console.WriteLine("Winner: Player");
}
else if (state.PlayerTeam.All(a => a.CurrentHp <= 0))
{
    Console.WriteLine("Winner: Enemy");
}
else
{
    Console.WriteLine($"Stalemate — round cap ({maxRounds}) reached with both sides still standing.");
}

// ---- Skill-selection priorities (hardcoded, one per Anima) ----

// Always Slash if it's in hand and affordable, otherwise pass.
Skill? ChooseEmberSkill(CombatState combatState)
{
    var slash = combatState.PlayerHand.FirstOrDefault(s => s.Name == "Slash");
    return slash != null && combatState.SharedEnergy >= slash.EnergyCost ? slash : null;
}

// Taunt first if affordable, otherwise Bash if affordable, otherwise pass.
Skill? ChooseBoulderSkill(CombatState combatState)
{
    var taunt = combatState.PlayerHand.FirstOrDefault(s => s.Name == "Taunt");
    if (taunt != null && combatState.SharedEnergy >= taunt.EnergyCost) return taunt;

    var bash = combatState.PlayerHand.FirstOrDefault(s => s.Name == "Bash");
    if (bash != null && combatState.SharedEnergy >= bash.EnergyCost) return bash;

    return null;
}

// Heal the lowest-HP ally with Lifebloom if they're below 50% HP and it's affordable,
// otherwise Smite if affordable, otherwise pass.
Skill? ChooseSproutSkill(CombatState combatState)
{
    var lowestAlly = combatState.PlayerTeam
        .Where(a => a.CurrentHp > 0)
        .OrderBy(a => (double)a.CurrentHp / a.MaxHp)
        .First();

    if ((double)lowestAlly.CurrentHp / lowestAlly.MaxHp < 0.5)
    {
        var lifebloom = combatState.PlayerHand.FirstOrDefault(s => s.Name == "Lifebloom");
        if (lifebloom != null && combatState.SharedEnergy >= lifebloom.EnergyCost) return lifebloom;
    }

    var smite = combatState.PlayerHand.FirstOrDefault(s => s.Name == "Smite");
    if (smite != null && combatState.SharedEnergy >= smite.EnergyCost) return smite;

    return null;
}
