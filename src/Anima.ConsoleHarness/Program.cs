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

engine.StartCombat();

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

// Prefer Slash; if it's not in hand, fall back through the rest of the kit (damage before
// pure utility) rather than passing just because the single favorite card wasn't drawn.
Skill? ChooseEmberSkill(CombatState combatState) =>
    ChooseFromPriority(combatState, "Slash", "Execute", "Charge");

// Bash whenever the target isn't already Weak (so the debuff actually gets applied/refreshed),
// otherwise Taunt. Either way, fall back through the rest of the kit before passing.
Skill? ChooseBoulderSkill(CombatState combatState)
{
    var targetIsWeak = combatState.EnemyTeam.Any(e => e.CurrentHp > 0 && e.ActiveStatuses.Any(s => s.Keyword == "Weak"));
    return targetIsWeak
        ? ChooseFromPriority(combatState, "Taunt", "Bash", "Hardened")
        : ChooseFromPriority(combatState, "Bash", "Taunt", "Hardened");
}

// Heal the lowest-HP ally with Lifebloom if they're below 50% HP, otherwise Smite. Either way,
// fall back through the rest of the kit before passing.
Skill? ChooseSproutSkill(CombatState combatState)
{
    var lowestAlly = combatState.PlayerTeam
        .Where(a => a.CurrentHp > 0)
        .OrderBy(a => (double)a.CurrentHp / a.MaxHp)
        .First();

    return (double)lowestAlly.CurrentHp / lowestAlly.MaxHp < 0.5
        ? ChooseFromPriority(combatState, "Lifebloom", "Smite", "Guiding Light")
        : ChooseFromPriority(combatState, "Smite", "Lifebloom", "Guiding Light");
}

// Shared fallback: play the first listed skill that's both in Hand and affordable. Callers
// order their list damage/heal skills first, pure-utility last, so a drawn hand actually gets
// used instead of passing just because the single most-preferred card wasn't drawn this turn.
Skill? ChooseFromPriority(CombatState combatState, params string[] priority)
{
    foreach (var name in priority)
    {
        var skill = combatState.Hand.FirstOrDefault(s => s.Name == name);
        if (skill != null && combatState.SharedEnergy >= skill.EnergyCost) return skill;
    }
    return null;
}
