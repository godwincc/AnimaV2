using Anima.Core.Enums;
using Anima.Core.Map;
using Anima.Core.Models;

namespace Anima.Server.Sessions;

// Per-connection cache of a Shop node's rolled Wares stock -- ensures "each Shop node rolls its
// own independent stock on entry" (CLAUDE.md's locked Shop spec) means exactly that: rolled ONCE
// on the first interaction with the node (Rest or Wares, whichever comes first -- see GameHub.
// EnsureShopVisited), held fixed for the rest of that visit, never re-rolled on a repeat read.
// EmberSlots entries go null once bought (consumed); ArtifactOffer goes null the same way.
//
// Deliberately in-memory only, tied to this connection -- NOT the same class of gap
// PendingPurchasedEmberEntity fixes. Losing this to a disconnect mid-visit just means the next
// GetShopStock call rerolls fresh stock for the same node: cosmetic (a not-yet-bought offer gets
// replaced by a new random one), not a currency loss -- anything actually PAID for by the time of
// the disconnect is already durable via PendingPurchasedEmberEntity/AccountArtifactStatEntity.
public sealed class ShopVisitState
{
    public required MapNode Node { get; init; }
    public required List<AnimaColor?> EmberSlots { get; init; }
    public Artifact? ArtifactOffer { get; set; }
}
