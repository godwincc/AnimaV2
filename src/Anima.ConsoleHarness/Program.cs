using Anima.Core.Combat;
using Anima.Core.Data;
using Anima.Core.Economy;
using Anima.Core.Enums;
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
// None of these Wisp amounts, the per-color Ember split, the Elite shard chance, or the Boss
// either/or shard pick existed anywhere before RewardService -- see its doc comments for the
// reasoning behind each number. Four checks: Combat (exact Wisp, exact total Ember count, ~25%
// per color, no shards), Elite (exact Wisp/Ember, ~25% shard-trigger rate, never both shard types
// at once), Resource (Wisp only), and Boss (exact Wisp, guaranteed Vessel, guaranteed exactly-one
// shard fragment every time, ~50/50 split between the two shard types).

Console.WriteLine();
Console.WriteLine("================ Rewards: RewardService node-outcome grants ================");

var rewardRng = new Random(4242);

// ---- Combat win ----
const int CombatTrials = 4000;
var combatLedger = new PersistentLedger();
for (var i = 0; i < CombatTrials; i++)
{
    RewardService.GrantCombatWin(combatLedger, rewardRng);
}
var combatWispPass = combatLedger.GetBalance(ResourceType.Wisp) == CombatTrials * RewardService.CombatWinWisp;
var combatEmberTotalDraws = CombatTrials * RewardService.CombatWinEmberCount;
var combatEmberSum = combatLedger.GetBalance(ResourceType.EmberCrimson) + combatLedger.GetBalance(ResourceType.EmberOnyx)
    + combatLedger.GetBalance(ResourceType.EmberVerdant) + combatLedger.GetBalance(ResourceType.EmberAzure);
var combatNoShardsPass = combatLedger.GetBalance(ResourceType.EchoShard) == 0 && combatLedger.GetBalance(ResourceType.VesselShard) == 0
    && combatLedger.GetBalance(ResourceType.Vessel) == 0;

Console.WriteLine($"  [{(combatWispPass ? "PASS" : "FAIL")}] Combat win grants exact Wisp ({CombatTrials}x{RewardService.CombatWinWisp} = {CombatTrials * RewardService.CombatWinWisp} -> {combatLedger.GetBalance(ResourceType.Wisp)})");
Console.WriteLine($"  [{(combatEmberSum == combatEmberTotalDraws ? "PASS" : "FAIL")}] Combat win grants exactly {RewardService.CombatWinEmberCount} Ember per win ({combatEmberTotalDraws} expected total draws -> {combatEmberSum} Ember across all colors)");
ReportBucket("EmberCrimson", combatLedger.GetBalance(ResourceType.EmberCrimson), combatEmberTotalDraws, 0.25);
ReportBucket("EmberOnyx", combatLedger.GetBalance(ResourceType.EmberOnyx), combatEmberTotalDraws, 0.25);
ReportBucket("EmberVerdant", combatLedger.GetBalance(ResourceType.EmberVerdant), combatEmberTotalDraws, 0.25);
ReportBucket("EmberAzure", combatLedger.GetBalance(ResourceType.EmberAzure), combatEmberTotalDraws, 0.25);
Console.WriteLine($"  [{(combatNoShardsPass ? "PASS" : "FAIL")}] Combat win grants no Shards/Vessel");

// ---- Elite win ----
Console.WriteLine();
const int EliteTrials = 4000;
var eliteLedger = new PersistentLedger();
var eliteShardTriggerCount = 0;
var eliteMultiShardViolation = false;
for (var i = 0; i < EliteTrials; i++)
{
    var echoBefore = eliteLedger.GetBalance(ResourceType.EchoShard);
    var vesselBefore = eliteLedger.GetBalance(ResourceType.VesselShard);
    RewardService.GrantEliteWin(eliteLedger, rewardRng);
    var shardDelta = (eliteLedger.GetBalance(ResourceType.EchoShard) - echoBefore) + (eliteLedger.GetBalance(ResourceType.VesselShard) - vesselBefore);
    if (shardDelta > 1) eliteMultiShardViolation = true;
    if (shardDelta == 1) eliteShardTriggerCount++;
}
var eliteWispPass = eliteLedger.GetBalance(ResourceType.Wisp) == EliteTrials * RewardService.EliteWinWisp;
var eliteEmberTotalDraws = EliteTrials * RewardService.EliteWinEmberCount;
var eliteEmberSum = eliteLedger.GetBalance(ResourceType.EmberCrimson) + eliteLedger.GetBalance(ResourceType.EmberOnyx)
    + eliteLedger.GetBalance(ResourceType.EmberVerdant) + eliteLedger.GetBalance(ResourceType.EmberAzure);

Console.WriteLine($"  [{(eliteWispPass ? "PASS" : "FAIL")}] Elite win grants exact Wisp ({EliteTrials}x{RewardService.EliteWinWisp} = {EliteTrials * RewardService.EliteWinWisp} -> {eliteLedger.GetBalance(ResourceType.Wisp)})");
Console.WriteLine($"  [{(eliteEmberSum == eliteEmberTotalDraws ? "PASS" : "FAIL")}] Elite win grants exactly {RewardService.EliteWinEmberCount} Ember per win ({eliteEmberTotalDraws} expected total draws -> {eliteEmberSum} Ember across all colors)");
Console.WriteLine($"  [{(!eliteMultiShardViolation ? "PASS" : "FAIL")}] Elite win never grants both Shard types in the same win");
ReportBucket("Elite shard trigger", eliteShardTriggerCount, EliteTrials, RewardService.EliteShardChance);

// ---- Resource node ----
Console.WriteLine();
var resourceLedger = new PersistentLedger();
RewardService.GrantResourceNode(resourceLedger);
var resourcePass = resourceLedger.GetBalance(ResourceType.Wisp) == RewardService.ResourceNodeWisp
    && resourceLedger.GetBalance(ResourceType.EmberCrimson) == 0 && resourceLedger.GetBalance(ResourceType.EmberOnyx) == 0
    && resourceLedger.GetBalance(ResourceType.EmberVerdant) == 0 && resourceLedger.GetBalance(ResourceType.EmberAzure) == 0
    && resourceLedger.GetBalance(ResourceType.EchoShard) == 0 && resourceLedger.GetBalance(ResourceType.VesselShard) == 0
    && resourceLedger.GetBalance(ResourceType.Vessel) == 0;
Console.WriteLine($"  [{(resourcePass ? "PASS" : "FAIL")}] Resource node grants Wisp only, no Ember/Shards/Vessel ({RewardService.ResourceNodeWisp} Wisp -> {resourceLedger.GetBalance(ResourceType.Wisp)})");

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

int EmberSum(PersistentLedger l) =>
    l.GetBalance(ResourceType.EmberCrimson) + l.GetBalance(ResourceType.EmberOnyx)
    + l.GetBalance(ResourceType.EmberVerdant) + l.GetBalance(ResourceType.EmberAzure);

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
    var emberBefore = EmberSum(markedCoinLedger);
    var echoBefore = markedCoinLedger.GetBalance(ResourceType.EchoShard);
    var vesselBefore = markedCoinLedger.GetBalance(ResourceType.VesselShard);

    ArtifactService.Grant(markedCoinRunLedger, markedCoin, markedCoinLedger, markedCoinRng);

    var wispHit = markedCoinLedger.GetBalance(ResourceType.Wisp) > wispBefore;
    var emberHit = EmberSum(markedCoinLedger) > emberBefore;
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
