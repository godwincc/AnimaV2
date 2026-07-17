using Anima.Core.Combat;
using Anima.Core.Data;
using Anima.Core.Enums;
using Anima.Core.Models;
// "Anima" (the model class) shares its name with the root "Anima" namespace, so a bare
// reference resolves to the namespace, not the type — alias it to sidestep the clash.
using AnimaUnit = Anima.Core.Models.Anima;

// ==== SEMI-RUN TEST ====
// Validates the resource loop (earn Wisp/Ember from wins, spend Ember at a Shop step on
// Increase Effect augments) end-to-end for the first time, replacing the prior one-off test
// where augments were hand-edited directly onto SampleAnimas.cs. Fixed fight sequence, no
// node-map/branching logic yet — see the flagged placeholders in the summary at the end.

const int WispPerWin = 50;
const int EmberPerWinPerColor = 2; // one color-matched batch per color present on the team
const int AugmentEmberCost = 3;
const double AugmentMultiplier = 1.4; // matches the +~40% precedent from the earlier manual test
const int MaxRounds = 50;

var ember = SampleAnimas.CreateEmber();
var boulder = SampleAnimas.CreateBoulder();
var sprout = SampleAnimas.CreateSprout();
var playerTeam = new List<AnimaUnit> { ember, boulder, sprout };

// Placeholder Shop decision rule: each Anima has a fixed priority queue of "which skill
// benefits most" — its highest-usage Attack/Heal/Shield skill first, second-highest second.
// A real Shop would let the player choose; this stands in for that choice until one exists.
var augmentPriority = new Dictionary<string, Queue<string>>
{
    ["Ember"] = new Queue<string>(new[] { "Slash", "Execute" }),
    ["Boulder"] = new Queue<string>(new[] { "Bash", "Hardened" }),
    ["Sprout"] = new Queue<string>(new[] { "Smite", "Lifebloom" }),
};

var totalWisp = 0;
var emberBalance = new Dictionary<AnimaColor, int>
{
    [AnimaColor.Crimson] = 0,
    [AnimaColor.Onyx] = 0,
    [AnimaColor.Verdant] = 0,
};
var emberEarned = new Dictionary<AnimaColor, int>(emberBalance);
var augmentLog = new List<string>();

var fights = new (string Label, Func<Enemy> Factory)[]
{
    ("Grovehide", SampleEnemies.CreateGrovehide),
    ("Quillfang", SampleEnemies.CreateQuillfang),
    ("Grovehide (rematch)", SampleEnemies.CreateGrovehide),
    ("The Sentinel", SampleEnemies.CreateSentinel),
};

var runOutcome = "Run did not complete.";

for (var fightNumber = 1; fightNumber <= fights.Length; fightNumber++)
{
    var (label, factory) = fights[fightNumber - 1];
    Console.WriteLine();
    Console.WriteLine($"########## FIGHT {fightNumber}: {label} ##########");

    // Full reset between fights: HP/statuses/position restored, fresh deck/hand/energy each
    // fight. The same Anima instances (and any augments applied to their Skill objects) carry
    // over — see the "no HP persistence" note in the final summary for why this is a placeholder.
    ember.CurrentHp = ember.MaxHp; ember.ActiveStatuses.Clear(); ember.Position = 1;
    boulder.CurrentHp = boulder.MaxHp; boulder.ActiveStatuses.Clear(); boulder.Position = 2;
    sprout.CurrentHp = sprout.MaxHp; sprout.ActiveStatuses.Clear(); sprout.Position = 3;

    var enemy = factory();
    var state = new CombatState
    {
        PlayerTeam = playerTeam,
        EnemyTeam = new List<Enemy> { enemy },
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

        totalWisp += WispPerWin;
        foreach (var color in playerTeam.Select(a => a.Color).Distinct())
        {
            emberBalance[color] += EmberPerWinPerColor;
            emberEarned[color] += EmberPerWinPerColor;
        }
        Console.WriteLine($"Earned: {WispPerWin} Wisp, {EmberPerWinPerColor} Ember each of Crimson/Onyx/Verdant.");

        if (fightNumber == fights.Length)
        {
            runOutcome = $"Run complete: defeated {label} in Round {endedInRound}.";
        }
    }
    else if (playerAlive)
    {
        Console.WriteLine("Result: STALEMATE (round cap reached) — treating as a run failure.");
        runOutcome = $"Run halted: stalemate against {label} (fight {fightNumber}, round cap {MaxRounds}).";
        break;
    }
    else
    {
        Console.WriteLine("Result: LOSS");
        runOutcome = $"Run halted: lost to {label} (fight {fightNumber}, Round {endedInRound}).";
        break;
    }

    if (fightNumber % 2 == 0)
    {
        RunShop(fightNumber);
    }
}

