using Anima.Core.Combat;
using Anima.Core.Data;
using Anima.Core.Economy;
using Anima.Core.Enums;
using Anima.Core.Map;
using Anima.Core.Models;
using Anima.Core.Reforge;
using Anima.Core.Weaving;
using AnimaUnit = Anima.Core.Models.Anima;

// ==== WEAVING (BREEDING) — DISTRIBUTION VALIDATION ====
// Two checks, both statistical (many trials, tolerance bands around the design's stated
// probabilities rather than exact-match, since these are genuine random rolls):
//
// 1. Dominant-source distribution: Ember x Reaper (both fully-pure Crimson founders), 1000
//    Weaves. Every resolved part's Dominant draw is pooled together (4 slots x 1000 trials =
//    4000 draws) and bucketed by which of the 6 weighted candidates won -- should land near
//    37.5/9.375/3.125% per parent. Dominant can never mutate, so there's no Mutation bucket here;
//    R1/R2's non-mutated source distribution is shown separately, informationally only, since
//    it's a conditional (sampling-without-replacement) distribution with no simple closed-form
//    expected percentage to assert against -- only its ~10% Mutation rate has one.
// 2. Hybrid trigger rate: Boulder x Ember (fully-pure Onyx x Crimson, a locked hybrid pairing),
//    1000 Weaves, checking how often the offspring's color comes back Vulcan -- should land near
//    the locked 33% trigger chance.

const int Trials = 1000;

bool WithinTolerance(int observedCount, double expectedP, int n, double sigma = 4.0)
{
    var expected = expectedP * n;
    var stdDev = Math.Sqrt(n * expectedP * (1 - expectedP));
    return Math.Abs(observedCount - expected) <= sigma * stdDev;
}

void ReportBucket(string label, int observedCount, int totalDraws, double expectedP)
{
    var observedP = (double)observedCount / totalDraws;
    var pass = WithinTolerance(observedCount, expectedP, totalDraws);
    Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {label,-16} observed {observedP:P2}  (expected {expectedP:P3}, n={totalDraws})");
}

Console.WriteLine("================ Weaving: Dominant-source distribution (Ember x Reaper, Crimson) ================");

var ember = SampleAnimas.CreateEmber();
var reaper = SampleAnimas.CreateReaper();
var emberGenome = GenomeFactory.CreateFounderGenome(ember);
var reaperGenome = GenomeFactory.CreateFounderGenome(reaper);
Console.WriteLine($"Parent A ({ember.Id}) fully pure: {emberGenome.IsFullyPure}. Parent B ({reaper.Id}) fully pure: {reaperGenome.IsFullyPure}.");

var rng = new Random(42);
var dominantCounts = new Dictionary<GeneSource, int>();
var hiddenCounts = new Dictionary<GeneSource, int>(); // R1 + R2 combined, includes Mutation

foreach (var source in Enum.GetValues<GeneSource>())
{
    dominantCounts[source] = 0;
    hiddenCounts[source] = 0;
}

for (var i = 0; i < Trials; i++)
{
    var result = WeavingService.Weave(emberGenome, reaperGenome, rng);
    foreach (var part in result.PartResolutions)
    {
        dominantCounts[part.DominantSource]++;
        hiddenCounts[part.R1Source]++;
        hiddenCounts[part.R2Source]++;
    }
}

var dominantTotal = dominantCounts.Values.Sum();
Console.WriteLine();
Console.WriteLine($"Dominant-source distribution (n={dominantTotal} draws = 4 slots x {Trials} trials):");
ReportBucket("ParentADominant", dominantCounts[GeneSource.ParentADominant], dominantTotal, 0.375);
ReportBucket("ParentAR1", dominantCounts[GeneSource.ParentAR1], dominantTotal, 0.09375);
ReportBucket("ParentAR2", dominantCounts[GeneSource.ParentAR2], dominantTotal, 0.03125);
ReportBucket("ParentBDominant", dominantCounts[GeneSource.ParentBDominant], dominantTotal, 0.375);
ReportBucket("ParentBR1", dominantCounts[GeneSource.ParentBR1], dominantTotal, 0.09375);
ReportBucket("ParentBR2", dominantCounts[GeneSource.ParentBR2], dominantTotal, 0.03125);
var dominantMutations = dominantCounts[GeneSource.Mutation];
Console.WriteLine($"  [{(dominantMutations == 0 ? "PASS" : "FAIL")}] Mutation          observed {(double)dominantMutations / dominantTotal:P2}  (expected 0.000%, n={dominantTotal}) -- Dominant must never mutate");

var hiddenTotal = hiddenCounts.Values.Sum();
Console.WriteLine();
Console.WriteLine($"R1/R2 (hidden gene) mutation rate (n={hiddenTotal} draws = 4 slots x 2 hidden genes x {Trials} trials):");
ReportBucket("Mutation", hiddenCounts[GeneSource.Mutation], hiddenTotal, 0.10);
Console.WriteLine();
Console.WriteLine("R1/R2 non-mutated source breakdown (informational only -- conditional on Dominant's slot");
Console.WriteLine("already being removed from the pool, so this is NOT expected to match 37.5/9.375/3.125):");
foreach (var source in new[] { GeneSource.ParentADominant, GeneSource.ParentAR1, GeneSource.ParentAR2, GeneSource.ParentBDominant, GeneSource.ParentBR1, GeneSource.ParentBR2 })
{
    Console.WriteLine($"      {source,-16} observed {(double)hiddenCounts[source] / hiddenTotal:P2}");
}

Console.WriteLine();
Console.WriteLine("================ Weaving: Hybrid trigger rate (Boulder x Ember, Onyx x Crimson) ================");

var boulder = SampleAnimas.CreateBoulder();
var boulderGenome = GenomeFactory.CreateFounderGenome(boulder);
Console.WriteLine($"Parent A ({boulder.Id}) fully pure: {boulderGenome.IsFullyPure}. Parent B ({ember.Id}) fully pure: {emberGenome.IsFullyPure}.");

var hybridCount = 0;
var vulcanColorCount = 0;
for (var i = 0; i < Trials; i++)
{
    var result = WeavingService.Weave(boulderGenome, emberGenome, rng);
    if (result.HybridTriggered) hybridCount++;
    if (result.Genome.Color == AnimaColor.Vulcan) vulcanColorCount++;
}

Console.WriteLine();
Console.WriteLine($"Hybrid trigger rate (n={Trials} Weaves):");
ReportBucket("HybridTriggered", hybridCount, Trials, 0.33);
Console.WriteLine($"  (sanity: HybridTriggered count == Color==Vulcan count: {hybridCount == vulcanColorCount})");

Console.WriteLine();
Console.WriteLine("Note: hybrid part composition currently reuses the identical normal weighted-roll logic");
Console.WriteLine("(just tagged with Vulcan/Mirage's own locked stats) -- a deliberate placeholder since");
Console.WriteLine("hybrid-specific breeding/part behavior isn't designed yet. Flagging per the brief.");

// ==== WEAVING — SIBLING RESTRICTION, WEAVE COUNT, AND ECHO ====
// Six checks: two deterministic lineage rejections, one WeaveCount-exhaustion rejection, one
// exact-match cost-curve check, and two statistical Echo checks (spontaneous rate + forceEcho).
// A dedicated Random (seeded, independent of the `rng` used above) drives this whole section.

Console.WriteLine();
Console.WriteLine("================ Weaving: Sibling Restriction, Weave Count, and Echo ================");

var weaveRng = new Random(2026);

// Richly-funded so none of the lineage/WeaveCount/cost/Echo checks below are incidentally blocked
// by affordability -- the dedicated ECONOMY section further down tests affordability itself.
var ledger = new PersistentLedger();
ledger.Add(ResourceType.Wisp, 2_000_000);

// ---- Sibling restriction: direct parent-child rejection ----
var lineageParent = SampleAnimas.CreateEmber();
lineageParent.Id = "LineageParent";
var lineageChild = SampleAnimas.CreateReaper();
lineageChild.Id = "LineageChild";
lineageChild.ParentAId = lineageParent.Id;
lineageChild.ParentBId = "SomeOtherParent";

var parentChildResult = WeavingService.AttemptWeave(
    lineageParent, lineageChild,
    GenomeFactory.CreateFounderGenome(SampleAnimas.CreateEmber()),
    GenomeFactory.CreateFounderGenome(SampleAnimas.CreateReaper()),
    ledger, weaveRng);
var parentChildPass = !parentChildResult.Success && parentChildResult.RejectionReason == WeaveRejectionReason.DirectParentChild;
Console.WriteLine($"  [{(parentChildPass ? "PASS" : "FAIL")}] Direct parent-child rejected (reason: {parentChildResult.RejectionReason})");

// ---- Sibling restriction: full-sibling rejection, parent order swapped ----
var siblingA = SampleAnimas.CreateSprout();
siblingA.Id = "SiblingA";
siblingA.ParentAId = "Founder1";
siblingA.ParentBId = "Founder2";
var siblingB = SampleAnimas.CreateShade();
siblingB.Id = "SiblingB";
siblingB.ParentAId = "Founder2"; // swapped vs SiblingA -- still the same unordered pair
siblingB.ParentBId = "Founder1";

var siblingResult = WeavingService.AttemptWeave(
    siblingA, siblingB,
    GenomeFactory.CreateFounderGenome(SampleAnimas.CreateSprout()),
    GenomeFactory.CreateFounderGenome(SampleAnimas.CreateShade()),
    ledger, weaveRng);
var siblingPass = !siblingResult.Success && siblingResult.RejectionReason == WeaveRejectionReason.FullSiblings;
Console.WriteLine($"  [{(siblingPass ? "PASS" : "FAIL")}] Full siblings rejected, order-independent (reason: {siblingResult.RejectionReason})");

// ---- Weave Count: WeaveCount-5 parent rejected before anything rolls ----
var exhaustedParent = SampleAnimas.CreateBoulder();
exhaustedParent.Id = "ExhaustedParent";
exhaustedParent.WeaveCount = 5;
var freshParent = SampleAnimas.CreateEmber();
freshParent.Id = "FreshParent";

var exhaustedResult = WeavingService.AttemptWeave(
    exhaustedParent, freshParent,
    GenomeFactory.CreateFounderGenome(SampleAnimas.CreateBoulder()),
    GenomeFactory.CreateFounderGenome(SampleAnimas.CreateEmber()),
    ledger, weaveRng);
var exhaustedPass = !exhaustedResult.Success && exhaustedResult.RejectionReason == WeaveRejectionReason.WeaveCountExhausted;
Console.WriteLine($"  [{(exhaustedPass ? "PASS" : "FAIL")}] WeaveCount-5 parent rejected (reason: {exhaustedResult.RejectionReason})");

// ---- Wisp cost: matches the locked 50/100/175/275/400 curve ----
Console.WriteLine();
var expectedCurve = new[] { 50, 100, 175, 275, 400 };
var curvePass = true;
for (var i = 0; i < expectedCurve.Length; i++)
{
    var actual = WeavingService.GetWeaveCost(i);
    if (actual != expectedCurve[i]) curvePass = false;
    Console.WriteLine($"    WeaveCount {i} -> cost {actual} (expected {expectedCurve[i]})");
}
Console.WriteLine($"  [{(curvePass ? "PASS" : "FAIL")}] Wisp cost curve matches 50/100/175/275/400");

var costParentA = SampleAnimas.CreateEmber();
costParentA.Id = "CostParentA";
costParentA.WeaveCount = 2; // going into their 3rd use -> 175
var costParentB = SampleAnimas.CreateReaper();
costParentB.Id = "CostParentB";
costParentB.WeaveCount = 4; // going into their 5th (last) use -> 400

var costResult = WeavingService.AttemptWeave(
    costParentA, costParentB,
    GenomeFactory.CreateFounderGenome(SampleAnimas.CreateEmber()),
    GenomeFactory.CreateFounderGenome(SampleAnimas.CreateReaper()),
    ledger, weaveRng);
var costPass = costResult.Success && costResult.WispCost == 175 + 400
    && costParentA.WeaveCount == 3 && costParentB.WeaveCount == 5;
Console.WriteLine($"  [{(costPass ? "PASS" : "FAIL")}] Weave at counts (2,4) costs 175+400=575 Wisp; both parents' WeaveCount incremented (A={costParentA.WeaveCount}, B={costParentB.WeaveCount})");

// ---- Echo: spontaneous ~5% rate ----
// Ember(Crimson) x Sprout(Verdant) is not one of the two locked hybrid pairings, so
// HybridTriggered is always false for this pair -- it can never gate the Echo roll off, isolating
// the observed rate to the Echo roll alone. WeaveCount is reset to 0 before every trial so the
// 5-use cap never interferes with a large statistical sample.
Console.WriteLine();
const int EchoTrials = 8000;
var echoParentA = SampleAnimas.CreateEmber();
echoParentA.Id = "EchoParentA";
var echoParentB = SampleAnimas.CreateSprout();
echoParentB.Id = "EchoParentB";
var echoGenomeA = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateEmber());
var echoGenomeB = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateSprout());

var spontaneousEchoCount = 0;
var anyHybridTriggeredForEchoPair = false;
for (var i = 0; i < EchoTrials; i++)
{
    echoParentA.WeaveCount = 0;
    echoParentB.WeaveCount = 0;
    var result = WeavingService.AttemptWeave(echoParentA, echoParentB, echoGenomeA, echoGenomeB, ledger, weaveRng);
    if (result.Primary!.HybridTriggered) anyHybridTriggeredForEchoPair = true;
    if (result.EchoTriggered) spontaneousEchoCount++;
}
Console.WriteLine($"  (sanity: Ember x Sprout never hybrid-triggers, so it never gates Echo off: {!anyHybridTriggeredForEchoPair})");
ReportBucket("Spontaneous Echo", spontaneousEchoCount, EchoTrials, 0.05);

// ---- Echo: forceEcho always triggers, with a Twin every time ----
const int ForceEchoTrials = 200;
var forceEchoSuccessCount = 0;
for (var i = 0; i < ForceEchoTrials; i++)
{
    echoParentA.WeaveCount = 0;
    echoParentB.WeaveCount = 0;
    var result = WeavingService.AttemptWeave(echoParentA, echoParentB, echoGenomeA, echoGenomeB, ledger, weaveRng, forceEcho: true);
    if (result.EchoTriggered && result.Twin is not null) forceEchoSuccessCount++;
}
var forceEchoPass = forceEchoSuccessCount == ForceEchoTrials;
Console.WriteLine($"  [{(forceEchoPass ? "PASS" : "FAIL")}] forceEcho triggers Echo with a Twin every time ({forceEchoSuccessCount}/{ForceEchoTrials})");

// ---- Echo: the two twins from one attempt can genuinely differ ----
// Not asserting they always MUST differ (that's not the design) -- just confirming the two rolls
// are truly independent, by finding at least one trial (out of a modest sample) where they landed
// on a different resolved genome.
string GenomeSignature(WeavingResult result) =>
    result.Genome.Color + "|" + string.Join(",", result.PartResolutions.Select(p => p.Dominant.Name));

const int TwinDifferenceTrials = 30;
var sawTwinDifference = false;
for (var i = 0; i < TwinDifferenceTrials; i++)
{
    echoParentA.WeaveCount = 0;
    echoParentB.WeaveCount = 0;
    var result = WeavingService.AttemptWeave(echoParentA, echoParentB, echoGenomeA, echoGenomeB, ledger, weaveRng, forceEcho: true);
    if (GenomeSignature(result.Primary!) != GenomeSignature(result.Twin!))
    {
        sawTwinDifference = true;
        break;
    }
}
Console.WriteLine($"  [{(sawTwinDifference ? "PASS" : "FAIL")}] Two Echo twins from the same parents can come out different (observed within {TwinDifferenceTrials} trials)");

