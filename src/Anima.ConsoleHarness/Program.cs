using Anima.Core.Combat;
using Anima.Core.Data;
using Anima.Core.Models;
// "Anima" (the model class) shares its name with the root "Anima" namespace, so a bare
// reference resolves to the namespace, not the type — alias it to sidestep the clash.
using AnimaUnit = Anima.Core.Models.Anima;

// ==== WARDEN OF THE HOLLOW — OPTIMIZED-BUILD TEST ====
// Five consecutive tuning passes on Warden's own stats (EnrageRound 20->23->25, summon cadence
// 5->7, Defense 11->9) never got the starter trio (Ember/Boulder/Sprout) a single win -- the
// augmented batch's best margin plateaued around 55-65/220 HP regardless of which lever moved.
// Swapping in a genuinely optimized team instead (Boulder out, Shade in for real Misdirect
// access against the guard, stacked augments) got the first wins of this whole test arc, but a
// same-build confirmation batch swung to 0/5 -- combined record 3W-7L (30%), a real coinflip
// with near-misses on both sides (41 HP and 1 HP losses). Warden's stats are now LOCKED (Defense
// 11, Broodling heal 8, EnrageRound 20, summon cadence 5) -- no more changes to her. This test
// adds a THIRD Increase Effect stack on Execute (one more than the winning build had) to see if
// that small extra investment pushes the coinflip toward a favored-but-contested win rate.

const int MaxRounds = 50;
const int RunCount = 5;
const double AugmentMultiplier = 1.4; // Increase Effect, same precedent as every prior augment test

Console.WriteLine("================ Optimized build: Ember + Shade + Sprout vs. Warden of the Hollow ================");

for (var runNumber = 1; runNumber <= RunCount; runNumber++)
{
    Console.WriteLine();
    Console.WriteLine($"########## RUN {runNumber} ##########");

    var ember = SampleAnimas.CreateEmber();
    var shade = SampleAnimas.CreateShade();
    var sprout = SampleAnimas.CreateSprout();
    ember.Position = 1;
    shade.Position = 2;
    sprout.Position = 3;
    var playerTeam = new List<AnimaUnit> { ember, shade, sprout };

    // Increase Effect on Execute, stacked three times: 40 -> 56 -> 78 -> 109.
    ApplyIncreaseEffect(ember, "Execute");
    ApplyIncreaseEffect(ember, "Execute");
    ApplyIncreaseEffect(ember, "Execute");
    // Increase Effect on Slash, once: 25 -> 35.
    ApplyIncreaseEffect(ember, "Slash");
    // Decrease Cost on Misdirect: 2 energy -> 1, so Shade can afford to recast it every Round
    // (it only needs to survive until Ember's own attack consumes it via the Marked redirect --
    // see ChooseShadeSkill below -- so cheap-and-recastable beats a one-shot application).
    ApplyDecreaseCost(shade, "Misdirect");

    var warden = SampleEnemies.CreateWardenOfTheHollow();
    var state = new CombatState
    {
        PlayerTeam = playerTeam,
        EnemyTeam = new List<Enemy> { warden },
    };

    var engine = new CombatEngine(state)
    {
        OnLog = Console.WriteLine,
        ChoosePlayerSkill = (anima, combatState) => anima.Id switch
        {
            "Ember" => ChooseEmberSkill(combatState),
            "Shade" => ChooseShadeSkill(combatState),
            "Sprout" => ChooseSproutSkill(combatState),
            _ => null,
        },
    };

    engine.StartCombat();

    while (state.RoundNumber <= MaxRounds
        && state.PlayerTeam.Any(a => a.CurrentHp > 0)
        && state.EnemyTeam.Any(e => e.CurrentHp > 0))
    {
        engine.RunRound();
        Console.WriteLine();
    }

    var playerWon = state.EnemyTeam.All(e => e.CurrentHp <= 0);
    var playerAlive = state.PlayerTeam.Any(a => a.CurrentHp > 0);
    var endedInRound = state.RoundNumber - 1; // RoundEndPhase increments before the loop re-checks

    if (playerWon)
    {
        Console.WriteLine($"Result: WIN (Round {endedInRound})");
    }
    else if (playerAlive)
    {
        Console.WriteLine($"Result: STALEMATE (round cap {MaxRounds} reached)");
    }
    else
    {
        Console.WriteLine($"Result: LOSS (Round {endedInRound})");
    }
}

// ---- Augments (applied once per run, before StartCombat, directly on the fresh Skill instances) ----

void ApplyIncreaseEffect(AnimaUnit anima, string skillName)
{
    var skill = anima.DeckSkills.First(s => s.Name == skillName);
    var before = skill.BaseDamage;
    skill.BaseDamage = (int)Math.Round(skill.BaseDamage * AugmentMultiplier);
    Console.WriteLine($"  [AUGMENT] Increase Effect on {anima.Id}'s {skillName}: damage {before} -> {skill.BaseDamage}.");
}

void ApplyDecreaseCost(AnimaUnit anima, string skillName)
{
    var skill = anima.DeckSkills.First(s => s.Name == skillName);
    var before = skill.EnergyCost;
    skill.EnergyCost = Math.Max(0, skill.EnergyCost - 1);
    Console.WriteLine($"  [AUGMENT] Decrease Cost on {anima.Id}'s {skillName}: energy {before} -> {skill.EnergyCost}.");
}

// ---- Skill-selection priorities (hardcoded, one per Anima; same across every run) ----

// Prefer Slash; if it's not in hand, fall back through the rest of the kit (damage before
// pure utility) rather than passing just because the single favorite card wasn't drawn.
Skill? ChooseEmberSkill(CombatState combatState) =>
    ChooseFromPriority(combatState, "Slash", "Execute", "Charge");

// Always lead with Misdirect (now cheap post-Decrease-Cost) rather than only when Warden isn't
// already Marked: SelectTarget's ChosenEnemy fallback resolves to the first living entry in
// EnemyTeam, which is always Warden herself (she's added to the list before any add is ever
// summoned) -- so Misdirect reliably Marks HER specifically, not whichever add is in front. Marked
// then gets consumed by the very next Enemy-type attack (Ember's Slash, here) that Round, so it
// needs re-casting every Round rather than only once -- Shade's Speed (13, fastest on the team)
// guarantees she acts before Ember each Round, so the Mark is always fresh when Slash resolves.
Skill? ChooseShadeSkill(CombatState combatState) =>
    ChooseFromPriority(combatState, "Misdirect", "Exploit", "Pin");

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
