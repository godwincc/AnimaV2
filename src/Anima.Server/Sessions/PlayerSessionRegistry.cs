using System.Collections.Concurrent;
using Anima.Server.Persistence;

namespace Anima.Server.Sessions;

// Keyed by SignalR ConnectionId (not AccountId) -- deliberately allows the same account to open
// more than one connection (e.g. two browser tabs) rather than rejecting a second login, since
// nothing in scope asked for single-session-per-account enforcement. Each connection gets its own
// PlayerSession (own loaded Roster/Ledger/DelveRun); AccountLockRegistry is what keeps their writes
// from racing each other (see its own comment), NOT this registry.
//
// Registered as a Singleton (it must outlive any single connection/hub instantiation), which is
// exactly why it does NOT constructor-inject SanctumRosterRepository/PersistentLedgerRepository --
// both are Scoped (they hold a Scoped AnimaDbContext), and a Singleton capturing a Scoped
// dependency in its own constructor is a DI lifetime bug (the repo would be silently pinned to
// whatever the first request's scope was, or throw, depending on validation settings). GameHub
// instead receives the repos through its own (correctly Scoped-per-invocation) constructor and
// passes them into CreateAsync as plain method arguments.
public sealed class PlayerSessionRegistry
{
    private readonly ConcurrentDictionary<string, PlayerSession> _sessions = new();

    public async Task<PlayerSession> CreateAsync(
        string connectionId,
        Guid accountId,
        string username,
        SanctumRosterRepository rosterRepo,
        PersistentLedgerRepository ledgerRepo,
        AccountRepository accountRepo,
        CancellationToken ct = default)
    {
        var roster = await rosterRepo.LoadAsync(accountId, ct);
        var ledger = await ledgerRepo.LoadAsync(accountId, ct);
        var team = await accountRepo.LoadTeamAsync(accountId, ct);

        var session = new PlayerSession
        {
            AccountId = accountId,
            Username = username,
            Roster = roster,
            Ledger = ledger,
            TeamAnimaIds = team.ToList(),
        };

        _sessions[connectionId] = session;
        return session;
    }

    public PlayerSession? Get(string connectionId) => _sessions.GetValueOrDefault(connectionId);

    // No persistence step here by design: DelveRun is intentionally dropped, not saved (see
    // PlayerSession's own comment), and every SanctumRoster/PersistentLedger mutation was already
    // written through to the DB at the moment it happened, not batched for disconnect.
    public void Remove(string connectionId) => _sessions.TryRemove(connectionId, out _);
}