// ==== WEAVING — FRESH VESSELS ARE UNNAMED (transitional, until the naming-prompt UI exists) ====
// Two checks: a normal Weave's Primary (and, forced via Echo, its Twin too) both come out with
// Name == null immediately after creation; the pre-made starter trio's own names (SampleAnimas.cs's
// hardcoded Id values -- there's no separate Name field on Anima itself, Id already doubles as the
// permanent display name for founders) are completely unaffected, since Weaving never touches them.

Console.WriteLine();
Console.WriteLine("================ Weaving: fresh Vessels are unnamed ================");

var namingParentA = SampleAnimas.CreateEmber();
namingParentA.Id = "NamingParentA";
var namingParentB = SampleAnimas.CreateReaper();
namingParentB.Id = "NamingParentB";
var namingGenomeA = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateEmber());
var namingGenomeB = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateReaper());
var namingLedger = new PersistentLedger();
namingLedger.Add(ResourceType.Wisp, 1000);

var normalWeaveResult = WeavingService.AttemptWeave(namingParentA, namingParentB, namingGenomeA, namingGenomeB, namingLedger, weaveRng);
var normalWeaveUnnamedPass = normalWeaveResult.Success && normalWeaveResult.Primary!.Name == null;
Console.WriteLine($"  [{(normalWeaveUnnamedPass ? "PASS" : "FAIL")}] A normal Weave's Primary Vessel has no Name immediately after creation (Name: {normalWeaveResult.Primary!.Name ?? "null"})");

var echoTwinResult = WeavingService.AttemptWeave(namingParentA, namingParentB, namingGenomeA, namingGenomeB, namingLedger, weaveRng, forceEcho: true);
var echoTwinUnnamedPass = echoTwinResult.Success && echoTwinResult.EchoTriggered
    && echoTwinResult.Primary!.Name == null && echoTwinResult.Twin!.Name == null;
Console.WriteLine($"  [{(echoTwinUnnamedPass ? "PASS" : "FAIL")}] An Echo Twin Vessel also has no Name immediately after creation, same as the Primary (Primary Name: {echoTwinResult.Primary!.Name ?? "null"}, Twin Name: {echoTwinResult.Twin!.Name ?? "null"})");

var starterTrioNamesPass = SampleAnimas.CreateEmber().Id == "Ember"
    && SampleAnimas.CreateBoulder().Id == "Boulder"
    && SampleAnimas.CreateSprout().Id == "Sprout";
Console.WriteLine($"  [{(starterTrioNamesPass ? "PASS" : "FAIL")}] The starter trio's names remain exactly as hardcoded in SampleAnimas.cs (Ember/Boulder/Sprout), unaffected by Weaving");

// ==== ECONOMY — PersistentLedger wired into Weaving and Reforge ====
// Five checks: successful Weave deducts exactly its Wisp cost, an underfunded Weave is rejected
// before rolling anything (and before touching the ledger or either parent's WeaveCount), Echo
// Shard spending works the same afford-then-commit way (success spends all 5, then a second
// attempt with 0 remaining is rejected), and the same two Wisp checks (successful deduction +
// underfunded rejection) for Reforge's Accept.

Console.WriteLine();
Console.WriteLine("================ Economy: Wisp / Echo Shard ledger wiring ================");

// ---- Weave: successful attempt deducts exactly its Wisp cost ----
var wispDeductParentA = SampleAnimas.CreateEmber();
wispDeductParentA.Id = "WispDeductParentA";
var wispDeductParentB = SampleAnimas.CreateReaper();
wispDeductParentB.Id = "WispDeductParentB";
var wispDeductGenomeA = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateEmber());
var wispDeductGenomeB = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateReaper());

var wispDeductLedger = new PersistentLedger();
wispDeductLedger.Add(ResourceType.Wisp, 100); // exactly covers 50 (count 0) + 50 (count 0)

var wispDeductResult = WeavingService.AttemptWeave(
    wispDeductParentA, wispDeductParentB, wispDeductGenomeA, wispDeductGenomeB, wispDeductLedger, weaveRng);
var wispDeductPass = wispDeductResult.Success && wispDeductResult.WispCost == 100
    && wispDeductLedger.GetBalance(ResourceType.Wisp) == 0;
Console.WriteLine($"  [{(wispDeductPass ? "PASS" : "FAIL")}] Successful Weave deducts its exact Wisp cost (100 -> {wispDeductLedger.GetBalance(ResourceType.Wisp)})");

// ---- Weave: insufficient Wisp rejects before rolling anything ----
var poorParentA = SampleAnimas.CreateBoulder();
poorParentA.Id = "PoorParentA";
var poorParentB = SampleAnimas.CreateSprout();
poorParentB.Id = "PoorParentB";
var poorGenomeA = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateBoulder());
var poorGenomeB = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateSprout());

var poorLedger = new PersistentLedger();
poorLedger.Add(ResourceType.Wisp, 50); // needs 100 (50+50), short by 50

var poorResult = WeavingService.AttemptWeave(poorParentA, poorParentB, poorGenomeA, poorGenomeB, poorLedger, weaveRng);
var poorPass = !poorResult.Success && poorResult.RejectionReason == WeaveRejectionReason.InsufficientWisp
    && poorLedger.GetBalance(ResourceType.Wisp) == 50 // untouched
    && poorParentA.WeaveCount == 0 && poorParentB.WeaveCount == 0; // nothing rolled/incremented
Console.WriteLine($"  [{(poorPass ? "PASS" : "FAIL")}] Insufficient Wisp rejects the Weave before rolling or charging anything (reason: {poorResult.RejectionReason})");

// ---- Weave: spending Echo Shards works the same afford-then-commit way ----
var shardParentA = SampleAnimas.CreateShade();
shardParentA.Id = "ShardParentA";
var shardParentB = SampleAnimas.CreateAnchor();
shardParentB.Id = "ShardParentB";
var shardGenomeA = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateShade());
var shardGenomeB = GenomeFactory.CreateFounderGenome(SampleAnimas.CreateAnchor());

var shardLedger = new PersistentLedger();
shardLedger.Add(ResourceType.Wisp, 1000); // plenty, isolates the Echo Shard check
shardLedger.Add(ResourceType.EchoShard, WeavingService.EchoShardCost); // exactly 5

var shardSpendResult = WeavingService.AttemptWeave(
    shardParentA, shardParentB, shardGenomeA, shardGenomeB, shardLedger, weaveRng, spendEchoShards: true);
var shardSpendPass = shardSpendResult.Success && shardSpendResult.EchoTriggered && shardSpendResult.Twin is not null
    && shardLedger.GetBalance(ResourceType.EchoShard) == 0;
Console.WriteLine($"  [{(shardSpendPass ? "PASS" : "FAIL")}] Spending 5 Echo Shards guarantees Echo (with a Twin) and drains the balance to 0");

// Second attempt: 0 Echo Shards remain, so requesting the spend again must be rejected -- Wisp is
// re-topped-up first so only the Echo Shard shortfall can be the cause.
shardLedger.Add(ResourceType.Wisp, 1000);
var shardCountBeforeSecondAttempt = shardParentA.WeaveCount;
var shardInsufficientResult = WeavingService.AttemptWeave(
    shardParentA, shardParentB, shardGenomeA, shardGenomeB, shardLedger, weaveRng, spendEchoShards: true);
var shardInsufficientPass = !shardInsufficientResult.Success
    && shardInsufficientResult.RejectionReason == WeaveRejectionReason.InsufficientEchoShards
    && shardParentA.WeaveCount == shardCountBeforeSecondAttempt; // nothing rolled
Console.WriteLine($"  [{(shardInsufficientPass ? "PASS" : "FAIL")}] Requesting Echo Shard spend with 0 remaining is rejected (reason: {shardInsufficientResult.RejectionReason})");

// ---- Reforge: successful Accept deducts its exact Wisp cost ----
var reforgeRng = new Random(99);
var reforgeTarget = SampleAnimas.CreateEmber();
var originalHead = reforgeTarget.Head;

var reforgeLedger = new PersistentLedger();
reforgeLedger.Add(ResourceType.Wisp, ReforgeService.BaseAcceptCost);

var reforgeOffer = ReforgeService.RollOffer(reforgeRng);
var reforgeAcceptSuccess = ReforgeService.Accept(reforgeOffer, reforgeTarget, reforgeLedger);
var reforgeAcceptPass = reforgeAcceptSuccess && reforgeLedger.GetBalance(ResourceType.Wisp) == 0;
Console.WriteLine($"  [{(reforgeAcceptPass ? "PASS" : "FAIL")}] Successful Reforge Accept deducts its exact Wisp cost ({ReforgeService.BaseAcceptCost} -> {reforgeLedger.GetBalance(ResourceType.Wisp)})");

// ---- Reforge: insufficient Wisp rejects Accept before touching the target ----
var poorReforgeTarget = SampleAnimas.CreateEmber();
var originalHeadBeforeRejectedAccept = poorReforgeTarget.Head;

var poorReforgeLedger = new PersistentLedger();
poorReforgeLedger.Add(ResourceType.Wisp, ReforgeService.BaseAcceptCost - 1); // one short

var poorReforgeOffer = ReforgeService.RollOffer(reforgeRng);
var poorReforgeAcceptSuccess = ReforgeService.Accept(poorReforgeOffer, poorReforgeTarget, poorReforgeLedger);
var poorReforgePass = !poorReforgeAcceptSuccess
    && poorReforgeLedger.GetBalance(ResourceType.Wisp) == ReforgeService.BaseAcceptCost - 1 // untouched
    && ReferenceEquals(poorReforgeTarget.Head, originalHeadBeforeRejectedAccept); // target untouched
Console.WriteLine($"  [{(poorReforgePass ? "PASS" : "FAIL")}] Insufficient Wisp rejects Reforge Accept before charging or swapping anything");

// ==== REWARDS — RewardService node-outcome grants ====
// None of these Wisp amounts, the Elite shard chance, or the Boss either/or shard pick existed
// anywhere before RewardService -- see its doc comments for the reasoning behind each number.
//
// Ember is momentary now, not a ledger balance (see ResourceType's own comment) -- every grant
// method that can drop Ember returns the dropped color(s) instead of writing them into the
// ledger, so these trials collect the returned lists directly rather than diffing a balance.
// Four checks: Combat (exact Wisp, exactly 1 guaranteed Ember/win, ~25% per color, no shards),
// Elite (exact Wisp, 1 guaranteed Ember + ~25% independent chance each for a 2nd/3rd, ~25%
// shard-trigger rate, never both shard types at once), Resource (exact Wisp, ~15% chance of
// exactly 1 bonus Ember), and Boss (exact Wisp, guaranteed Vessel, guaranteed exactly-one shard
// fragment every time, ~50/50 split between the two shard types, no Ember at all).

Console.WriteLine();
Console.WriteLine("================ Rewards: RewardService node-outcome grants ================");

var rewardRng = new Random(4242);

// ---- Combat win ----
const int CombatTrials = 4000;
var combatLedger = new PersistentLedger();
var combatEmberDrops = new List<AnimaColor>();
for (var i = 0; i < CombatTrials; i++)
{
    combatEmberDrops.AddRange(RewardService.GrantCombatWin(combatLedger, rewardRng));
}
var combatWispPass = combatLedger.GetBalance(ResourceType.Wisp) == CombatTrials * RewardService.CombatWinWisp;
var combatEmberTotalDraws = combatEmberDrops.Count;
var combatEmberCountPass = combatEmberTotalDraws == CombatTrials; // exactly 1 guaranteed per win, always
var combatNoShardsPass = combatLedger.GetBalance(ResourceType.EchoShard) == 0 && combatLedger.GetBalance(ResourceType.VesselShard) == 0
    && combatLedger.GetBalance(ResourceType.Vessel) == 0;

Console.WriteLine($"  [{(combatWispPass ? "PASS" : "FAIL")}] Combat win grants exact Wisp ({CombatTrials}x{RewardService.CombatWinWisp} = {CombatTrials * RewardService.CombatWinWisp} -> {combatLedger.GetBalance(ResourceType.Wisp)})");
Console.WriteLine($"  [{(combatEmberCountPass ? "PASS" : "FAIL")}] Combat win drops exactly 1 Ember per win, guaranteed ({combatEmberTotalDraws}/{CombatTrials})");
ReportBucket("EmberCrimson", combatEmberDrops.Count(c => c == AnimaColor.Crimson), combatEmberTotalDraws, 0.25);
ReportBucket("EmberOnyx", combatEmberDrops.Count(c => c == AnimaColor.Onyx), combatEmberTotalDraws, 0.25);
ReportBucket("EmberVerdant", combatEmberDrops.Count(c => c == AnimaColor.Verdant), combatEmberTotalDraws, 0.25);
ReportBucket("EmberAzure", combatEmberDrops.Count(c => c == AnimaColor.Azure), combatEmberTotalDraws, 0.25);
Console.WriteLine($"  [{(combatNoShardsPass ? "PASS" : "FAIL")}] Combat win grants no Shards/Vessel");

// ---- Elite win ----
Console.WriteLine();
const int EliteTrials = 4000;
var eliteLedger = new PersistentLedger();
var eliteEmberDropsPerWin = new List<List<AnimaColor>>();
var eliteShardTriggerCount = 0;
var eliteMultiShardViolation = false;
for (var i = 0; i < EliteTrials; i++)
{
    var echoBefore = eliteLedger.GetBalance(ResourceType.EchoShard);
    var vesselBefore = eliteLedger.GetBalance(ResourceType.VesselShard);
    eliteEmberDropsPerWin.Add(RewardService.GrantEliteWin(eliteLedger, rewardRng));
    var shardDelta = (eliteLedger.GetBalance(ResourceType.EchoShard) - echoBefore) + (eliteLedger.GetBalance(ResourceType.VesselShard) - vesselBefore);
    if (shardDelta > 1) eliteMultiShardViolation = true;
    if (shardDelta == 1) eliteShardTriggerCount++;
}
var eliteWispPass = eliteLedger.GetBalance(ResourceType.Wisp) == EliteTrials * RewardService.EliteWinWisp;
var eliteEmberDrops = eliteEmberDropsPerWin.SelectMany(d => d).ToList();
var eliteEmberTotalDraws = eliteEmberDrops.Count;
var eliteEmberBoundsPass = eliteEmberDropsPerWin.All(d => d.Count is >= 1 and <= 3); // 1 guaranteed, max 3

// The 2nd and 3rd slots are each their OWN independent EliteBonusEmberChance roll (neither gates
// the other), so a win's TOTAL count can't distinguish "slot 2 hit" from "only slot 3 hit" -- the
// only thing observable from the aggregate is the exact final count, whose distribution is the
// binomial composite of the two independent rolls: P(1)=0.75^2, P(2)=2x0.25x0.75, P(3)=0.25^2.
var eliteExactlyOneCount = eliteEmberDropsPerWin.Count(d => d.Count == 1);
var eliteExactlyTwoCount = eliteEmberDropsPerWin.Count(d => d.Count == 2);
var eliteExactlyThreeCount = eliteEmberDropsPerWin.Count(d => d.Count == 3);

