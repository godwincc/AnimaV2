using Anima.Core.Data;
using Anima.Core.Enums;
using Anima.Core.Weaving;

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
