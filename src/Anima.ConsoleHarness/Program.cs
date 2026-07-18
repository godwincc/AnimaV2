using Anima.Core.Data;
using Anima.Core.Enums;
using Anima.Core.Reforge;
// "Anima" (the model class) shares its name with the root "Anima" namespace, so a bare
// reference resolves to the namespace, not the type — alias it to sidestep the clash.
using AnimaUnit = Anima.Core.Models.Anima;

// ==== REFORGE MECHANIC — SANITY CHECK ====
// Rolls a base (all-color) offer, mock-Augments whichever slot it lands on (same in-place field
// mutation pattern the Warden augment tests use), accepts the offer onto the target, and checks:
// the slot's skill actually changed, the augmented instance was discarded (not just re-used),
// the pool's own candidate skill was never mutated (Clone() actually isolates instances), and a
// second roll with a chosen color is correctly narrowed to that color at the 80-Wisp price.

var results = new List<(string Check, bool Passed)>();
void Check(string description, bool passed) => results.Add((description, passed));

Console.WriteLine("================ Reforge: base roll (40 Wisp) ================");

var rng = new Random(7);
var baseOffer = ReforgeService.RollOffer(rng);
Console.WriteLine($"Rolled: {baseOffer.Candidate.ArchetypeName}'s \"{baseOffer.Candidate.Skill.Name}\" " +
    $"({baseOffer.Candidate.Skill.Part}, {baseOffer.Candidate.Skill.Color}). Accept cost: {baseOffer.AcceptCost} Wisp.");
Check("Base roll costs 40 Wisp", baseOffer.AcceptCost == ReforgeService.BaseAcceptCost);

var ember = SampleAnimas.CreateEmber();
var slotSkill = baseOffer.Candidate.Skill.Part switch
{
    Part.Head => ember.Head,
    Part.Frame => ember.Frame,
    Part.Tail => ember.Tail,
    _ => throw new InvalidOperationException(),
};

var poolBaselineDamage = baseOffer.Candidate.Skill.BaseDamage;
var beforeAugmentDamage = slotSkill.BaseDamage;
slotSkill.BaseDamage += 999; // stand-in Augment mutation
Console.WriteLine($"Augmented Ember's {slotSkill.Name} ({baseOffer.Candidate.Skill.Part}): BaseDamage {beforeAugmentDamage} -> {slotSkill.BaseDamage}.");

ReforgeService.Accept(baseOffer, ember);

var afterSkill = baseOffer.Candidate.Skill.Part switch
{
    Part.Head => ember.Head,
    Part.Frame => ember.Frame,
    Part.Tail => ember.Tail,
    _ => throw new InvalidOperationException(),
};
Console.WriteLine($"Ember's {baseOffer.Candidate.Skill.Part} is now \"{afterSkill.Name}\" (BaseDamage {afterSkill.BaseDamage}).");

Check("Part slot actually changed to the rolled skill", afterSkill.Name == baseOffer.Candidate.Skill.Name);
Check("Augmented instance was discarded, not reused", !ReferenceEquals(afterSkill, slotSkill));
Check("Augment is gone (BaseDamage matches pool baseline, not the +999 mutation)", afterSkill.BaseDamage == poolBaselineDamage);
Check("Pool's own candidate skill was never mutated (Clone() isolation)", baseOffer.Candidate.Skill.BaseDamage == poolBaselineDamage);

Console.WriteLine();
Console.WriteLine("================ Reforge: color-choice roll (80 Wisp) ================");

var colorOffer = ReforgeService.RollOffer(rng, AnimaColor.Verdant);
Console.WriteLine($"Rolled: {colorOffer.Candidate.ArchetypeName}'s \"{colorOffer.Candidate.Skill.Name}\" " +
    $"({colorOffer.Candidate.Skill.Part}, {colorOffer.Candidate.Skill.Color}). Accept cost: {colorOffer.AcceptCost} Wisp.");
Check("Color-choice roll costs 80 Wisp", colorOffer.AcceptCost == ReforgeService.ChooseColorAcceptCost);
Check("Color-choice roll is narrowed to the chosen color", colorOffer.Candidate.Skill.Color == AnimaColor.Verdant);

var sprout = SampleAnimas.CreateSprout();
var sproutBefore = colorOffer.Candidate.Skill.Part switch
{
    Part.Head => sprout.Head,
    Part.Frame => sprout.Frame,
    Part.Tail => sprout.Tail,
    _ => throw new InvalidOperationException(),
};
ReforgeService.Accept(colorOffer, sprout);
var sproutAfter = colorOffer.Candidate.Skill.Part switch
{
    Part.Head => sprout.Head,
    Part.Frame => sprout.Frame,
    Part.Tail => sprout.Tail,
    _ => throw new InvalidOperationException(),
};
Console.WriteLine($"Sprout's {colorOffer.Candidate.Skill.Part}: \"{sproutBefore.Name}\" -> \"{sproutAfter.Name}\".");
Check("Sprout's slot actually changed to the rolled cross-Archetype skill", sproutAfter.Name == colorOffer.Candidate.Skill.Name && !ReferenceEquals(sproutAfter, sproutBefore));

Console.WriteLine();
Console.WriteLine("Decline path: costs nothing and mutates nothing -- it's simply not calling Accept, so no separate check is needed here.");

Console.WriteLine();
Console.WriteLine("================ Results ================");
foreach (var (description, passed) in results)
{
    Console.WriteLine($"  [{(passed ? "PASS" : "FAIL")}] {description}");
}
Console.WriteLine(results.All(r => r.Passed) ? "ALL CHECKS PASSED" : "SOME CHECKS FAILED");