Console.WriteLine($"  [{(eliteWispPass ? "PASS" : "FAIL")}] Elite win grants exact Wisp ({EliteTrials}x{RewardService.EliteWinWisp} = {EliteTrials * RewardService.EliteWinWisp} -> {eliteLedger.GetBalance(ResourceType.Wisp)})");
Console.WriteLine($"  [{(eliteEmberBoundsPass ? "PASS" : "FAIL")}] Elite win always drops between 1 and 3 Ember (1 guaranteed, max 3)");
ReportBucket("Elite exactly 1 Ember", eliteExactlyOneCount, EliteTrials, (1 - RewardService.EliteBonusEmberChance) * (1 - RewardService.EliteBonusEmberChance));
ReportBucket("Elite exactly 2 Ember", eliteExactlyTwoCount, EliteTrials, 2 * RewardService.EliteBonusEmberChance * (1 - RewardService.EliteBonusEmberChance));
ReportBucket("Elite exactly 3 Ember", eliteExactlyThreeCount, EliteTrials, RewardService.EliteBonusEmberChance * RewardService.EliteBonusEmberChance);
ReportBucket("EmberCrimson", eliteEmberDrops.Count(c => c == AnimaColor.Crimson), eliteEmberTotalDraws, 0.25);
ReportBucket("EmberOnyx", eliteEmberDrops.Count(c => c == AnimaColor.Onyx), eliteEmberTotalDraws, 0.25);
ReportBucket("EmberVerdant", eliteEmberDrops.Count(c => c == AnimaColor.Verdant), eliteEmberTotalDraws, 0.25);
ReportBucket("EmberAzure", eliteEmberDrops.Count(c => c == AnimaColor.Azure), eliteEmberTotalDraws, 0.25);
Console.WriteLine($"  [{(!eliteMultiShardViolation ? "PASS" : "FAIL")}] Elite win never grants both Shard types in the same win");
ReportBucket("Elite shard trigger", eliteShardTriggerCount, EliteTrials, RewardService.EliteShardChance);

// ---- Resource node ----
Console.WriteLine();
const int ResourceTrials = 4000;
var resourceLedger = new PersistentLedger();
var resourceEmberDropCount = 0;
for (var i = 0; i < ResourceTrials; i++)
{
    resourceEmberDropCount += RewardService.GrantResourceNode(resourceLedger, rewardRng).Count;
}
var resourcePass = resourceLedger.GetBalance(ResourceType.Wisp) == ResourceTrials * RewardService.ResourceNodeWisp
    && resourceLedger.GetBalance(ResourceType.EchoShard) == 0 && resourceLedger.GetBalance(ResourceType.VesselShard) == 0
    && resourceLedger.GetBalance(ResourceType.Vessel) == 0;
Console.WriteLine($"  [{(resourcePass ? "PASS" : "FAIL")}] Resource node grants exact Wisp, no Shards/Vessel ({ResourceTrials}x{RewardService.ResourceNodeWisp} = {ResourceTrials * RewardService.ResourceNodeWisp} -> {resourceLedger.GetBalance(ResourceType.Wisp)})");
ReportBucket("Resource bonus Ember", resourceEmberDropCount, ResourceTrials, RewardService.ResourceBonusEmberChance);

// ---- Boss win ----
Console.WriteLine();
const int BossTrials = 2000;
var bossLedger = new PersistentLedger();
var bossMultiShardViolation = false;
var bossZeroShardViolation = false;
for (var i = 0; i < BossTrials; i++)
{
    var echoBefore = bossLedger.GetBalance(ResourceType.EchoShard);
    var vesselBefore = bossLedger.GetBalance(ResourceType.VesselShard);
    RewardService.GrantBossWin(bossLedger, rewardRng);
    var shardDelta = (bossLedger.GetBalance(ResourceType.EchoShard) - echoBefore) + (bossLedger.GetBalance(ResourceType.VesselShard) - vesselBefore);
    if (shardDelta > 1) bossMultiShardViolation = true;
    if (shardDelta != 1) bossZeroShardViolation = true; // guaranteed -- must be exactly 1 every time
}
var bossWispPass = bossLedger.GetBalance(ResourceType.Wisp) == BossTrials * RewardService.BossWinWisp;
var bossVesselPass = bossLedger.GetBalance(ResourceType.Vessel) == BossTrials;
var bossTotalShards = bossLedger.GetBalance(ResourceType.EchoShard) + bossLedger.GetBalance(ResourceType.VesselShard);

Console.WriteLine($"  [{(bossWispPass ? "PASS" : "FAIL")}] Boss win grants exact Wisp ({BossTrials}x{RewardService.BossWinWisp} = {BossTrials * RewardService.BossWinWisp} -> {bossLedger.GetBalance(ResourceType.Wisp)})");
Console.WriteLine($"  [{(bossVesselPass ? "PASS" : "FAIL")}] Boss win grants exactly 1 Vessel every time ({bossLedger.GetBalance(ResourceType.Vessel)}/{BossTrials})");
Console.WriteLine($"  [{(!bossZeroShardViolation ? "PASS" : "FAIL")}] Boss win grants exactly 1 Shard fragment every time, never 0 ({bossTotalShards}/{BossTrials})");
Console.WriteLine($"  [{(!bossMultiShardViolation ? "PASS" : "FAIL")}] Boss win never grants both Shard types at once");
ReportBucket("Boss EchoShard-of-shards", bossLedger.GetBalance(ResourceType.EchoShard), bossTotalShards, 0.5);

// ---- Reward tier ladder sanity ----
Console.WriteLine();
var wispOrderingPass = RewardService.ResourceNodeWisp < RewardService.CombatWinWisp
    && RewardService.CombatWinWisp < RewardService.EliteWinWisp
    && RewardService.EliteWinWisp < RewardService.BossWinWisp;
Console.WriteLine($"  [{(wispOrderingPass ? "PASS" : "FAIL")}] Wisp reward tier ladder holds: Resource({RewardService.ResourceNodeWisp}) < Combat({RewardService.CombatWinWisp}) < Elite({RewardService.EliteWinWisp}) < Boss({RewardService.BossWinWisp})");

// ==== ARTIFACTS — first pass at 6 Delve-scoped (run-only) Artifacts ====
// Twin Flame, Barrier Stone, Vanguard's Bell, and Weaver's Thread are combat-time effects, so
// they're driven through a real (minimal) CombatEngine fight rather than called directly -- the
// same public API (StartCombat/RunRound) a real caller would use. Wisp Charm and Marked Coin are
// pure Economy-layer effects, tested directly against RewardService/ArtifactService.

Console.WriteLine();
Console.WriteLine("================ Artifacts: 6 Delve-scoped Artifacts ================");

// Passive dummy enemy (empty BehaviorRules -- always "has no valid action") so a test round can
// isolate a Round-Start/combat-start effect without any incoming damage muddying the result.
Enemy MakeDummyEnemy() => new()
{
    Name = "Dummy",
    MaxHp = 50,
    Defense = 0,
    CurrentHp = 50,
    Position = 1,
    Speed = 1,
    BehaviorRules = [],
};

// ---- Twin Flame: saves exactly once per combat, then stops ----
var twinFlameAnima = SampleAnimas.CreateEmber();
twinFlameAnima.CurrentHp = 5;

var lethalStrike = new Skill
{
    Name = "Lethal Strike",
    Category = SkillCategory.Attack,
    Range = AttackRange.Melee,
    Target = TargetType.Enemy,
    EnergyCost = 0,
    BaseDamage = 999,
};
var lethalStriker = new Enemy
{
    Name = "LethalStriker",
    MaxHp = 100,
    Defense = 0,
    CurrentHp = 100,
    Position = 1,
    Speed = 1,
    BehaviorRules = [new EnemyBehaviorRule { Condition = (_, _) => true, Skill = lethalStrike }],
};

var twinFlameState = new CombatState { PlayerTeam = [twinFlameAnima], EnemyTeam = [lethalStriker] };
var twinFlameEngine = new CombatEngine(twinFlameState, [SampleArtifacts.CreateTwinFlame()]);
twinFlameEngine.StartCombat();
twinFlameEngine.RunRound(); // lethal hit #1 -- should be saved at 1 HP
var twinFlameSavedAt1 = twinFlameAnima.CurrentHp == 1;
twinFlameEngine.RunRound(); // lethal hit #2 -- Twin Flame already used, should NOT save again
var twinFlameDiedSecondHit = twinFlameAnima.CurrentHp == 0;
Console.WriteLine($"  [{(twinFlameSavedAt1 ? "PASS" : "FAIL")}] Twin Flame saves the first lethal hit at exactly 1 HP (HP: {(twinFlameSavedAt1 ? 1 : twinFlameAnima.CurrentHp)})");
Console.WriteLine($"  [{(twinFlameDiedSecondHit ? "PASS" : "FAIL")}] Twin Flame does NOT save a second lethal hit in the same combat (HP: {twinFlameAnima.CurrentHp})");

// ---- Barrier Stone: grants Shield to the whole team at every Round Start ----
var barrierAnimaA = SampleAnimas.CreateEmber();
var barrierAnimaB = SampleAnimas.CreateReaper();
barrierAnimaB.Position = 2;

var barrierState = new CombatState { PlayerTeam = [barrierAnimaA, barrierAnimaB], EnemyTeam = [MakeDummyEnemy()] };
var barrierEngine = new CombatEngine(barrierState, [SampleArtifacts.CreateBarrierStone()]);
barrierEngine.StartCombat();
barrierEngine.RunRound();
var barrierShieldA = barrierAnimaA.ActiveStatuses.FirstOrDefault(s => s.Keyword == "Shield")?.Magnitude;
var barrierShieldB = barrierAnimaB.ActiveStatuses.FirstOrDefault(s => s.Keyword == "Shield")?.Magnitude;
var barrierPass = barrierShieldA == 5 && barrierShieldB == 5;
Console.WriteLine($"  [{(barrierPass ? "PASS" : "FAIL")}] Barrier Stone grants +5 Shield to the whole team at Round Start (A: {barrierShieldA?.ToString() ?? "none"}, B: {barrierShieldB?.ToString() ?? "none"})");

// ---- Vanguard's Bell: +1 starting Energy, Round 1 only ----
var noBellState = new CombatState { PlayerTeam = [SampleAnimas.CreateEmber()], EnemyTeam = [MakeDummyEnemy()] };
new CombatEngine(noBellState).StartCombat();
var baselineEnergyPass = noBellState.SharedEnergy == 3;

var bellState = new CombatState { PlayerTeam = [SampleAnimas.CreateEmber()], EnemyTeam = [MakeDummyEnemy()] };
new CombatEngine(bellState, [SampleArtifacts.CreateVanguardsBell()]).StartCombat();
var bellPass = bellState.SharedEnergy == 4;
Console.WriteLine($"  [{(baselineEnergyPass ? "PASS" : "FAIL")}] Baseline starting Energy (no Artifact) is 3 (observed {noBellState.SharedEnergy})");
Console.WriteLine($"  [{(bellPass ? "PASS" : "FAIL")}] Vanguard's Bell grants +1 starting Energy (observed {bellState.SharedEnergy})");

// ---- Weaver's Thread: +1 opening hand card, 7 -> 8 ----
var noThreadState = new CombatState { PlayerTeam = [SampleAnimas.CreateEmber()], EnemyTeam = [MakeDummyEnemy()] };
new CombatEngine(noThreadState).StartCombat();
var baselineHandPass = noThreadState.Hand.Count == 7;

var threadState = new CombatState { PlayerTeam = [SampleAnimas.CreateEmber()], EnemyTeam = [MakeDummyEnemy()] };
new CombatEngine(threadState, [SampleArtifacts.CreateWeaversThread()]).StartCombat();
var threadPass = threadState.Hand.Count == 8;
Console.WriteLine($"  [{(baselineHandPass ? "PASS" : "FAIL")}] Baseline opening hand (no Artifact) is 7 cards (observed {noThreadState.Hand.Count})");
Console.WriteLine($"  [{(threadPass ? "PASS" : "FAIL")}] Weaver's Thread grants an 8-card opening hand (observed {threadState.Hand.Count})");

// ---- Wisp Charm: +20% Wisp on every reward type ----
Console.WriteLine();
var wispCharmRunLedger = new RunLedger();
wispCharmRunLedger.Artifacts.Add(SampleArtifacts.CreateWispCharm());
var wispCharmRng = new Random(555);

var wispCharmCombatLedger = new PersistentLedger();
RewardService.GrantCombatWin(wispCharmCombatLedger, wispCharmRng, wispCharmRunLedger);
var wispCharmCombatPass = wispCharmCombatLedger.GetBalance(ResourceType.Wisp) == (int)Math.Round(RewardService.CombatWinWisp * 1.2);

var wispCharmBossLedger = new PersistentLedger();
RewardService.GrantBossWin(wispCharmBossLedger, wispCharmRng, wispCharmRunLedger);
var wispCharmBossPass = wispCharmBossLedger.GetBalance(ResourceType.Wisp) == (int)Math.Round(RewardService.BossWinWisp * 1.2);

var noWispCharmLedger = new PersistentLedger();
RewardService.GrantCombatWin(noWispCharmLedger, wispCharmRng); // no runLedger -- baseline, unboosted
var noWispCharmPass = noWispCharmLedger.GetBalance(ResourceType.Wisp) == RewardService.CombatWinWisp;

Console.WriteLine($"  [{(noWispCharmPass ? "PASS" : "FAIL")}] Baseline Combat win Wisp (no Artifact) is unboosted ({RewardService.CombatWinWisp} -> {noWispCharmLedger.GetBalance(ResourceType.Wisp)})");
Console.WriteLine($"  [{(wispCharmCombatPass ? "PASS" : "FAIL")}] Wisp Charm boosts Combat win Wisp by exactly 20% ({RewardService.CombatWinWisp} -> {wispCharmCombatLedger.GetBalance(ResourceType.Wisp)}, expected {(int)Math.Round(RewardService.CombatWinWisp * 1.2)})");
Console.WriteLine($"  [{(wispCharmBossPass ? "PASS" : "FAIL")}] Wisp Charm boosts Boss win Wisp by exactly 20% ({RewardService.BossWinWisp} -> {wispCharmBossLedger.GetBalance(ResourceType.Wisp)}, expected {(int)Math.Round(RewardService.BossWinWisp * 1.2)})");

// ---- Marked Coin: grants a resource immediately on pickup ----
Console.WriteLine();
const int MarkedCoinTrials = 4000;
var markedCoin = SampleArtifacts.CreateMarkedCoin();
var markedCoinRunLedger = new RunLedger();
var markedCoinLedger = new PersistentLedger();
var markedCoinRng = new Random(31415);

var markedCoinWispCount = 0;
var markedCoinEmberCount = 0;
var markedCoinEchoCount = 0;
var markedCoinVesselCount = 0;
var markedCoinBadTrial = false;
for (var i = 0; i < MarkedCoinTrials; i++)
{
    var wispBefore = markedCoinLedger.GetBalance(ResourceType.Wisp);
    var echoBefore = markedCoinLedger.GetBalance(ResourceType.EchoShard);
    var vesselBefore = markedCoinLedger.GetBalance(ResourceType.VesselShard);

    // Ember is momentary now -- ArtifactService.Grant returns the dropped color directly (null
    // unless Marked Coin's roll landed on the Ember branch) instead of writing it to the ledger.
    var markedCoinDroppedEmber = ArtifactService.Grant(markedCoinRunLedger, markedCoin, markedCoinLedger, markedCoinRng);

    var wispHit = markedCoinLedger.GetBalance(ResourceType.Wisp) > wispBefore;
    var emberHit = markedCoinDroppedEmber is not null;
    var echoHit = markedCoinLedger.GetBalance(ResourceType.EchoShard) > echoBefore;
    var vesselHit = markedCoinLedger.GetBalance(ResourceType.VesselShard) > vesselBefore;

    var hitCount = (wispHit ? 1 : 0) + (emberHit ? 1 : 0) + (echoHit ? 1 : 0) + (vesselHit ? 1 : 0);
    if (hitCount != 1) markedCoinBadTrial = true;

    if (wispHit) markedCoinWispCount++;
    if (emberHit) markedCoinEmberCount++;
    if (echoHit) markedCoinEchoCount++;
    if (vesselHit) markedCoinVesselCount++;
}

