using Anima.Core.Data;
using Anima.Core.Economy;
using Anima.Core.Enums;
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
