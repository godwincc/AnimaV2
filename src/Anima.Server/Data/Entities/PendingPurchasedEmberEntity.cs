namespace Anima.Server.Data.Entities;

// Phase 4 audit finding: PlayerSession.PendingEmbers (Phase 3) is deliberately in-memory only,
// justified because a free node-dropped Ember has no stored value to lose on disconnect. Shop's
// Wares sells Ember for real Wisp (EmberService.TryBuyEmber spends it immediately, before the
// Augment/Convert choice is ever made) -- that's the SAME shape as the pending-Weave bug (currency
// already spent, resolution still pending), not the "nothing to lose" case PendingEmbers covers.
// One row per unresolved purchased Ember (NOT unique per account -- unlike PendingWeaveEntity,
// more than one can be pending at once: a single Shop visit can sell up to 3). GameHub.
// BuyWaresEmber adds a row; ConvertPendingEmberToWisp/AugmentPendingEmber remove the oldest one
// first, before ever touching the free in-memory queue -- see GameHub's own comment for why paid
// ones resolve first.
public class PendingPurchasedEmberEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string Color { get; set; }
    public DateTime PurchasedAtUtc { get; set; }
    public int Version { get; set; }
}