var markedCoinAddedPass = markedCoinRunLedger.Artifacts.Count == MarkedCoinTrials;
Console.WriteLine($"  [{(markedCoinAddedPass ? "PASS" : "FAIL")}] Each pickup call adds Marked Coin to RunLedger.Artifacts ({markedCoinRunLedger.Artifacts.Count}/{MarkedCoinTrials})");
Console.WriteLine($"  [{(!markedCoinBadTrial ? "PASS" : "FAIL")}] Every pickup grants EXACTLY ONE resource type, never zero or multiple");
ReportBucket("Marked Coin -> Wisp", markedCoinWispCount, MarkedCoinTrials, 0.50);
ReportBucket("Marked Coin -> Ember", markedCoinEmberCount, MarkedCoinTrials, 0.35);
ReportBucket("Marked Coin -> EchoShard", markedCoinEchoCount, MarkedCoinTrials, 0.075);
ReportBucket("Marked Coin -> VesselShard", markedCoinVesselCount, MarkedCoinTrials, 0.075);

// ==== ARTIFACTS (PART 2) — the final 4, completing the full 10-Artifact set ====
Console.WriteLine();
Console.WriteLine("================ Artifacts: final 4 (Withering Fang, Focusing Lens, Silent Chime, Ember Core) ================");

// ---- Withering Fang: wasted on a non-combat node, executes the lowest-current-HP enemy on a combat node ----
var witheringFangNonCombatLedger = new RunLedger();
witheringFangNonCombatLedger.Artifacts.Add(SampleArtifacts.CreateWitheringFang());
var witheringFangNonCombatTeam = new List<AnimaUnit> { SampleAnimas.CreateEmber() };
ArtifactService.OnNodeVisited(witheringFangNonCombatLedger, witheringFangNonCombatTeam); // non-combat node -- no CombatState passed
var witheringFangWastedPass = !witheringFangNonCombatLedger.Artifacts.Any(a => a.Name == "Withering Fang");
Console.WriteLine($"  [{(witheringFangWastedPass ? "PASS" : "FAIL")}] Withering Fang is consumed (wasted, no effect) on a non-combat node visit");

var witheringFangCombatLedger = new RunLedger();
witheringFangCombatLedger.Artifacts.Add(SampleArtifacts.CreateWitheringFang());
var witheringFangHighHp = new Enemy { Name = "HighHp", MaxHp = 200, Defense = 0, CurrentHp = 150, Position = 1, Speed = 1, BehaviorRules = [] };
var witheringFangLowHp = new Enemy { Name = "LowHp", MaxHp = 200, Defense = 0, CurrentHp = 30, Position = 2, Speed = 1, BehaviorRules = [] };
var witheringFangMidHp = new Enemy { Name = "MidHp", MaxHp = 200, Defense = 0, CurrentHp = 90, Position = 3, Speed = 1, BehaviorRules = [] };
var witheringFangCombatState = new CombatState { PlayerTeam = [SampleAnimas.CreateEmber()], EnemyTeam = [witheringFangHighHp, witheringFangLowHp, witheringFangMidHp] };
ArtifactService.OnNodeVisited(witheringFangCombatLedger, witheringFangCombatState.PlayerTeam, witheringFangCombatState);
var witheringFangExecutePass = witheringFangLowHp.CurrentHp == 1 && witheringFangHighHp.CurrentHp == 150 && witheringFangMidHp.CurrentHp == 90;
var witheringFangCombatConsumedPass = !witheringFangCombatLedger.Artifacts.Any(a => a.Name == "Withering Fang");
Console.WriteLine($"  [{(witheringFangExecutePass ? "PASS" : "FAIL")}] Withering Fang sets the lowest-current-HP enemy to exactly 1 HP on a combat node (LowHp: {witheringFangLowHp.CurrentHp}, HighHp: {witheringFangHighHp.CurrentHp}, MidHp: {witheringFangMidHp.CurrentHp})");
Console.WriteLine($"  [{(witheringFangCombatConsumedPass ? "PASS" : "FAIL")}] Withering Fang is consumed after executing on a combat node too");

// ---- Sapling Charm: heals the whole team 10% MaxHp on ANY node entry (combat or not), repeatedly ----
var saplingCharmLedger = new RunLedger();
saplingCharmLedger.Artifacts.Add(SampleArtifacts.CreateSaplingCharm());

var saplingCharmA = SampleAnimas.CreateEmber(); // MaxHp 100 -> 10/heal
saplingCharmA.CurrentHp = 50;
var saplingCharmB = SampleAnimas.CreateBoulder(); // MaxHp 130 -> 13/heal
saplingCharmB.CurrentHp = 60;
var saplingCharmFallen = SampleAnimas.CreateSprout(); // MaxHp 100, starts at 0 -- should NOT be revived
saplingCharmFallen.CurrentHp = 0;
var saplingCharmTeam = new List<AnimaUnit> { saplingCharmA, saplingCharmB, saplingCharmFallen };

// Non-combat node entry (Resource/Treasure/Shop/Reforge all look like this -- no CombatState).
ArtifactService.OnNodeVisited(saplingCharmLedger, saplingCharmTeam);
var saplingCharmNonCombatPass = saplingCharmA.CurrentHp == 60 && saplingCharmB.CurrentHp == 73 && saplingCharmFallen.CurrentHp == 0;
Console.WriteLine($"  [{(saplingCharmNonCombatPass ? "PASS" : "FAIL")}] Sapling Charm heals 10% MaxHp on a non-combat node entry, doesn't revive the fallen (Ember 50->{saplingCharmA.CurrentHp} expected 60, Boulder 60->{saplingCharmB.CurrentHp} expected 73, Sprout stays {saplingCharmFallen.CurrentHp} expected 0)");

// Combat node entry (CombatState present) -- should fire identically, not just on non-combat nodes.
var saplingCharmDummyEnemy = new Enemy { Name = "Dummy", MaxHp = 10, Defense = 0, CurrentHp = 10, Position = 1, Speed = 1, BehaviorRules = [] };
var saplingCharmCombatState = new CombatState { PlayerTeam = saplingCharmTeam, EnemyTeam = [saplingCharmDummyEnemy] };
ArtifactService.OnNodeVisited(saplingCharmLedger, saplingCharmTeam, saplingCharmCombatState);
var saplingCharmCombatPass = saplingCharmA.CurrentHp == 70 && saplingCharmB.CurrentHp == 86;
Console.WriteLine($"  [{(saplingCharmCombatPass ? "PASS" : "FAIL")}] Sapling Charm also heals on a combat node entry (Ember ->{saplingCharmA.CurrentHp} expected 70, Boulder ->{saplingCharmB.CurrentHp} expected 86)");

// Caps at MaxHp -- doesn't overheal.
var saplingCharmNearFull = SampleAnimas.CreateEmber();
saplingCharmNearFull.CurrentHp = 98;
ArtifactService.OnNodeVisited(saplingCharmLedger, new List<AnimaUnit> { saplingCharmNearFull });
var saplingCharmCapPass = saplingCharmNearFull.CurrentHp == 100;
Console.WriteLine($"  [{(saplingCharmCapPass ? "PASS" : "FAIL")}] Sapling Charm's heal caps at MaxHp, no overheal (98 -> {saplingCharmNearFull.CurrentHp}, expected 100)");

// Not owned -- no effect, sanity baseline.
var noSaplingCharmLedger = new RunLedger();
var noSaplingCharmAnima = SampleAnimas.CreateEmber();
noSaplingCharmAnima.CurrentHp = 50;
ArtifactService.OnNodeVisited(noSaplingCharmLedger, new List<AnimaUnit> { noSaplingCharmAnima });
var noSaplingCharmPass = noSaplingCharmAnima.CurrentHp == 50;
Console.WriteLine($"  [{(noSaplingCharmPass ? "PASS" : "FAIL")}] Baseline node entry (no Artifact) heals nothing (50 -> {noSaplingCharmAnima.CurrentHp})");

// ---- Focusing Lens: triggers exactly on the 4th/8th/12th Attack play, resets between fights ----
Skill MakeFocusingLensAttack() => new()
{
    Name = "Test Strike",
    Category = SkillCategory.Attack,
    Range = AttackRange.Melee,
    Target = TargetType.Enemy,
    EnergyCost = 0,
    BaseDamage = 10,
};
AnimaUnit MakeArtifactTestAnima(string id, Skill head, Skill frame, Skill tail) => new()
{
    Id = id,
    Color = AnimaColor.Crimson,
    BaseStats = new Stats { MaxHp = 100, Defense = 0, Speed = 10, DamageMultiplier = 1.0, SpiritMultiplier = 1.0 },
    Head = head,
    Frame = frame,
    Tail = tail,
    Crest = new Skill { Name = "NoCrest", Category = SkillCategory.Passive, Target = TargetType.SelfTarget },
    CurrentHp = 100,
    Position = 1,
};
Enemy MakeTankyDummy() => new() { Name = "TankyDummy", MaxHp = 500, Defense = 0, CurrentHp = 500, Position = 1, Speed = 1, BehaviorRules = [] };
int? ParseFocusingLensTrigger(string line)
{
    const string marker = "deals double damage!";
    if (!line.Contains(marker)) return null;
    var hashIndex = line.IndexOf('#');
    var closeParenIndex = line.IndexOf(')', hashIndex);
    return int.Parse(line[(hashIndex + 1)..closeParenIndex]);
}

var focusingLensTriggers1 = new List<int>();
var focusingLensAnima1 = MakeArtifactTestAnima("FocusingLensTester1", MakeFocusingLensAttack(), MakeFocusingLensAttack(), MakeFocusingLensAttack());
var focusingLensState1 = new CombatState { PlayerTeam = [focusingLensAnima1], EnemyTeam = [MakeTankyDummy()] };
var focusingLensEngine1 = new CombatEngine(focusingLensState1, [SampleArtifacts.CreateFocusingLens()]);
focusingLensEngine1.ChoosePlayerSkill = (a, s) => s.Hand.FirstOrDefault();
focusingLensEngine1.OnLog = line => { if (ParseFocusingLensTrigger(line) is int n) focusingLensTriggers1.Add(n); };
focusingLensEngine1.StartCombat();
for (var i = 0; i < 12; i++) focusingLensEngine1.RunRound();
var focusingLensFirstFightPass = focusingLensTriggers1.SequenceEqual(new[] { 4, 8, 12 });
Console.WriteLine($"  [{(focusingLensFirstFightPass ? "PASS" : "FAIL")}] Focusing Lens triggers exactly on attacks #4, #8, #12 across 12 plays (observed: {string.Join(",", focusingLensTriggers1)})");

var focusingLensTriggers2 = new List<int>();
var focusingLensAnima2 = MakeArtifactTestAnima("FocusingLensTester2", MakeFocusingLensAttack(), MakeFocusingLensAttack(), MakeFocusingLensAttack());
var focusingLensState2 = new CombatState { PlayerTeam = [focusingLensAnima2], EnemyTeam = [MakeTankyDummy()] };
var focusingLensEngine2 = new CombatEngine(focusingLensState2, [SampleArtifacts.CreateFocusingLens()]);
focusingLensEngine2.ChoosePlayerSkill = (a, s) => s.Hand.FirstOrDefault();
focusingLensEngine2.OnLog = line => { if (ParseFocusingLensTrigger(line) is int n) focusingLensTriggers2.Add(n); };
focusingLensEngine2.StartCombat();
for (var i = 0; i < 4; i++) focusingLensEngine2.RunRound();
var focusingLensResetPass = focusingLensTriggers2.SequenceEqual(new[] { 4 });
Console.WriteLine($"  [{(focusingLensResetPass ? "PASS" : "FAIL")}] Focusing Lens's counter resets in a fresh combat (2nd fight's own attack #4 triggers again, observed: {string.Join(",", focusingLensTriggers2)})");

// ---- Silent Chime: grants exactly one extra action, then is used up for the rest of the Delve ----
var silentChimeRunLedger = new RunLedger();
silentChimeRunLedger.Artifacts.Add(SampleArtifacts.CreateSilentChime());

var chimeAnima = MakeArtifactTestAnima("SilentChimeTester", MakeFocusingLensAttack(), MakeFocusingLensAttack(), MakeFocusingLensAttack());
var chimeActionsPlayed = 0;
var chimeState = new CombatState { PlayerTeam = [chimeAnima], EnemyTeam = [MakeTankyDummy()] };
var chimeEngine = new CombatEngine(chimeState);
chimeEngine.ChoosePlayerSkill = (a, s) => { chimeActionsPlayed++; return s.Hand.FirstOrDefault(); };
chimeEngine.StartCombat();

var chimeActivated = chimeEngine.TryActivateSilentChime(chimeAnima, silentChimeRunLedger);
chimeEngine.RunRound(); // chimeAnima should act TWICE this Round: its normal turn + the Silent Chime extra action
var chimeActionsAfterActivatedRound = chimeActionsPlayed;
var chimeStillOwnedAfterUse = silentChimeRunLedger.Artifacts.Any(a => a.Name == "Silent Chime");

var chimeSecondActivationAttempt = chimeEngine.TryActivateSilentChime(chimeAnima, silentChimeRunLedger); // already used -- should fail
chimeEngine.RunRound();
var chimeActionsAfterSecondRound = chimeActionsPlayed;

var chimeGrantedExtraPass = chimeActivated && chimeActionsAfterActivatedRound == 2;
var chimeConsumedPass = !chimeStillOwnedAfterUse;
var chimeSingleUsePass = !chimeSecondActivationAttempt && (chimeActionsAfterSecondRound - chimeActionsAfterActivatedRound) == 1;

Console.WriteLine($"  [{(chimeGrantedExtraPass ? "PASS" : "FAIL")}] Silent Chime grants exactly one extra action in its activated Round (actions played: {chimeActionsAfterActivatedRound})");
Console.WriteLine($"  [{(chimeConsumedPass ? "PASS" : "FAIL")}] Silent Chime is removed from RunLedger.Artifacts immediately on activation");
Console.WriteLine($"  [{(chimeSingleUsePass ? "PASS" : "FAIL")}] Silent Chime cannot be re-activated after being used (2nd attempt returned {chimeSecondActivationAttempt}, next Round played only {chimeActionsAfterSecondRound - chimeActionsAfterActivatedRound} action)");

// ---- Ember Core: discounts a sample Reforge cost by 20% ----
var emberCoreRunLedger = new RunLedger();
emberCoreRunLedger.Artifacts.Add(SampleArtifacts.CreateEmberCore());
var emberCoreRng = new Random(2468);
var emberCoreOffer = ReforgeService.RollOffer(emberCoreRng);

var emberCoreLedger = new PersistentLedger();
emberCoreLedger.Add(ResourceType.Wisp, emberCoreOffer.AcceptCost); // full price -- discount should leave leftover
var emberCoreAcceptSuccess = ReforgeService.Accept(emberCoreOffer, SampleAnimas.CreateEmber(), emberCoreLedger, emberCoreRunLedger);
var expectedDiscountedCost = (int)Math.Round(emberCoreOffer.AcceptCost * 0.8);
var expectedLeftover = emberCoreOffer.AcceptCost - expectedDiscountedCost;
var emberCorePass = emberCoreAcceptSuccess && emberCoreLedger.GetBalance(ResourceType.Wisp) == expectedLeftover;
Console.WriteLine($"  [{(emberCorePass ? "PASS" : "FAIL")}] Ember Core discounts a Reforge Accept by 20% (full price {emberCoreOffer.AcceptCost} -> discounted {expectedDiscountedCost}, {expectedLeftover} Wisp left over)");