Console.WriteLine();
Console.WriteLine("########## SEMI-RUN SUMMARY ##########");
Console.WriteLine($"Total Wisp earned: {totalWisp} (no Wisp sink exists in this v1 — see notes)");
foreach (var color in new[] { AnimaColor.Crimson, AnimaColor.Onyx, AnimaColor.Verdant })
{
    Console.WriteLine($"Total {color} Ember earned: {emberEarned[color]} (unspent remainder: {emberBalance[color]})");
}

Console.WriteLine();
Console.WriteLine("Augments applied:");
if (augmentLog.Count == 0)
{
    Console.WriteLine("  none");
}
foreach (var entry in augmentLog)
{
    Console.WriteLine($"  - {entry}");
}

Console.WriteLine();
Console.WriteLine(runOutcome);

// ---- Shop step (placeholder decision rule) ----

void RunShop(int fightNumber)
{
    Console.WriteLine();
    Console.WriteLine($"---- SHOP (after fight {fightNumber}) ----");
    Console.WriteLine($"Balance: {totalWisp} Wisp | Crimson {emberBalance[AnimaColor.Crimson]} | " +
        $"Onyx {emberBalance[AnimaColor.Onyx]} | Verdant {emberBalance[AnimaColor.Verdant]}");

    var purchased = false;
    foreach (var anima in playerTeam)
    {
        var queue = augmentPriority[anima.Id];
        if (queue.Count == 0)
        {
            continue;
        }

        if (emberBalance[anima.Color] < AugmentEmberCost)
        {
            Console.WriteLine($"  Not enough {anima.Color} Ember to augment {anima.Id} yet " +
                $"({emberBalance[anima.Color]}/{AugmentEmberCost}).");
            continue;
        }

        var skillName = queue.Dequeue();
        emberBalance[anima.Color] -= AugmentEmberCost;
        ApplyIncreaseEffectAugment(anima, skillName, fightNumber);
        purchased = true;
    }

    if (!purchased)
    {
        Console.WriteLine("  No purchases this Shop visit.");
    }
}

void ApplyIncreaseEffectAugment(AnimaUnit anima, string skillName, int fightNumber)
{
    var skill = anima.DeckSkills.First(s => s.Name == skillName);

    string field;
    int before, after;
    if (skill.BaseDamage > 0)
    {
        before = skill.BaseDamage;
        skill.BaseDamage = (int)Math.Round(skill.BaseDamage * AugmentMultiplier);
        after = skill.BaseDamage;
        field = "damage";
    }
    else if (skill.BaseHeal > 0)
    {
        before = skill.BaseHeal;
        skill.BaseHeal = (int)Math.Round(skill.BaseHeal * AugmentMultiplier);
        after = skill.BaseHeal;
        field = "heal";
    }
    else
    {
        before = skill.BaseShield;
        skill.BaseShield = (int)Math.Round(skill.BaseShield * AugmentMultiplier);
        after = skill.BaseShield;
        field = "shield";
    }

    var entry = $"After fight {fightNumber}: Increase Effect on {anima.Id}'s {skillName} " +
        $"({field} {before} -> {after}), spent {AugmentEmberCost} {anima.Color} Ember.";
    augmentLog.Add(entry);
    Console.WriteLine($"  [PURCHASE] {entry}");
}

// ---- Skill-selection priorities (hardcoded, one per Anima; same across every fight) ----

// Prefer Slash; if it's not in hand, fall back through the rest of the kit (damage before
// pure utility) rather than passing just because the single favorite card wasn't drawn.
Skill? ChooseEmberSkill(CombatState combatState) =>
    ChooseFromPriority(combatState, "Slash", "Execute", "Charge");

// Proactively Taunt (self-Mark) whenever nobody on the player team is currently Marked --
// it doesn't expire on its own (Until-Consumed), so recasting while one's already active
// would just waste energy re-marking the same redirect. Otherwise Bash. Either way, fall
// back through the rest of the kit before passing.
Skill? ChooseBoulderSkill(CombatState combatState)
{
    var alreadyMarked = combatState.PlayerTeam.Any(a => a.CurrentHp > 0 && a.ActiveStatuses.Any(s => s.Keyword == "Marked"));
    return !alreadyMarked
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
