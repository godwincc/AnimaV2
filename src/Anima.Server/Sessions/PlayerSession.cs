using Anima.Core.Economy;
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

    // Deliberately in-memory only, tied to this session/connection -- explicit scope decision (see
    // CLAUDE.md's new-scope note: "no resume, no save/load of in-progress run state"). Discarded
    // (never persisted) the moment the connection drops, per PlayerSessionRegistry.OnDisconnected.
    public DelveRun? ActiveDelveRun { get; set; }
}