var noEmberCoreLedger = new PersistentLedger();
noEmberCoreLedger.Add(ResourceType.Wisp, emberCoreOffer.AcceptCost);
var noEmberCoreAcceptSuccess = ReforgeService.Accept(emberCoreOffer, SampleAnimas.CreateEmber(), noEmberCoreLedger); // no runLedger -- baseline
var noEmberCorePass = noEmberCoreAcceptSuccess && noEmberCoreLedger.GetBalance(ResourceType.Wisp) == 0;
Console.WriteLine($"  [{(noEmberCorePass ? "PASS" : "FAIL")}] Baseline Reforge Accept (no Artifact) charges full price ({emberCoreOffer.AcceptCost} -> {noEmberCoreLedger.GetBalance(ResourceType.Wisp)})");

// Ember Core's own description ("Reforge and Augment costs are reduced by 20%") plus the new
// spec's "buying Ember from Wares, discounted same as other Wares" -- both checked here alongside
// the Reforge case above, same Artifact instance/RunLedger.
var expectedEmberBuyDiscounted = (int)Math.Round(EmberService.ShopPrice * 0.8);
var emberBuyDiscountLedger = new PersistentLedger();
emberBuyDiscountLedger.Add(ResourceType.Wisp, EmberService.ShopPrice);
var emberBuyDiscountSuccess = EmberService.TryBuyEmber(emberBuyDiscountLedger, emberCoreRunLedger);
var emberBuyDiscountPass = emberBuyDiscountSuccess && emberBuyDiscountLedger.GetBalance(ResourceType.Wisp) == EmberService.ShopPrice - expectedEmberBuyDiscounted;
Console.WriteLine($"  [{(emberBuyDiscountPass ? "PASS" : "FAIL")}] Ember Core discounts buying Ember from Wares by 20% (full price {EmberService.ShopPrice} -> discounted {expectedEmberBuyDiscounted})");

var augmentDiscountSkill = new Skill { Name = "Discount Strike", Category = SkillCategory.Attack, Target = TargetType.Enemy, Color = AnimaColor.Crimson, EnergyCost = 1, BaseDamage = 10 };
var fullFirstSlotCost = AugmentService.GetNextAugmentCost(augmentDiscountSkill)!.Value; // undiscounted preview -- 15
var expectedAugmentDiscounted = (int)Math.Round(fullFirstSlotCost * 0.8);
var augmentDiscountLedger = new PersistentLedger();
augmentDiscountLedger.Add(ResourceType.Wisp, fullFirstSlotCost);
var augmentDiscountResult = AugmentService.TryApplyAugment(augmentDiscountSkill, AugmentType.IncreaseEffect, AnimaColor.Crimson, augmentDiscountLedger, emberCoreRunLedger);
var augmentDiscountPass = augmentDiscountResult.Success && augmentDiscountResult.WispCost == expectedAugmentDiscounted
    && augmentDiscountLedger.GetBalance(ResourceType.Wisp) == fullFirstSlotCost - expectedAugmentDiscounted;
Console.WriteLine($"  [{(augmentDiscountPass ? "PASS" : "FAIL")}] Ember Core discounts an Augment's Wisp tier by 20% too (full {fullFirstSlotCost} -> discounted {expectedAugmentDiscounted})");

// ---- EmberService: the other two pickup-flow halves (Augment-now is AugmentService itself) ----
var convertLedger = new PersistentLedger();
EmberService.ConvertToWisp(convertLedger);
var convertPass = convertLedger.GetBalance(ResourceType.Wisp) == EmberService.ConvertToWispAmount;
Console.WriteLine($"  [{(convertPass ? "PASS" : "FAIL")}] Converting a dropped Ember to Wisp grants exactly {EmberService.ConvertToWispAmount} Wisp");

var buyLedger = new PersistentLedger();
buyLedger.Add(ResourceType.Wisp, EmberService.ShopPrice);
var buySuccess = EmberService.TryBuyEmber(buyLedger);
var buyPass = buySuccess && buyLedger.GetBalance(ResourceType.Wisp) == 0;
Console.WriteLine($"  [{(buyPass ? "PASS" : "FAIL")}] Baseline (no Artifact) buying Ember from Wares costs exactly {EmberService.ShopPrice} Wisp");

var buyPoorLedger = new PersistentLedger();
buyPoorLedger.Add(ResourceType.Wisp, EmberService.ShopPrice - 1);
var buyPoorSuccess = EmberService.TryBuyEmber(buyPoorLedger);
var buyPoorPass = !buyPoorSuccess && buyPoorLedger.GetBalance(ResourceType.Wisp) == EmberService.ShopPrice - 1;
Console.WriteLine($"  [{(buyPoorPass ? "PASS" : "FAIL")}] Insufficient Wisp rejects buying Ember from Wares, nothing charged");

// ==== AUGMENTS — the real AugmentService, replacing the Delve simulation's earlier [SIM AUGMENT] stub ====
// The 4 locked types (Increase Effect, AoE Damage, Decrease Cost, Extend), the 3-per-part cap, and
// the 15/30/50 Wisp cost curve. Each of the 4 types is verified by actually running a real
// CombatEngine exchange before and after applying it -- not just asserting the mutated field --
// since the brief specifically asks for mechanical (not just data) proof.
//
// Ember is momentary now (see ResourceType's own comment) -- TryApplyAugment takes the color of an
// already-in-hand Ember directly as a parameter rather than reading a per-color balance, so there's
// nothing to pre-load for that half; only Wisp needs seeding below.

Console.WriteLine();
Console.WriteLine("================ Augments: real AugmentService ================");

var augmentLedger = new PersistentLedger();
augmentLedger.Add(ResourceType.Wisp, 10_000); // ample for every Wisp-tier Augment call in this section

int RunSingleHitDamage(AnimaUnit attacker, Skill skillToUse, Enemy target)
{
    var state = new CombatState { PlayerTeam = [attacker], EnemyTeam = [target] };
    var engine = new CombatEngine(state);
    engine.ChoosePlayerSkill = (_, _) => skillToUse;
    engine.StartCombat();
    var before = target.CurrentHp;
    engine.RunRound();
    return before - target.CurrentHp;
}

Enemy MakeAugmentDummy(string name = "Dummy", int position = 1) =>
    new() { Name = name, MaxHp = 1000, Defense = 0, CurrentHp = 1000, Position = position, Speed = 1, BehaviorRules = [] };

// ---- Increase Effect: boosts the skill's core magnitude (BaseDamage here) by +20% ----
var increaseEffectSkill = new Skill
{
    Name = "IE Strike", Category = SkillCategory.Attack, Range = AttackRange.Melee,
    Target = TargetType.Enemy, Color = AnimaColor.Crimson, EnergyCost = 0, BaseDamage = 20,
};
var increaseEffectAnima = MakeArtifactTestAnima("IncreaseEffectTester", increaseEffectSkill, MakeFocusingLensAttack(), MakeFocusingLensAttack());

var ieDamageBefore = RunSingleHitDamage(increaseEffectAnima, increaseEffectSkill, MakeAugmentDummy());
var ieResult = AugmentService.TryApplyAugment(increaseEffectSkill, AugmentType.IncreaseEffect, AnimaColor.Crimson, augmentLedger);
var ieDamageAfter = RunSingleHitDamage(increaseEffectAnima, increaseEffectSkill, MakeAugmentDummy());

var ieExpectedDamage = (int)Math.Round(20 * 1.2);
var iePass = ieResult.Success && ieResult.AppliedType == AugmentType.IncreaseEffect
    && increaseEffectSkill.BaseDamage == ieExpectedDamage
    && ieDamageBefore == 20 && ieDamageAfter == ieExpectedDamage;
Console.WriteLine($"  [{(iePass ? "PASS" : "FAIL")}] Increase Effect boosts real combat damage dealt (dealt {ieDamageBefore} -> {ieDamageAfter}, expected 20 -> {ieExpectedDamage}; cost {ieResult.WispCost} Wisp)");

// ---- AoE Damage: converts a single-enemy-target Attack to hit AllEnemies at 50% value ----
var aoeSkill = new Skill
{
    Name = "AoE Strike", Category = SkillCategory.Attack, Range = AttackRange.Melee,
    Target = TargetType.Enemy, Color = AnimaColor.Crimson, EnergyCost = 0, BaseDamage = 20,
};
var aoeAnima = MakeArtifactTestAnima("AoEDamageTester", aoeSkill, MakeFocusingLensAttack(), MakeFocusingLensAttack());

var aoeTeamBefore = new List<Enemy> { MakeAugmentDummy("D1", 1), MakeAugmentDummy("D2", 2), MakeAugmentDummy("D3", 3) };
var aoeStateBefore = new CombatState { PlayerTeam = [aoeAnima], EnemyTeam = aoeTeamBefore };
var aoeEngineBefore = new CombatEngine(aoeStateBefore);
aoeEngineBefore.ChoosePlayerSkill = (_, _) => aoeSkill;
aoeEngineBefore.StartCombat();
aoeEngineBefore.RunRound();
var aoeHitCountBefore = aoeTeamBefore.Count(e => e.CurrentHp < 1000);

var aoeResult = AugmentService.TryApplyAugment(aoeSkill, AugmentType.AoEDamage, AnimaColor.Crimson, augmentLedger);

var aoeTeamAfter = new List<Enemy> { MakeAugmentDummy("D1", 1), MakeAugmentDummy("D2", 2), MakeAugmentDummy("D3", 3) };
var aoeStateAfter = new CombatState { PlayerTeam = [aoeAnima], EnemyTeam = aoeTeamAfter };
var aoeEngineAfter = new CombatEngine(aoeStateAfter);
aoeEngineAfter.ChoosePlayerSkill = (_, _) => aoeSkill;
aoeEngineAfter.StartCombat();
aoeEngineAfter.RunRound();
var aoeDamagesAfter = aoeTeamAfter.Select(e => 1000 - e.CurrentHp).ToList();
var aoeHitCountAfter = aoeTeamAfter.Count(e => e.CurrentHp < 1000);

var aoePass = aoeResult.Success && aoeResult.AppliedType == AugmentType.AoEDamage
    && aoeSkill.Target == TargetType.AllEnemies && aoeSkill.BaseDamage == 10
    && aoeHitCountBefore == 1 && aoeHitCountAfter == 3 && aoeDamagesAfter.All(d => d == 10);
Console.WriteLine($"  [{(aoePass ? "PASS" : "FAIL")}] AoE Damage converts single-target to hit ALL enemies at 50% value (before: {aoeHitCountBefore}/3 hit; after: {aoeHitCountAfter}/3 hit, {string.Join(",", aoeDamagesAfter)} dmg each; cost {aoeResult.WispCost} Wisp)");

// ---- Decrease Cost: reduces EnergyCost with NO floor -- 3 applications on a 2-cost skill drive it negative ----
var costSkill = new Skill
{
    Name = "Cost Strike", Category = SkillCategory.Attack, Range = AttackRange.Melee,
    Target = TargetType.Enemy, Color = AnimaColor.Onyx, EnergyCost = 2, BaseDamage = 5,
};
var costAnima = MakeArtifactTestAnima("DecreaseCostTester", costSkill, MakeFocusingLensAttack(), MakeFocusingLensAttack());

int RunEnergySpendDelta(AnimaUnit attacker, Skill skillToUse)
{
    var state = new CombatState { PlayerTeam = [attacker], EnemyTeam = [MakeAugmentDummy()] };
    var engine = new CombatEngine(state);
    engine.ChoosePlayerSkill = (_, _) => skillToUse;
    engine.StartCombat();
    engine.RunRound();
    const int EnergyIfNoPlay = 6; // default SharedEnergy(3) + Round Start's +3, capped at 9 -- the baseline to diff against
    return EnergyIfNoPlay - state.SharedEnergy; // positive = spent, negative = net GAINED (over-refund)
}

var costSpendBefore = RunEnergySpendDelta(costAnima, costSkill);
var costResult1 = AugmentService.TryApplyAugment(costSkill, AugmentType.DecreaseCost, AnimaColor.Onyx, augmentLedger);
var costSpendAfter1 = RunEnergySpendDelta(costAnima, costSkill);
var costResult2 = AugmentService.TryApplyAugment(costSkill, AugmentType.DecreaseCost, AnimaColor.Onyx, augmentLedger);
var costSpendAfter2 = RunEnergySpendDelta(costAnima, costSkill);
var costResult3 = AugmentService.TryApplyAugment(costSkill, AugmentType.DecreaseCost, AnimaColor.Onyx, augmentLedger);
var costSpendAfter3 = RunEnergySpendDelta(costAnima, costSkill);

var decreaseCostPass = costResult1.Success && costResult2.Success && costResult3.Success
    && costSkill.EnergyCost == -1
    && costSpendBefore == 2 && costSpendAfter1 == 1 && costSpendAfter2 == 0 && costSpendAfter3 == -1;
Console.WriteLine($"  [{(decreaseCostPass ? "PASS" : "FAIL")}] Decrease Cost reduces EnergyCost with no floor across 3 applications, verified via real combat energy spend (spend sequence: {costSpendBefore} -> {costSpendAfter1} -> {costSpendAfter2} -> {costSpendAfter3}, final EnergyCost={costSkill.EnergyCost} -- negative means playing it now GRANTS energy)");

// ---- 4th Augment attempt on the same part is rejected (costSkill already has 3: MaxAugmentsPerPart) ----
var wispBeforeFourth = augmentLedger.GetBalance(ResourceType.Wisp);
var fourthResult = AugmentService.TryApplyAugment(costSkill, AugmentType.IncreaseEffect, AnimaColor.Onyx, augmentLedger);
var fourthRejectedPass = !fourthResult.Success && fourthResult.RejectionReason == AugmentRejectionReason.MaxAugmentsReached
    && costSkill.AppliedAugments.Count == 3 && costSkill.BaseDamage == 5 // IncreaseEffect never actually ran
    && augmentLedger.GetBalance(ResourceType.Wisp) == wispBeforeFourth; // no Wisp spent
Console.WriteLine($"  [{(fourthRejectedPass ? "PASS" : "FAIL")}] A 4th Augment attempt on an already-3-Augmented part is rejected, nothing mutated or charged (reason: {fourthResult.RejectionReason})");

// ---- Extend: +1 Charge to an UntilConsumed on-hit debuff, verified across a real multi-round exchange ----
var extendSkill = new Skill
{
    Name = "Extend Strike", Category = SkillCategory.Attack, Range = AttackRange.Melee,
    Target = TargetType.Enemy, Color = AnimaColor.Onyx, EnergyCost = 0, BaseDamage = 10,
    OnHitStatusKeyword = "Weak", OnHitStatusMagnitude = 50, OnHitStatusDuration = DurationType.UntilConsumed,
};
var extendPlainSkill = new Skill
{
    Name = "Plain Strike", Category = SkillCategory.Attack, Range = AttackRange.Melee,
    Target = TargetType.Enemy, Color = AnimaColor.Onyx, EnergyCost = 0, BaseDamage = 1,
};
var extendFoeAttack = new Skill
{
    Name = "Foe Attack", Category = SkillCategory.Attack, Range = AttackRange.Melee,
    Target = TargetType.Enemy, EnergyCost = 0, BaseDamage = 20,
};

Enemy MakeExtendFoe() => new()
{
    Name = "ExtendFoe", MaxHp = 1000, Defense = 0, CurrentHp = 1000, Position = 1, Speed = 1,
    BehaviorRules = [new EnemyBehaviorRule { Condition = (_, _) => true, Skill = extendFoeAttack }],
};

