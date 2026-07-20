namespace Anima.Core.Economy;

// The two non-Augment halves of Ember's pickup-choice flow (the Augment-application half lives on
// AugmentService.TryApplyAugment, which takes the dropped Ember's color directly rather than
// reading it from here -- there's no ledger balance to read, per ResourceType's own comment).
//
// RewardService (node drops), ArtifactService.Grant (Marked Coin), and ShopService.TryBuyEmber
// (Wares purchase) each surface an Ember as a plain AnimaColor -- once in hand, it's resolved via
// EXACTLY ONE of:
//   1. AugmentService.TryApplyAugment(skill, type, thatColor, ledger) -- "Augment now"
//   2. EmberService.ConvertToWisp(ledger) -- "Convert to Wisp"
// A Wares-bought Ember is expected to always take path 1 in practice (converting it back would be
// a guaranteed Wisp loss -- paid 25 to buy it, only 15 back), but nothing stops a caller from
// choosing path 2 -- this class doesn't distinguish where an Ember came from. Nothing here (or in
// RewardService/ShopService) ever stores an Ember anywhere; the caller (a future Run layer) is
// expected to resolve each Ember individually and sequentially before moving on, per the
// pickup-flow spec.
public static class EmberService
{
    // Flat conversion value for "Convert to Wisp" at the reward screen. Deliberately NOT run
    // through Wisp Charm -- that Artifact boosts node REWARDS, and this is a player-chosen
    // conversion of an already-dropped Ember rather than a fresh reward grant.
    public const int ConvertToWispAmount = 15;

    public static void ConvertToWisp(PersistentLedger ledger)
    {
        ledger.Add(ResourceType.Wisp, ConvertToWispAmount);
    }

    // Shop Wares price for buying 1 Ember outright, subject to Ember Core's discount same as every
    // other Wares item. The bought Ember's color comes from whichever pre-rolled ShopStock.
    // EmberOffers slot the player picked (see ShopService.Roll) -- this method only handles the
    // Wisp charge; the caller passes that slot's color straight into AugmentService.
    // TryApplyAugment right after this succeeds, same Augment-page flow as a node-dropped Ember.
    public const int ShopPrice = 25;

    // Returns false (ledger untouched) if the player can't afford it -- mirrors
    // ReforgeService.Accept's own "check affordability, only then spend" shape.
    public static bool TryBuyEmber(PersistentLedger ledger, RunLedger? runLedger = null)
    {
        var cost = ArtifactService.ApplyEmberCoreDiscount(ShopPrice, runLedger);
        return ledger.TrySpend(ResourceType.Wisp, cost);
    }
}
