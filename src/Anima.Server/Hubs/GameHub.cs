using Anima.Core.Map;
using Anima.Core.Run;
using Anima.Server.Auth;
using Anima.Server.Persistence;
using Anima.Server.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using AnimaUnit = Anima.Core.Models.Anima;

namespace Anima.Server.Hubs;

// Authenticated SignalR entry point -- proves the auth + DB-persistence foundation end to end
// (connect -> DB-loaded roster/ledger -> in-memory DelveRun tied to this connection) rather than
// porting the entire game surface (Weaving/Combat/Shop/Reforge/etc.) onto hub methods, which is
// real follow-up work once this foundation is in place, not part of this pass.
[Authorize]
public class GameHub(PlayerSessionRegistry sessions, SanctumRosterRepository rosterRepo, PersistentLedgerRepository ledgerRepo) : Hub
{
    private Guid AccountId =>
        Guid.Parse(Context.User!.FindFirst(JwtTokenService.AccountIdClaimType)!.Value);

    private string Username => Context.User!.Identity!.Name ?? Context.User!.FindFirst("unique_name")!.Value;

    private PlayerSession Session =>
        sessions.Get(Context.ConnectionId) ?? throw new HubException("Session not initialized.");

    public override async Task OnConnectedAsync()
    {
        await sessions.CreateAsync(Context.ConnectionId, AccountId, Username, rosterRepo, ledgerRepo);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // DelveRun (if any) is simply dropped here, never persisted -- deliberate scope decision,
        // see PlayerSession's own comment. Roster/Ledger need no flush: every mutation already wrote
        // through to the DB at the moment it happened.
        sessions.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task<IReadOnlyList<AnimaSummary>> GetRoster()
    {
        var summaries = Session.Roster.Animas
            .Select(a => new AnimaSummary(a.Id, a.Name, a.Color.ToString(), a.Gen, a.WeaveCount, a.CurrentHp, a.MaxHp))
            .ToList();
        return Task.FromResult<IReadOnlyList<AnimaSummary>>(summaries);
    }

    public Task<LedgerSnapshot> GetLedger()
    {
        var balances = Enum.GetValues<Core.Economy.ResourceType>()
            .ToDictionary(t => t.ToString(), t => Session.Ledger.GetBalance(t));
        return Task.FromResult(new LedgerSnapshot(balances));
    }

    // Minimal team-selection: looks the 3 requested Animas up in this account's already-loaded
    // roster (no cross-account lookup possible, since Session.Roster only ever contains rows this
    // account's SanctumRosterRepository.LoadAsync returned) and starts a fresh DelveRun exactly the
    // way DelveRun.Start's own doc comment describes -- Team must be these exact AnimaUnit
    // instances, never a rebuilt/cloned list, so HP attrition keeps working (see CLAUDE.md's own
    // gotcha).
    public Task<DelveStatus> StartDelve(StartDelveRequest request)
    {
        var team = request.TeamAnimaIds
            .Select(id => Session.Roster.FindById(id) ?? throw new HubException($"Anima {id} not found in this account's roster."))
            .ToList();

        var map = MapGenerator.Generate();
        Session.ActiveDelveRun = DelveRun.Start(map, team, Session.Ledger);

        return Task.FromResult(BuildStatus(Session.ActiveDelveRun));
    }

    public Task<DelveStatus> GetDelveStatus()
    {
        if (Session.ActiveDelveRun is null) throw new HubException("No active Delve for this connection.");
        return Task.FromResult(BuildStatus(Session.ActiveDelveRun));
    }

    private static DelveStatus BuildStatus(DelveRun run)
    {
        NodeRef ToRef(MapNode n) => new(n.FloorIndex, n.Column, n.Type?.ToString());

        return new DelveStatus(
            run.CurrentNode is null ? null : ToRef(run.CurrentNode),
            run.AvailableNodes.Select(ToRef).ToList(),
            run.WispEarnedSoFar);
    }
}
