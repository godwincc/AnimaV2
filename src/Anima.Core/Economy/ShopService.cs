using Anima.Core.Data;
using Anima.Core.Enums;
using Anima.Core.Models;
using AnimaUnit = Anima.Core.Models.Anima;

namespace Anima.Core.Economy;

// A single Shop node's stock, rolled once on node entry -- see ShopService.Roll. EmberOffers is
// always exactly EmberOfferCount colors (duplicates allowed, each an independent roll).
// ArtifactOffer is null if the roll skipped it (player already at ArtifactService.
// MaxArtifactsPerDelve, or -- purely defensively -- if the player somehow already holds all 11).
public sealed record ShopStock(IReadOnlyList<AnimaColor> EmberOffers, Artifact? ArtifactOffer);

// Rolls a Shop node's Wares stock and resolves purchases from it, plus Rest (the Shop's other
// section). Deliberately stateless/per-visit -- "each Shop node rolls its own independent stock
// on entry, no shared/depleting pool across multiple Shop nodes in the same Delve" is satisfied
// simply by calling Roll fresh every time a Shop node is entered, nothing persisted between
// visits.
public static class ShopService
{
    public const int EmberOfferCount = 3;

    private static readonly AnimaColor[] EmberColors =
        [AnimaColor.Crimson, AnimaColor.Onyx, AnimaColor.Verdant, AnimaColor.Azure];

    // Wisp price for the Wares Artifact slot. Not specified anywhere (CLAUDE.md only ever said
    // "Artifacts for sale with Wisp prices," no number) -- picked deliberately well above Reforge's
    // 80-Wisp ceiling and Augment's 50-Wisp top tier, since a whole Artifact is a much bigger,
    // permanent-for-the-Delve power spike than either. First-pass number, needs tuning like every
    // other Wisp amount in RewardService -- flag to the user if a different price was intended.
    public const int ArtifactWaresPrice = 200;

    // Rolls a fresh stock: EmberOfferCount independent random-color Ember offers, plus 1 Artifact
    // offer picked uniformly from the 11 EXCLUDING whatever the player currently holds this run --
    // null if the player is already at the Artifact cap (per ArtifactService.MaxArtifactsPerDelve)
    // or, purely as a defensive fallback, if every Artifact is already held.
    public static ShopStock Roll(RunLedger runLedger, Random rng)
    {
        var emberOffers = new List<AnimaColor>(EmberOfferCount);
        for (var i = 0; i < EmberOfferCount; i++)
        {
            emberOffers.Add(EmberColors[rng.Next(EmberColors.Length)]);
        }

        Artifact? artifactOffer = null;
        if (ArtifactService.HasArtifactCapacity(runLedger))
        {
            var heldNames = runLedger.Artifacts.Select(a => a.Name).ToHashSet();
            var available = SampleArtifacts.AllFactories.Select(f => f()).Where(a => !heldNames.Contains(a.Name)).ToList();
            if (available.Count > 0)
            {
                artifactOffer = available[rng.Next(available.Count)];
            }
        }

        return new ShopStock(emberOffers, artifactOffer);
    }

    // Buys the offered Artifact outright -- same "check affordability, only then spend" shape as
    // ReforgeService.Accept. Ember Core's discount applies, same as every other Wares price.
    // Returns the dropped Ember color from the Artifact's own OnPickup hook (e.g. buying Marked
    // Coin still fires its one-time bonus roll), same as ArtifactService.Grant itself.
    public static (bool Success, AnimaColor? DroppedEmber) TryBuyArtifact(Artifact artifact, RunLedger runLedger, PersistentLedger ledger, Random rng)
    {
        var cost = ArtifactService.ApplyEmberCoreDiscount(ArtifactWaresPrice, runLedger);
        if (!ledger.TrySpend(ResourceType.Wisp, cost)) return (false, null);

        var droppedEmber = ArtifactService.Grant(runLedger, artifact, ledger, rng);
        return (true, droppedEmber);
    }

    // Rest's flat team-wide heal. Not specified anywhere beyond CLAUDE.md's own "heal 40% max HP
    // for Wisp" -- neither the exact Wisp price nor whether it targets the whole team or one
    // chosen Anima is locked. Picked WHOLE TEAM (matching Sapling Charm's own "heals the whole
    // team" precedent -- see ArtifactService.OnNodeVisited -- and there's no target-picker UI in
    // the locked Shop screen design), repeatable as many times as affordable (no stated
    // once-per-visit cap), no revive (matches the game's general "no partial-death/revival"
    // rule -- only currently-living members heal). RestWispCost=40 is a first-pass number picked
    // to loosely echo the 40% heal figure -- needs tuning like every other Wisp amount in
    // RewardService. Flag to the user if a different price or per-Anima targeting was intended.
    public const double RestHealPercent = 0.40;
    public const int RestWispCost = 40;

    // Same "check affordability, only then spend" shape as TryBuyArtifact/EmberService.TryBuyEmber.
    // Ember Core's discount applies here too, for consistency with every other Wisp-costing Shop
    // action -- Ember Core's own flavor text only names "Reforge and Augment," but the CODE already
    // applies it more broadly (Wares' Ember/Artifact purchases too, see ApplyEmberCoreDiscount's
    // own comment) -- a pre-existing scope choice, not something introduced here.
    public static bool TryRest(List<AnimaUnit> team, PersistentLedger ledger, RunLedger? runLedger = null)
    {
        var cost = ArtifactService.ApplyEmberCoreDiscount(RestWispCost, runLedger);
        if (!ledger.TrySpend(ResourceType.Wisp, cost)) return false;

        foreach (var anima in team.Where(a => a.CurrentHp > 0))
        {
            var healAmount = (int)Math.Round(anima.MaxHp * RestHealPercent);
            anima.CurrentHp = Math.Min(anima.MaxHp, anima.CurrentHp + healAmount);
        }

        return true;
    }
}
