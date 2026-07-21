using Anima.Core.Economy;
using Anima.Core.Enums;
using Anima.Core.Models;
using Anima.Core.Run;

namespace Anima.Server.Sessions;

// The per-connection join point between an authenticated account and the in-memory Anima.Core
// state that account is currently playing with. Anima.Core's SanctumRoster/PersistentLedger are
// loaded from the DB once when this session is created and held here for the connection's
// lifetime; every mutation is written back through SanctumRosterRepository/PersistentLedgerRepository
// immediately (write-through), so there's no separate "flush on disconnect" step.
//
// ANSWERS A FLAGGED QUESTION FROM SCOPE: DelveRun itself does NOT carry an AccountId -- Anima.Core
// stays completely unaware accounts exist. Instead, this class is the thing that scopes a DelveRun
// to one account: DelveRun/RunLedger/PersistentLedger are the SAME instances for the lifetime of one
// PlayerSession, and a PlayerSession is 1:1 with one authenticated connection's AccountId, so
// DelveEndService's Wisp-math writes always land back on THIS PersistentLedger, which
// PersistentLedgerRepository.SaveAsync always saves under THIS AccountId. The risk this guards
// against (a Delve's result getting written to the wrong account) can only happen if some future
// caller mixes PlayerSessions -- e.g. reuses one session's DelveRun/PersistentLedger reference from
// a different session/connection. Nothing in this codebase does that today.
public sealed class PlayerSession
{
    public required Guid AccountId { get; init; }
    public required string Username { get; init; }
    public required SanctumRoster Roster { get; init; }
    public required PersistentLedger Ledger { get; init; }

    // The active-team selection (Sanctum's "In team" badge), loaded from AccountEntity.
    // TeamAnimaIdsJson at session creation and kept in sync with the DB by GameHub.SetTeam.
    public List<string> TeamAnimaIds { get; set; } = new();

    // DB-backed (see PendingWeaveEntity/PendingWeaveRepository) -- a Phase 3 audit found this was
    // originally in-memory-only, meaning a dropped connection between AttemptWeave and ConfirmWeave
    // silently lost an already-paid-for Weave (Wisp/Echo Shard/WeaveCount spent, nothing to show
    // for it) and reset the guard, letting the player start and orphan another one. Now reloaded by
    // PlayerSessionRegistry.CreateAsync on every (re)connect, so ConfirmWeave stays resumable.
    public PendingWeave? PendingWeave { get; set; }

    // Deliberately in-memory only, tied to this session/connection -- NOT the same fix as
    // PendingWeave above, and ONLY for FREE (node-dropped) Ember -- a purchased one is a real,
    // paid-for pending outcome and gets PendingPurchasedEmberEntity's DB-backed treatment instead
    // (a Phase 4 audit finding; see GameHub.BuyWaresEmber). A free Ember has no stored value
    // anywhere by design (see EmberService's own comment: "nothing... ever stores an Ember
    // anywhere"), so losing an unresolved one to a dropped connection costs at most one
    // Augment/15 Wisp of upside never gained -- far below PendingWeave's stakes (a whole
    // materialized Vessel + a capped, precious WeaveCount charge) or a purchased Ember's stakes
    // (real Wisp already spent). A real queue (not a single slot), per CLAUDE.md's locked
    // pickup-flow spec: "sequential if multiple dropped -- never batched" (relevant once
    // Elite/Combat can drop up to 3 at once).
    public Queue<AnimaColor> PendingEmbers { get; } = new();

    // Deliberately in-memory only, tied to this session/connection -- see ShopVisitState's own
    // comment for why losing this to a disconnect is cosmetic, not a currency loss.
    public ShopVisitState? CurrentShopStock { get; set; }

    // Deliberately in-memory only, tied to this session/connection -- explicit scope decision (see
    // CLAUDE.md's new-scope note: "no resume, no save/load of in-progress run state"). Discarded
    // (never persisted) the moment the connection drops, per PlayerSessionRegistry.OnDisconnected.
    public DelveRun? ActiveDelveRun { get; set; }
}
