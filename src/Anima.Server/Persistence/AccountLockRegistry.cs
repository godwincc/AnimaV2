using System.Collections.Concurrent;

namespace Anima.Server.Persistence;

// Per-account async lock so a load-mutate-save cycle against a given account's SanctumRoster or
// PersistentLedger runs serialized within THIS process, even if two requests/connections for the
// same account overlap (e.g. the player has two browser tabs open). Anima.Core's SanctumRoster
// (plain List<Anima>) and PersistentLedger (plain Dictionary<ResourceType,int>) were both written
// assuming single-threaded, single-session access -- correctly, for an engine layer that has no
// business knowing about HTTP/SignalR concurrency. This registry is where that assumption gets
// re-established now that a database (and therefore multiple concurrent callers per account) is in
// the picture.
//
// FLAGGED LIMITATION: this is an in-process mitigation only. It does nothing if Anima.Server is
// ever scaled out to more than one instance/process sharing the same database -- two instances
// each think they hold "the" lock for an account. The EF Core optimistic-concurrency token
// (IConcurrencyStamped.Version, see AnimaDbContext) is the real cross-process safety net for that
// case: a stale write throws DbUpdateConcurrencyException instead of silently clobbering the other
// writer, but callers don't currently retry on that exception (see repository classes) -- a retry
// policy is real follow-up work if/when horizontal scale-out happens, not needed for a single-
// instance pre-launch deployment.
public sealed class AccountLockRegistry
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(Guid accountId, CancellationToken ct = default)
    {
        var sem = _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new Releaser(sem);
    }

    private sealed class Releaser(SemaphoreSlim sem) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            sem.Release();
            return ValueTask.CompletedTask;
        }
    }
}