// Round 1: caster (Speed 10 > foe's Speed 1, so always acts first) applies Weak to the foe, THEN
// the foe -- already Weakened -- attacks back the same Round, consuming (or partially consuming,
// if Extended) its own Weak. Rounds 2+: caster uses the no-status filler skill instead, so Weak is
// never re-applied -- isolating exactly how many of the foe's OWN subsequent attacks stay reduced.
List<int> RunExtendExchange(AnimaUnit caster, int rounds)
{
    var foe = MakeExtendFoe();
    var state = new CombatState { PlayerTeam = [caster], EnemyTeam = [foe] };
    var engine = new CombatEngine(state);
    var round = 0;
    engine.ChoosePlayerSkill = (_, _) => round == 0 ? extendSkill : extendPlainSkill;
    engine.StartCombat();
    var damages = new List<int>();
    for (var i = 0; i < rounds; i++)
    {
        var before = caster.CurrentHp;
        engine.RunRound();
        damages.Add(before - caster.CurrentHp);
        round++;
    }
    return damages;
}

var extendCasterBefore = MakeArtifactTestAnima("ExtendCasterBefore", extendSkill, extendPlainSkill, MakeFocusingLensAttack());
var extendDamagesBefore = RunExtendExchange(extendCasterBefore, 3);

var extendResult = AugmentService.TryApplyAugment(extendSkill, AugmentType.Extend, AnimaColor.Onyx, augmentLedger);

var extendCasterAfter = MakeArtifactTestAnima("ExtendCasterAfter", extendSkill, extendPlainSkill, MakeFocusingLensAttack());
var extendDamagesAfter = RunExtendExchange(extendCasterAfter, 3);

var extendPass = extendResult.Success && extendResult.AppliedType == AugmentType.Extend && extendSkill.OnHitStatusExtraCharges == 1
    && extendDamagesBefore.SequenceEqual(new[] { 10, 20, 20 }) // unextended: 1 charge -- Weak consumed after the foe's 1st attack
    && extendDamagesAfter.SequenceEqual(new[] { 10, 10, 20 }); // extended: 2 charges -- Weak survives the foe's 1st attack too, gone by the 3rd
Console.WriteLine($"  [{(extendPass ? "PASS" : "FAIL")}] Extend adds +1 Charge to an UntilConsumed on-hit debuff, verified via a real 3-round combat exchange (baseline foe dmg/round: {string.Join(",", extendDamagesBefore)}, expected 10,20,20; extended: {string.Join(",", extendDamagesAfter)}, expected 10,10,20; cost {extendResult.WispCost} Wisp)");

// ---- Cost curve 15/30/50 Wisp, enforced exactly at every threshold (replaces the old 2/4/7 Ember curve) ----
var curveSkill = new Skill
{
    Name = "Curve Strike", Category = SkillCategory.Attack, Range = AttackRange.Melee,
    Target = TargetType.Enemy, Color = AnimaColor.Verdant, EnergyCost = 2, BaseDamage = 10,
};
var curveLedger = new PersistentLedger();

curveLedger.Add(ResourceType.Wisp, 14); // one short of the 1st slot's cost (15)
var curveReject1 = AugmentService.TryApplyAugment(curveSkill, AugmentType.DecreaseCost, AnimaColor.Verdant, curveLedger);
var curveReject1Pass = !curveReject1.Success && curveReject1.RejectionReason == AugmentRejectionReason.InsufficientWisp
    && curveLedger.GetBalance(ResourceType.Wisp) == 14 && curveSkill.EnergyCost == 2;

curveLedger.Add(ResourceType.Wisp, 1); // now exactly 15
var curveAccept1 = AugmentService.TryApplyAugment(curveSkill, AugmentType.DecreaseCost, AnimaColor.Verdant, curveLedger);
var curveAccept1Pass = curveAccept1.Success && curveAccept1.WispCost == 15 && curveLedger.GetBalance(ResourceType.Wisp) == 0;

curveLedger.Add(ResourceType.Wisp, 29); // one short of the 2nd slot's cost (30)
var curveReject2 = AugmentService.TryApplyAugment(curveSkill, AugmentType.DecreaseCost, AnimaColor.Verdant, curveLedger);
var curveReject2Pass = !curveReject2.Success && curveReject2.RejectionReason == AugmentRejectionReason.InsufficientWisp
    && curveLedger.GetBalance(ResourceType.Wisp) == 29;

curveLedger.Add(ResourceType.Wisp, 1); // now exactly 30
var curveAccept2 = AugmentService.TryApplyAugment(curveSkill, AugmentType.DecreaseCost, AnimaColor.Verdant, curveLedger);
var curveAccept2Pass = curveAccept2.Success && curveAccept2.WispCost == 30 && curveLedger.GetBalance(ResourceType.Wisp) == 0;

curveLedger.Add(ResourceType.Wisp, 49); // one short of the 3rd slot's cost (50)
var curveReject3 = AugmentService.TryApplyAugment(curveSkill, AugmentType.DecreaseCost, AnimaColor.Verdant, curveLedger);
var curveReject3Pass = !curveReject3.Success && curveReject3.RejectionReason == AugmentRejectionReason.InsufficientWisp
    && curveLedger.GetBalance(ResourceType.Wisp) == 49;

curveLedger.Add(ResourceType.Wisp, 1); // now exactly 50
var curveAccept3 = AugmentService.TryApplyAugment(curveSkill, AugmentType.DecreaseCost, AnimaColor.Verdant, curveLedger);
var curveAccept3Pass = curveAccept3.Success && curveAccept3.WispCost == 50 && curveLedger.GetBalance(ResourceType.Wisp) == 0;

var augmentCurvePass = curveReject1Pass && curveAccept1Pass && curveReject2Pass && curveAccept2Pass && curveReject3Pass && curveAccept3Pass;
Console.WriteLine($"  [{(augmentCurvePass ? "PASS" : "FAIL")}] Cost curve 15/30/50 Wisp is enforced exactly at every slot (each 1-short rejection and each exact-cost success both verified)");

// ---- Bonus edge cases: a colorless skill, a color-mismatched Ember, and a mechanically-inapplicable type ----
var noColorSkill = new Skill { Name = "No Color Strike", Category = SkillCategory.Attack, Target = TargetType.Enemy, EnergyCost = 1, BaseDamage = 10 };
var noColorResult = AugmentService.TryApplyAugment(noColorSkill, AugmentType.IncreaseEffect, AnimaColor.Crimson, augmentLedger);
var noColorPass = !noColorResult.Success && noColorResult.RejectionReason == AugmentRejectionReason.SkillMissingColor;
Console.WriteLine($"  [{(noColorPass ? "PASS" : "FAIL")}] A skill with no Color is rejected (nothing to know whether the Ember in hand even matches)");

// New rejection path introduced by the pickup-choice rework: the pickup page is expected to only
// ever offer same-color skills, but TryApplyAugment defensively rejects a mismatch anyway.
var colorMismatchSkill = new Skill { Name = "Mismatch Strike", Category = SkillCategory.Attack, Target = TargetType.Enemy, Color = AnimaColor.Azure, EnergyCost = 1, BaseDamage = 10 };
var colorMismatchWispBefore = augmentLedger.GetBalance(ResourceType.Wisp);
var colorMismatchResult = AugmentService.TryApplyAugment(colorMismatchSkill, AugmentType.IncreaseEffect, AnimaColor.Crimson, augmentLedger);
var colorMismatchPass = !colorMismatchResult.Success && colorMismatchResult.RejectionReason == AugmentRejectionReason.EmberColorMismatch
    && colorMismatchSkill.AppliedAugments.Count == 0 && augmentLedger.GetBalance(ResourceType.Wisp) == colorMismatchWispBefore;
Console.WriteLine($"  [{(colorMismatchPass ? "PASS" : "FAIL")}] An Ember whose color doesn't match the target skill's Color is rejected, nothing charged (skill is Azure, Ember in hand was Crimson)");

var healSkillForRejectTest = new Skill { Name = "Heal Test", Category = SkillCategory.Heal, Target = TargetType.Ally, Color = AnimaColor.Verdant, EnergyCost = 1, BaseHeal = 20 };
var notApplicableResult = AugmentService.TryApplyAugment(healSkillForRejectTest, AugmentType.AoEDamage, AnimaColor.Verdant, augmentLedger);
var notApplicablePass = !notApplicableResult.Success && notApplicableResult.RejectionReason == AugmentRejectionReason.NotApplicableToSkill;
Console.WriteLine($"  [{(notApplicablePass ? "PASS" : "FAIL")}] AoE Damage is rejected on a non-single-target-Attack skill (e.g. a Heal skill)");

// ---- Eligibility is keyed on the SKILL's own archetype Color, not the owning Anima's body Color ----
// Bastion is Onyx-bodied (Color = AnimaColor.Onyx) but its Tail (Cleanse) is a Verdant part --
// exactly the "mixed-color breeding" scenario the brief calls out. A Verdant Ember must be able to
// Augment Cleanse despite Bastion itself being Onyx, and an Onyx Ember must be rejected against
// that same Tail -- proving the check reads skill.Color, never anima.Color.
var bastion = SampleAnimas.CreateBastion();
var bastionEligibilityLedger = new PersistentLedger();
bastionEligibilityLedger.Add(ResourceType.Wisp, 1000);

var bastionCorrectColorResult = AugmentService.TryApplyAugment(bastion.Tail, AugmentType.IncreaseEffect, AnimaColor.Verdant, bastionEligibilityLedger);
var bastionCorrectColorPass = bastionCorrectColorResult.Success && bastion.Color == AnimaColor.Onyx && bastion.Tail.Color == AnimaColor.Verdant;
Console.WriteLine($"  [{(bastionCorrectColorPass ? "PASS" : "FAIL")}] A Verdant Ember Augments Bastion's Verdant Tail (Cleanse) even though Bastion's own body Color is Onyx");

var bastionWrongColorResult = AugmentService.TryApplyAugment(bastion.Head, AugmentType.IncreaseEffect, AnimaColor.Verdant, bastionEligibilityLedger);
var bastionWrongColorPass = !bastionWrongColorResult.Success && bastionWrongColorResult.RejectionReason == AugmentRejectionReason.EmberColorMismatch;
Console.WriteLine($"  [{(bastionWrongColorPass ? "PASS" : "FAIL")}] The SAME Verdant Ember is rejected against Bastion's Onyx Head (Bash) -- eligibility never falls back to the Anima's body Color");

var bastionOnyxHeadResult = AugmentService.TryApplyAugment(bastion.Head, AugmentType.IncreaseEffect, AnimaColor.Onyx, bastionEligibilityLedger);
var bastionOnyxHeadPass = bastionOnyxHeadResult.Success;
Console.WriteLine($"  [{(bastionOnyxHeadPass ? "PASS" : "FAIL")}] An Onyx Ember correctly Augments Bastion's Onyx Head (Bash) -- each of a single Anima's 4 parts is checked independently by its own Color");

// ==== ARTIFACT CAP — hard 3-Artifact hold cap per Delve, no swap mechanic ====
Console.WriteLine();
Console.WriteLine("================ Artifact cap: ArtifactService.MaxArtifactsPerDelve ================");

var capRunLedger = new RunLedger();
var capPersistentLedger = new PersistentLedger();
var capRng = new Random(99);
var capHasCapacityAtZero = ArtifactService.HasArtifactCapacity(capRunLedger);

var capGrant1 = ArtifactService.Grant(capRunLedger, SampleArtifacts.CreateWispCharm(), capPersistentLedger, capRng);
var capGrant2 = ArtifactService.Grant(capRunLedger, SampleArtifacts.CreateBarrierStone(), capPersistentLedger, capRng);
var capHasCapacityAtTwo = ArtifactService.HasArtifactCapacity(capRunLedger);
var capGrant3 = ArtifactService.Grant(capRunLedger, SampleArtifacts.CreateTwinFlame(), capPersistentLedger, capRng);
var capHasCapacityAtThree = ArtifactService.HasArtifactCapacity(capRunLedger);

var artifactCapPass = ArtifactService.MaxArtifactsPerDelve == 3
    && capHasCapacityAtZero && capHasCapacityAtTwo && !capHasCapacityAtThree
    && capRunLedger.Artifacts.Count == 3;
Console.WriteLine($"  [{(artifactCapPass ? "PASS" : "FAIL")}] HasArtifactCapacity correctly tracks the cap across 0/2/3 held Artifacts (cap={ArtifactService.MaxArtifactsPerDelve})");

// ---- ShopService: fresh independent stock every Roll, held-Artifact exclusion, cap-skips-the-offer ----
Console.WriteLine();
var shopRollRng = new Random(555);
var shopRoll1 = ShopService.Roll(new RunLedger(), shopRollRng);
var shopRoll2 = ShopService.Roll(new RunLedger(), shopRollRng);
var shopStockShapePass = shopRoll1.EmberOffers.Count == ShopService.EmberOfferCount && shopRoll2.EmberOffers.Count == ShopService.EmberOfferCount
    && shopRoll1.ArtifactOffer != null && shopRoll2.ArtifactOffer != null;
var shopStockIndependentPass = !shopRoll1.EmberOffers.SequenceEqual(shopRoll2.EmberOffers) || shopRoll1.ArtifactOffer!.Name != shopRoll2.ArtifactOffer!.Name;
Console.WriteLine($"  [{(shopStockShapePass ? "PASS" : "FAIL")}] Every Roll offers exactly {ShopService.EmberOfferCount} Ember and 1 Artifact when under the cap");
Console.WriteLine($"  [{(shopStockIndependentPass ? "PASS" : "FAIL")}] Two independent Rolls (fresh RunLedgers) produce different stock -- no shared/depleting pool (roll1: [{string.Join(",", shopRoll1.EmberOffers)}]+{shopRoll1.ArtifactOffer!.Name}, roll2: [{string.Join(",", shopRoll2.EmberOffers)}]+{shopRoll2.ArtifactOffer!.Name})");

var shopExclusionRunLedger = new RunLedger();
shopExclusionRunLedger.Artifacts.Add(SampleArtifacts.CreateWispCharm());
shopExclusionRunLedger.Artifacts.Add(SampleArtifacts.CreateBarrierStone());
var shopExclusionRng = new Random(1);
var shopExclusionHitCount = 0;
const int ShopExclusionTrials = 500;
for (var i = 0; i < ShopExclusionTrials; i++)
{
    var roll = ShopService.Roll(shopExclusionRunLedger, shopExclusionRng);
    if (roll.ArtifactOffer?.Name is "Wisp Charm" or "Barrier Stone") shopExclusionHitCount++;
}
var shopExclusionPass = shopExclusionHitCount == 0;
Console.WriteLine($"  [{(shopExclusionPass ? "PASS" : "FAIL")}] The Artifact offer never re-offers an Artifact the player already holds, across {ShopExclusionTrials} independent Rolls ({shopExclusionHitCount} violations)");

var shopAtCapRunLedger = new RunLedger();
shopAtCapRunLedger.Artifacts.Add(SampleArtifacts.CreateWispCharm());
shopAtCapRunLedger.Artifacts.Add(SampleArtifacts.CreateBarrierStone());
shopAtCapRunLedger.Artifacts.Add(SampleArtifacts.CreateTwinFlame());
var shopAtCapRoll = ShopService.Roll(shopAtCapRunLedger, new Random(2));
var shopAtCapPass = shopAtCapRoll.ArtifactOffer == null && shopAtCapRoll.EmberOffers.Count == ShopService.EmberOfferCount;
Console.WriteLine($"  [{(shopAtCapPass ? "PASS" : "FAIL")}] At the 3-Artifact cap, the Artifact slot doesn't roll at all (Ember slots still do)");

