using Anima.Core.Combat;
using Anima.Core.Economy;
using Anima.Core.Enums;
using Anima.Core.Map;
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

    // DB-backed (see PendingBossHatchEntity/PendingBossHatchRepository) -- same treatment as
    // PendingWeave above and for the same reason: a Boss Victory's guaranteed hatched Anima is
    // already-granted-in-substance (the fight is won) the instant it's rolled, so losing the
    // unresolved genome to a dropped connection before ConfirmBossHatch names it would be a real,
    // one-per-Boss-clear loss. Reloaded by PlayerSessionRegistry.CreateAsync on every (re)connect.
    public PendingBossHatch? PendingBossHatch { get; set; }

    // DB-backed (see PendingStarterRevealEntity/PendingStarterRevealRepository) -- same treatment
    // as PendingWeave/PendingBossHatch above: a fresh account's 3 rolled starter Anima are already
    // committed-in-substance the moment AuthService.RegisterAsync rolls them, so losing them to a
    // disconnect before ConfirmStarterAnima names all 3 would leave a brand-new account permanently
    // stuck with zero playable Anima. Reloaded by PlayerSessionRegistry.CreateAsync on every
    // (re)connect -- null once all 3 slots are named.
    public PendingStarterReveal? PendingStarterReveal { get; set; }

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

    // Reforge's own idempotent-first-visit marker (NEW, this session's map-odds reintroduction) --
    // mirrors CurrentShopStock's "fire ArtifactService.OnNodeVisited exactly once per visit"
    // shape, just with no stock to roll/cache: the browse-and-pick flow is multiple hub calls
    // (color -> skill -> target -> Accept/Decline), so this tracks whether OnNodeVisited already
    // fired for the CURRENT Reforge node (reference-compared), not whether any particular action
    // has happened yet. No explicit reset needed -- moving to a different node (Reforge or not)
    // means this reference simply stops matching run.CurrentNode, so the check fails open
    // correctly on its own.
    public MapNode? ReforgeVisitedNode { get; set; }

    // Deliberately in-memory only, tied to this session/connection -- explicit scope decision (see
    // CLAUDE.md's new-scope note: "no resume, no save/load of in-progress run state"). Discarded
    // (never persisted) the moment the connection drops, per PlayerSessionRegistry.OnDisconnected.
    public DelveRun? ActiveDelveRun { get; set; }

    // Deliberately in-memory only, same lifetime as ActiveDelveRun above -- NOT the PendingWeave/
    // PendingPurchasedEmber DB-backed treatment. Reasoning: ActiveDelveRun (the enclosing container
    // a Combat node lives inside) is ALREADY in-memory-only and already lost on disconnect today --
    // persisting CombatState to a DB while its own enclosing DelveRun does not would be a strange
    // half-measure (a resumed fight pointing at a Delve that no longer exists). Nothing valuable is
    // uniquely at risk either: HP loss taken mid-fight is already durable (CombatState.PlayerTeam IS
    // ActiveDelveRun.Team -- the same Anima instances SanctumRosterRepository saves after every
    // action), and Phase 5a grants no rewards/spends no currency to enter or resolve a fight, unlike
    // a paid-for Weave or a bought Ember. Losing this to a disconnect costs at most "redo this one
    // fight" (once Combat/Boss support restarting an interrupted node -- not itself a Phase 5a
    // concern), not real economic value already committed.
    public CombatState? ActiveCombat { get; set; }
}