// ---- ShopService.TryBuyArtifact: charges Wisp (Ember Core discount applies), grants the exact instance ----
var buyArtifactLedger = new PersistentLedger();
buyArtifactLedger.Add(ResourceType.Wisp, ShopService.ArtifactWaresPrice);
var buyArtifactRunLedger = new RunLedger();
var buyArtifactOffer = SampleArtifacts.CreateBarrierStone();
var (buyArtifactSuccess, buyArtifactDroppedEmber) = ShopService.TryBuyArtifact(buyArtifactOffer, buyArtifactRunLedger, buyArtifactLedger, new Random(3));
var buyArtifactPass = buyArtifactSuccess && buyArtifactDroppedEmber == null
    && buyArtifactLedger.GetBalance(ResourceType.Wisp) == 0
    && buyArtifactRunLedger.Artifacts.Count == 1 && ReferenceEquals(buyArtifactRunLedger.Artifacts[0], buyArtifactOffer);
Console.WriteLine($"  [{(buyArtifactPass ? "PASS" : "FAIL")}] Buying an Artifact from Wares charges exactly {ShopService.ArtifactWaresPrice} Wisp (no Artifact) and grants the offered instance");

var buyArtifactDiscountLedger = new PersistentLedger();
var expectedArtifactDiscounted = (int)Math.Round(ShopService.ArtifactWaresPrice * 0.8);
buyArtifactDiscountLedger.Add(ResourceType.Wisp, expectedArtifactDiscounted);
var buyArtifactDiscountRunLedger = new RunLedger();
buyArtifactDiscountRunLedger.Artifacts.Add(SampleArtifacts.CreateEmberCore());
var (buyArtifactDiscountSuccess, _) = ShopService.TryBuyArtifact(SampleArtifacts.CreateSilentChime(), buyArtifactDiscountRunLedger, buyArtifactDiscountLedger, new Random(4));
var buyArtifactDiscountPass = buyArtifactDiscountSuccess && buyArtifactDiscountLedger.GetBalance(ResourceType.Wisp) == 0;
Console.WriteLine($"  [{(buyArtifactDiscountPass ? "PASS" : "FAIL")}] Ember Core discounts the Wares Artifact price by 20% too (full {ShopService.ArtifactWaresPrice} -> discounted {expectedArtifactDiscounted})");

var buyArtifactPoorLedger = new PersistentLedger();
buyArtifactPoorLedger.Add(ResourceType.Wisp, ShopService.ArtifactWaresPrice - 1);
var buyArtifactPoorRunLedger = new RunLedger();
var (buyArtifactPoorSuccess, _) = ShopService.TryBuyArtifact(SampleArtifacts.CreateWeaversThread(), buyArtifactPoorRunLedger, buyArtifactPoorLedger, new Random(5));
var buyArtifactPoorPass = !buyArtifactPoorSuccess && buyArtifactPoorRunLedger.Artifacts.Count == 0 && buyArtifactPoorLedger.GetBalance(ResourceType.Wisp) == ShopService.ArtifactWaresPrice - 1;
Console.WriteLine($"  [{(buyArtifactPoorPass ? "PASS" : "FAIL")}] Insufficient Wisp rejects buying the Wares Artifact, nothing charged or granted");

// Buying Marked Coin from Wares still fires its own OnPickup roll, same as a Treasure grant would
// -- checked across several seeds since OnPickup's 4-way pool means any single seed might land on
// the Ember branch (Wisp/Shard balances unchanged, only the returned AnimaColor? proves it fired).
var buyMarkedCoinFiredCount = 0;
const int BuyMarkedCoinTrials = 20;
for (var seed = 0; seed < BuyMarkedCoinTrials; seed++)
{
    var buyMarkedCoinLedger = new PersistentLedger();
    buyMarkedCoinLedger.Add(ResourceType.Wisp, ShopService.ArtifactWaresPrice);
    var buyMarkedCoinRunLedger = new RunLedger();
    var echoBefore = buyMarkedCoinLedger.GetBalance(ResourceType.EchoShard);
    var vesselBefore = buyMarkedCoinLedger.GetBalance(ResourceType.VesselShard);
    var (buyMarkedCoinSuccess, buyMarkedCoinDroppedEmber) = ShopService.TryBuyArtifact(SampleArtifacts.CreateMarkedCoin(), buyMarkedCoinRunLedger, buyMarkedCoinLedger, new Random(seed));
    if (!buyMarkedCoinSuccess) continue;

    var fired = buyMarkedCoinDroppedEmber != null
        || buyMarkedCoinLedger.GetBalance(ResourceType.Wisp) > 0 // the Wisp branch: spent ArtifactWaresPrice, then got 40 back
        || buyMarkedCoinLedger.GetBalance(ResourceType.EchoShard) > echoBefore
        || buyMarkedCoinLedger.GetBalance(ResourceType.VesselShard) > vesselBefore;
    if (fired) buyMarkedCoinFiredCount++;
}
var buyMarkedCoinPass = buyMarkedCoinFiredCount == BuyMarkedCoinTrials;
Console.WriteLine($"  [{(buyMarkedCoinPass ? "PASS" : "FAIL")}] Buying Marked Coin from Wares still fires its OnPickup hook every time (bonus resource granted on purchase, not just on Treasure pickup) ({buyMarkedCoinFiredCount}/{BuyMarkedCoinTrials})");

// ==== DELVE SIMULATION — full end-to-end playthrough ====
// The first genuine playthrough combining every system built this session: real map generation,
// real combat against real enemies, real reward/Artifact/Reforge/Shop mechanics, walked start to
// Boss. Not a pass/fail test suite -- a stress test for INTEGRATION gaps isolated unit tests can't
// catch. Several judgment calls were required where the brief explicitly left them open; each is
// flagged inline at the point it's made, and summarized again at the end.
//
// Pathing: at any branch, always take Next[0] (the first-listed connection) -- a simple,
// deterministic tie-break, not a claim about optimal routing. The Floor 1 start node is the
// leftmost (lowest Column) node, for the same reason.
//
// Team: HP is NOT reset between nodes (attrition across the whole Delve) -- Shop's own "heal the
// team" instruction only makes sense as a mechanic if HP otherwise persists, so this is treated as
// the intended model rather than an open question.
//
// Loss handling: a Combat/Elite/Boss node that ends in a full team wipe (or hits the Round safety
// cap without either side dying -- a stalemate) ends the Delve immediately. There's no partial-
// death/revival system in the engine to make "continue with a weakened team" mean anything
// coherent after a total wipe; a Shop's heal CAN revive an individual fallen Anima (nothing in
// ApplyHeal guards against healing a 0-HP combatant), so a wipe specifically means everyone is
// down at once, not that the run is fragile after any single loss.
//
// Enemy selection: Combat nodes field Grovehide AND Quillfang together (a small pack, matching
// "3v3" Combat framing more than a trivial 1-enemy skirmish) -- alternating which comes at Combat
// node index feels arbitrary, so instead alternate the ELITE pick (Sentinel / Leech Mother) between
// Elite node visits, to get real coverage of both Elite kits within one Delve.
//
// Ember -> Augment: now backed by the real Anima.Core.Economy.AugmentService AND EmberService,
// replacing both the earlier [SIM AUGMENT] stand-in and the (now removed) per-color Ember bank.
// Ember is momentary (see ResourceType's own comment) -- every node/Marked Coin/Shop drop is
// resolved individually and sequentially, right where it drops: scan every skill on the team (any
// of the 3 Animas, any of their 4 parts, Head/Frame/Tail/Crest) whose Color matches the dropped
// Ember, and apply the first of the 4 locked Augment types (fixed priority: Increase Effect, AoE
// Damage, Decrease Cost, Extend) that's both mechanically applicable and affordable in Wisp --
// converting the Ember to Wisp instead if no matching skill/type/Wisp combination qualifies. That
// priority order and scan order are a placeholder decision policy -- there's no real player choice
// UI yet, same scope note as the Reforge accept logic below.
//
// Reforge decision: accept whenever affordable, unconditionally -- no evaluation of whether the
// roll is actually good for the team (that would require real deckbuilding judgment this harness
// doesn't have). Always applied to the front-liner. A placeholder decision policy, flagged as such.

Console.WriteLine();
Console.WriteLine("================ Delve Simulation: full end-to-end playthrough ================");

const int DelveSeed = 7;
var delveMap = MapGenerator.Generate(new Random(DelveSeed));
var delveMapViolations = Anima.ConsoleHarness.MapPrinter.Validate(delveMap);
Console.WriteLine($"Map generated (seed {DelveSeed}). Rule violations: {delveMapViolations.Count}.");

List<MapNode> WalkPath(DungeonMap map)
{
    var path = new List<MapNode>();
    var current = map.Floors[0].OrderBy(n => n.Column).First(); // leftmost Floor-1 node -- deterministic "first"
    path.Add(current);
    while (current.Next.Count > 0)
    {
        current = current.Next[0]; // always take the first available connection -- Floor 15's own
                                    // Next already includes the Boss, so this naturally walks all
                                    // the way there without special-casing it.
        path.Add(current);
    }
    return path;
}

var delvePath = WalkPath(delveMap);
Console.WriteLine($"Path: {delvePath.Count} nodes, Floor 1 through Boss -- {string.Join(" -> ", delvePath.Select(n => n.Type == MapNodeType.Boss ? "BOSS" : $"F{n.FloorIndex + 1}:{n.Type!.Value.ToString()[0]}"))}");

// Boulder (Onyx tank) fronts, Ember (Crimson DPS) mids, Sprout (Verdant healer) hangs back --
// a sane default formation, not one under test here.
var delvePlayerTeam = new List<AnimaUnit> { SampleAnimas.CreateBoulder(), SampleAnimas.CreateEmber(), SampleAnimas.CreateSprout() };
delvePlayerTeam[0].Position = 1;
delvePlayerTeam[1].Position = 2;
delvePlayerTeam[2].Position = 3;

var delvePersistentLedger = new PersistentLedger();
var delveRunLedger = new RunLedger();
var delveSimRng = new Random(DelveSeed * 31 + 1);
var delveRewardRng = new Random(DelveSeed * 31 + 2);

// Cycles deterministically through the full 11-Artifact roster (SampleArtifacts.AllFactories) --
// not a drop-rate simulation. With the 3-Artifact cap (ArtifactService.MaxArtifactsPerDelve) now
// in effect, most Delves only ever actually collect the first 3 of these; later Treasure hits are
// expected to be skipped/lost once at cap (see the Treasure case below).
var delveTreasureIndex = 0;

// Simple greedy heuristic, not real strategic play: heal self/ally when hurt if possible,
// otherwise swing the hardest-hitting affordable/usable Attack, otherwise use whatever's left.
Skill? ChooseDelveSkill(AnimaUnit anima, CombatState state)
{
    if (anima.CurrentHp <= 0) return null;

    bool UsableFromPosition(Skill s) => s.UsableFromOverride is { Length: > 0 } positions
        ? positions.Contains(anima.Position)
        : s.Range switch
        {
            AttackRange.Melee => anima.Position is 1 or 2,
            AttackRange.Ranged => anima.Position is 2 or 3,
            _ => true,
        };

    var usable = state.Hand
        .Where(s => anima.DeckSkills.Contains(s) && s.EnergyCost <= state.SharedEnergy && UsableFromPosition(s))
        .ToList();
    if (usable.Count == 0) return null;

    if (anima.CurrentHp < anima.MaxHp * 0.4)
    {
        var heal = usable.Where(s => s.Category == SkillCategory.Heal && s.BaseHeal > 0)
            .OrderByDescending(s => s.BaseHeal).FirstOrDefault();
        if (heal != null) return heal;
    }

    var attack = usable.Where(s => s.Category == SkillCategory.Attack)
        .OrderByDescending(s => s.BaseDamage).FirstOrDefault();
    if (attack != null) return attack;

    return usable.OrderByDescending(s => s.Category is SkillCategory.Heal or SkillCategory.Buff ? 1 : 0).First();
}

const int DelveRoundCap = 40;

(bool won, int rounds, bool stalemate) RunDelveFight(CombatState state, List<Artifact> ownedArtifacts, RunLedger runLedgerForChime, List<string> fightLog)
{
    var engine = new CombatEngine(state, ownedArtifacts);
    engine.ChoosePlayerSkill = ChooseDelveSkill;
    engine.OnLog = line => fightLog.Add(line);
    engine.StartCombat();

    if (runLedgerForChime.Artifacts.Any(a => a.Name == "Silent Chime"))
    {
        var frontline = state.PlayerTeam.FirstOrDefault(a => a.CurrentHp > 0);
        if (frontline != null && engine.TryActivateSilentChime(frontline, runLedgerForChime))
        {
            fightLog.Add($"  [SIM] Silent Chime pre-activated on {frontline.Id} for this fight.");
        }
    }

    var round = 0;
    while (round < DelveRoundCap && state.PlayerTeam.Any(a => a.CurrentHp > 0) && state.EnemyTeam.Any(e => e.CurrentHp > 0))
    {
        engine.RunRound();
        round++;
    }

    var won = state.EnemyTeam.All(e => e.CurrentHp <= 0);
    var lost = state.PlayerTeam.All(a => a.CurrentHp <= 0);
    return (won, round, !won && !lost);
}

string DescribeSkillForAugmentLog(Skill skill) =>
    $"dmg={skill.BaseDamage},heal={skill.BaseHeal},shield={skill.BaseShield},cost={skill.EnergyCost},target={skill.Target},extraCharges={skill.OnHitStatusExtraCharges}";

// Every skill on the team, any of the 3 Animas x any of their 4 parts (Crest included -- it's not
// in the deck, but it's still a real skill with a real Color an Ember can match).
IEnumerable<(AnimaUnit Owner, Skill Skill)> AllTeamSkills(List<AnimaUnit> team) =>
    team.SelectMany(a => new[] { (a, a.Head), (a, a.Frame), (a, a.Tail), (a, a.Crest) });

// See the top-of-section note for the policy this implements (fixed priority order, scan every
// team skill of the matching color, convert-to-Wisp if nothing qualifies). Every decision is both
// printed immediately AND accumulated into `log` (a persistent list spanning the whole Delve), so
// the post-walk summary can automatically confirm this used the real AugmentService rather than
// the old [SIM AUGMENT] placeholder -- see the summary section after the Delve loop.
void ResolveEmberDrop(AnimaColor emberColor, List<AnimaUnit> team, PersistentLedger ledger, RunLedger runLedgerForDiscount, List<string> log, string context)
{
    void Emit(string line)
    {
        log.Add(line);
        Console.WriteLine(line);
    }

    foreach (var (owner, skill) in AllTeamSkills(team).Where(ts => ts.Skill.Color == emberColor))
    {
        foreach (var augmentType in new[] { AugmentType.IncreaseEffect, AugmentType.AoEDamage, AugmentType.DecreaseCost, AugmentType.Extend })
        {
            var before = DescribeSkillForAugmentLog(skill);
            var result = AugmentService.TryApplyAugment(skill, augmentType, emberColor, ledger, runLedgerForDiscount);
            if (result.Success)
            {
                Emit($"  [AUGMENT] Applied {augmentType} to {owner.Id}'s {skill.Name} for {result.WispCost} Wisp using a dropped {emberColor} Ember ({context}). {before} -> {DescribeSkillForAugmentLog(skill)}");
                return;
            }
        }
    }

    EmberService.ConvertToWisp(ledger);
    Emit($"  [AUGMENT] No affordable/applicable {emberColor} skill to Augment right now -- converted the dropped Ember to {EmberService.ConvertToWispAmount} Wisp instead ({context}).");
}

void PrintResourceSnapshot(PersistentLedger ledger, RunLedger runLedger)
{
    Console.WriteLine($"  Wisp: {ledger.GetBalance(ResourceType.Wisp)} | EchoShard: {ledger.GetBalance(ResourceType.EchoShard)} | VesselShard: {ledger.GetBalance(ResourceType.VesselShard)} | Vessel: {ledger.GetBalance(ResourceType.Vessel)}");
    Console.WriteLine($"  Artifacts owned ({runLedger.Artifacts.Count}/{Anima.Core.Economy.ArtifactService.MaxArtifactsPerDelve}): {(runLedger.Artifacts.Count == 0 ? "none" : string.Join(", ", runLedger.Artifacts.Select(a => a.Name)))}");
}

var delveNodesVisited = new Dictionary<MapNodeType, int>();
var delveEndedEarly = false;
var delveGaps = new List<string>();
var delveAugmentLog = new List<string>();

foreach (var node in delvePath)
{
    // Type is only null for a not-yet-assigned node, which can't happen here -- every node in
    // delvePath came from MapGenerator's own already-fully-typed output.
    var nodeType = node.Type!.Value;
    var label = nodeType == MapNodeType.Boss ? "BOSS" : $"Floor {node.FloorIndex + 1} col {node.Column}";
    delveNodesVisited[nodeType] = delveNodesVisited.GetValueOrDefault(nodeType) + 1;

    Console.WriteLine();
    Console.WriteLine($"---- {label}: {nodeType} ----");
    Console.WriteLine($"  Team HP: {string.Join(", ", delvePlayerTeam.Select(a => $"{a.Id}={a.CurrentHp}/{a.MaxHp}"))}");
    PrintResourceSnapshot(delvePersistentLedger, delveRunLedger);

    switch (nodeType)
    {
        case MapNodeType.Combat:
        case MapNodeType.Elite:
        case MapNodeType.Boss:
        {
            List<Enemy> enemyTeam;
            string fightName;
            if (nodeType == MapNodeType.Boss)
            {
                enemyTeam = [SampleEnemies.CreateWardenOfTheHollow()];
                fightName = "Warden of the Hollow";
            }
            else if (nodeType == MapNodeType.Elite)
            {
                var useSentinel = delveNodesVisited[MapNodeType.Elite] % 2 == 1;
                enemyTeam = [useSentinel ? SampleEnemies.CreateSentinel() : SampleEnemies.CreateLeechMother()];
                fightName = useSentinel ? "The Sentinel" : "The Leech Mother";
            }
            else
            {
                var grovehide = SampleEnemies.CreateGrovehide();
                var quillfang = SampleEnemies.CreateQuillfang();
                quillfang.Position = 2;
                enemyTeam = [grovehide, quillfang];
                fightName = "Grovehide + Quillfang";
            }

            var combatState = new CombatState { PlayerTeam = delvePlayerTeam, EnemyTeam = enemyTeam };
            Anima.Core.Economy.ArtifactService.OnNodeVisited(delveRunLedger, delvePlayerTeam, combatState); // Withering Fang: pre-fight snipe; Sapling Charm: heal on entry, if owned

            Console.WriteLine($"  Fighting {fightName}...");
            var fightLog = new List<string>();
            var (won, rounds, stalemate) = RunDelveFight(combatState, delveRunLedger.Artifacts, delveRunLedger, fightLog);

            foreach (var line in fightLog.Where(l =>
                l.Contains("Twin Flame") || l.Contains("Silent Chime") || l.Contains("Focusing Lens")
                || l.Contains("ENRAGED") || l.Contains("PHASE 2") || l.Contains("has fallen")))
            {
                Console.WriteLine($"    {line.Trim()}");
            }

            var outcome = won ? "WIN" : stalemate ? "STALEMATE (round cap hit)" : "LOSS (team wiped)";
            Console.WriteLine($"  {fightName}: {outcome} in {rounds} rounds.");
            Console.WriteLine($"  Team HP after: {string.Join(", ", delvePlayerTeam.Select(a => $"{a.Id}={a.CurrentHp}/{a.MaxHp}"))}");

            if (stalemate) delveGaps.Add($"{label} ({fightName}): hit the {DelveRoundCap}-round safety cap without either side dying.");

            if (!won)
            {
                Console.WriteLine("  Delve ends here -- full team wipe (or stalemate), nothing coherent to continue with.");
                delveEndedEarly = true;
                goto AfterDelveWalk;
            }

            var nodeEmberDrops = new List<AnimaColor>();
            if (nodeType == MapNodeType.Boss)
            {
                Anima.Core.Economy.RewardService.GrantBossWin(delvePersistentLedger, delveRewardRng, delveRunLedger);
            }
            else if (nodeType == MapNodeType.Elite)
            {
                nodeEmberDrops = Anima.Core.Economy.RewardService.GrantEliteWin(delvePersistentLedger, delveRewardRng, delveRunLedger);
            }
            else
            {
                nodeEmberDrops = Anima.Core.Economy.RewardService.GrantCombatWin(delvePersistentLedger, delveRewardRng, delveRunLedger);
            }
            Console.WriteLine("  Rewards granted:");
            PrintResourceSnapshot(delvePersistentLedger, delveRunLedger);

            foreach (var emberColor in nodeEmberDrops)
            {
                ResolveEmberDrop(emberColor, delvePlayerTeam, delvePersistentLedger, delveRunLedger, delveAugmentLog, $"on {fightName} win");
            }
            break;
        }

        case MapNodeType.Resource:
        {
            Anima.Core.Economy.ArtifactService.OnNodeVisited(delveRunLedger, delvePlayerTeam);
            var resourceEmberDrops = Anima.Core.Economy.RewardService.GrantResourceNode(delvePersistentLedger, delveRewardRng, delveRunLedger);
            Console.WriteLine("  Resource node grants Wisp:");
            PrintResourceSnapshot(delvePersistentLedger, delveRunLedger);
            foreach (var emberColor in resourceEmberDrops)
            {
                ResolveEmberDrop(emberColor, delvePlayerTeam, delvePersistentLedger, delveRunLedger, delveAugmentLog, "on a Resource node's bonus Ember");
            }
            break;
        }

        case MapNodeType.Treasure:
        {
            Anima.Core.Economy.ArtifactService.OnNodeVisited(delveRunLedger, delvePlayerTeam);
            var newArtifact = SampleArtifacts.AllFactories[delveTreasureIndex % SampleArtifacts.AllFactories.Count]();
            delveTreasureIndex++;

            // Hard 3-Artifact cap, no swap -- a Treasure hit while already at cap is skipped/lost
            // entirely, no substitute given (an intentional punish for a wasted node).
            if (!Anima.Core.Economy.ArtifactService.HasArtifactCapacity(delveRunLedger))
            {
                Console.WriteLine($"  Treasure would grant {newArtifact.Name}, but the team is already at the {Anima.Core.Economy.ArtifactService.MaxArtifactsPerDelve}-Artifact cap -- reward lost.");
                break;
            }

            var treasureEmberDrop = Anima.Core.Economy.ArtifactService.Grant(delveRunLedger, newArtifact, delvePersistentLedger, delveSimRng);
            Console.WriteLine($"  Treasure grants Artifact: {newArtifact.Name} -- {newArtifact.Description}");
            PrintResourceSnapshot(delvePersistentLedger, delveRunLedger);
            if (treasureEmberDrop is { } treasureEmberColor)
            {
                ResolveEmberDrop(treasureEmberColor, delvePlayerTeam, delvePersistentLedger, delveRunLedger, delveAugmentLog, $"from {newArtifact.Name}'s pickup roll");
            }
            break;
        }

        case MapNodeType.Shop:
        {
            Anima.Core.Economy.ArtifactService.OnNodeVisited(delveRunLedger, delvePlayerTeam);

            // Two sections only: Rest (below) and Wares. There's no standalone "Augment a skill"
            // menu at the Shop -- Augmenting only ever triggers from an Ember in hand, either a
            // node drop (handled at its own node case) or a Wares purchase (right here).
            //
            // Wares: fresh independent stock every visit (ShopService.Roll -- no shared/depleting
            // pool across Shop nodes). Buys every offered Ember that has at least one applicable,
            // uncapped matching-Color-archetype skill somewhere on the team and is affordable,
            // and buys the offered Artifact (if any) whenever affordable -- "accept whenever
            // affordable, unconditionally" is the same placeholder decision policy as the Reforge
            // accept logic below, there being no real player-choice UI yet.
            var shopStock = ShopService.Roll(delveRunLedger, delveSimRng);
            Console.WriteLine($"  Wares stock: Ember [{string.Join(", ", shopStock.EmberOffers)}]" +
                (shopStock.ArtifactOffer != null ? $", Artifact [{shopStock.ArtifactOffer.Name}]" : ", no Artifact offer (at cap or none left)"));

            foreach (var offeredColor in shopStock.EmberOffers)
            {
                var hasUsefulTarget = AllTeamSkills(delvePlayerTeam)
                    .Any(ts => ts.Skill.Color == offeredColor && AugmentService.GetNextAugmentCost(ts.Skill) != null);
                if (!hasUsefulTarget)
                {
                    Console.WriteLine($"  Wares: skipping the {offeredColor} Ember -- no team skill of that Color could use it right now.");
                    continue;
                }

                var shopEmberCost = Anima.Core.Economy.ArtifactService.ApplyEmberCoreDiscount(EmberService.ShopPrice, delveRunLedger);
                if (EmberService.TryBuyEmber(delvePersistentLedger, delveRunLedger))
                {
                    Console.WriteLine($"  Bought 1 {offeredColor} Ember from Wares for {shopEmberCost} Wisp.");
                    ResolveEmberDrop(offeredColor, delvePlayerTeam, delvePersistentLedger, delveRunLedger, delveAugmentLog, "bought at Shop");
                }
                else
                {
                    Console.WriteLine($"  Wares: {shopEmberCost} Wisp {offeredColor} Ember on offer, but insufficient Wisp -- skipped.");
                }
            }

            if (shopStock.ArtifactOffer is { } offeredArtifact)
            {
                var artifactCost = Anima.Core.Economy.ArtifactService.ApplyEmberCoreDiscount(ShopService.ArtifactWaresPrice, delveRunLedger);
                var (artifactBought, artifactDroppedEmber) = ShopService.TryBuyArtifact(offeredArtifact, delveRunLedger, delvePersistentLedger, delveSimRng);
                if (artifactBought)
                {
                    Console.WriteLine($"  Bought Artifact from Wares: {offeredArtifact.Name} for {artifactCost} Wisp -- {offeredArtifact.Description}");
                    if (artifactDroppedEmber is { } artifactEmberColor)
                    {
                        ResolveEmberDrop(artifactEmberColor, delvePlayerTeam, delvePersistentLedger, delveRunLedger, delveAugmentLog, $"from {offeredArtifact.Name}'s pickup roll (bought at Shop)");
                    }
                }
                else
                {
                    Console.WriteLine($"  Wares: {offeredArtifact.Name} on offer for {artifactCost} Wisp, but insufficient Wisp -- skipped.");
                }
            }

            const double ShopHealPercent = 0.35; // middle of CLAUDE.md's stated ~30-40% range
            foreach (var anima in delvePlayerTeam)
            {
                var healAmount = (int)Math.Round(anima.MaxHp * ShopHealPercent);
                var before = anima.CurrentHp;
                anima.CurrentHp = Math.Min(anima.MaxHp, anima.CurrentHp + healAmount);
                Console.WriteLine($"  Shop heals {anima.Id}: {before} -> {anima.CurrentHp} HP.");
            }
            Console.WriteLine($"  Remaining Wisp: {delvePersistentLedger.GetBalance(ResourceType.Wisp)}.");
            break;
        }

        case MapNodeType.Reforge:
        {
            Anima.Core.Economy.ArtifactService.OnNodeVisited(delveRunLedger, delvePlayerTeam);

            var offer = ReforgeService.RollOffer(delveSimRng);
            var previewCost = Anima.Core.Economy.ArtifactService.ApplyEmberCoreDiscount(offer.AcceptCost, delveRunLedger);
            Console.WriteLine($"  Reforge rolls {offer.Candidate.ArchetypeName}'s {offer.Candidate.Skill.Name} ({offer.Candidate.Skill.Part}), cost {previewCost} Wisp.");

            if (!delvePersistentLedger.CanAfford(ResourceType.Wisp, previewCost))
            {
                Console.WriteLine("  Declined -- insufficient Wisp.");
                break;
            }

            var delveReforgeTarget = delvePlayerTeam.OrderBy(a => a.Position).First();
            var accepted = ReforgeService.Accept(offer, delveReforgeTarget, delvePersistentLedger, delveRunLedger);
            Console.WriteLine(accepted
                ? $"  Accepted -- {delveReforgeTarget.Id}'s {offer.Candidate.Skill.Part} is now {offer.Candidate.Skill.Name}."
                : "  Accept unexpectedly failed despite passing the affordability check.");
            if (!accepted) delveGaps.Add($"{label}: Reforge Accept failed despite CanAfford having just passed -- possible race in the cost preview vs. actual charge.");
            PrintResourceSnapshot(delvePersistentLedger, delveRunLedger);
            break;
        }
    }
}
AfterDelveWalk:

Console.WriteLine();
Console.WriteLine("================ Delve Simulation: summary ================");
Console.WriteLine($"Outcome: {(delveEndedEarly ? "ENDED EARLY (loss/stalemate)" : "REACHED THE END OF THE PATH")}");
Console.WriteLine($"Nodes visited by type: {string.Join(", ", delveNodesVisited.Select(kv => $"{kv.Key}={kv.Value}"))}");
Console.WriteLine("Final team state:");
foreach (var anima in delvePlayerTeam)
{
    Console.WriteLine($"  {anima.Id}: {anima.CurrentHp}/{anima.MaxHp} HP, Head={anima.Head.Name}(dmg {anima.Head.BaseDamage})");
}
Console.WriteLine("Final resources:");
PrintResourceSnapshot(delvePersistentLedger, delveRunLedger);
Console.WriteLine(delveGaps.Count == 0
    ? "No structural gaps/anomalies flagged during the walk (beyond the design judgment calls noted at the top of this section)."
    : "Gaps/anomalies flagged during the walk:\n  - " + string.Join("\n  - ", delveGaps));

// Confirms this run used the REAL AugmentService/EmberService, not the earlier [SIM AUGMENT] stub
// or the (now removed) per-color Ember bank -- gated on at least one genuine Augment actually
// landing (not just every drop converting to Wisp), and a hard structural check that no leftover
// [SIM AUGMENT] tag appears anywhere (it can't -- that code path no longer exists -- but asserting
// it directly, rather than just trusting the diff, is the point).
var delveAugmentAppliedCount = delveAugmentLog.Count(l => l.Contains("[AUGMENT] Applied"));
var delveAugmentConvertedCount = delveAugmentLog.Count - delveAugmentAppliedCount;
var delveNoStubTagPass = delveAugmentLog.All(l => !l.Contains("[SIM AUGMENT"));
var delveRealAugmentPass = delveAugmentLog.Count > 0 && delveAugmentAppliedCount > 0 && delveNoStubTagPass;
Console.WriteLine($"  [{(delveRealAugmentPass ? "PASS" : "FAIL")}] Delve simulation used the real AugmentService/EmberService: {delveAugmentAppliedCount} Augment(s) genuinely applied, {delveAugmentConvertedCount} Ember(s) converted to Wisp instead, 0 leftover [SIM AUGMENT] placeholder tags.");
