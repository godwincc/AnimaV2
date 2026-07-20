using Anima.Core.Data;
using Anima.Core.Enums;
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
public class GameHub(
    PlayerSessionRegistry sessions,
    SanctumRosterRepository rosterRepo,
    PersistentLedgerRepository ledgerRepo,
    AccountRepository accountRepo,
    AccountArtifactStatRepository artifactStatsRepo) : Hub
{
    private Guid AccountId =>
        Guid.Parse(Context.User!.FindFirst(JwtTokenService.AccountIdClaimType)!.Value);

    private string Username => Context.User!.Identity!.Name ?? Context.User!.FindFirst("unique_name")!.Value;

    private PlayerSession Session =>
        sessions.Get(Context.ConnectionId) ?? throw new HubException("Session not initialized.");

    public override async Task OnConnectedAsync()
    {
        await sessions.CreateAsync(Context.ConnectionId, AccountId, Username, rosterRepo, ledgerRepo, accountRepo);
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
        var team = new HashSet<string>(Session.TeamAnimaIds);
        var summaries = Session.Roster.Animas
            .Select(a => new AnimaSummary(
                a.Id, a.Name, a.Color.ToString(), a.Gen, a.WeaveCount, a.CurrentHp, a.MaxHp,
                team.Contains(a.Id), BuildParts(a)))
            .ToList();
        return Task.FromResult<IReadOnlyList<AnimaSummary>>(summaries);
    }

    private static IReadOnlyList<AnimaPartSummary> BuildParts(AnimaUnit a) =>
    [
        new AnimaPartSummary(nameof(Part.Head), a.Head.Name, a.Head.Category.ToString()),
        new AnimaPartSummary(nameof(Part.Frame), a.Frame.Name, a.Frame.Category.ToString()),
        new AnimaPartSummary(nameof(Part.Tail), a.Tail.Name, a.Tail.Category.ToString()),
        new AnimaPartSummary(nameof(Part.Crest), a.Crest.Name, a.Crest.Category.ToString()),
    ];

    // Persists the Sanctum "In team" selection to AccountEntity.TeamAnimaIdsJson and updates the
    // in-memory session copy GetRoster reads from. At most 3 (a "3-Anima team" per CLAUDE.md), no
    // duplicates, every Id must resolve in this account's already-loaded roster -- same
    // no-cross-account-lookup guarantee StartDelve's team lookup already relies on.
    public async Task<IReadOnlyList<string>> SetTeam(string[] animaIds)
    {
        if (animaIds.Length > 3) throw new HubException("A team can have at most 3 Anima.");
        if (animaIds.Distinct().Count() != animaIds.Length) throw new HubException("Team cannot contain duplicate Anima.");
        foreach (var id in animaIds)
        {
            if (Session.Roster.FindById(id) is null)
                throw new HubException($"Anima {id} not found in this account's roster.");
        }

        Session.TeamAnimaIds = animaIds.ToList();
        await accountRepo.SaveTeamAsync(AccountId, Session.TeamAnimaIds);
        return Session.TeamAnimaIds;
    }

    public Task<LedgerSnapshot> GetLedger()
    {
        var balances = Enum.GetValues<Core.Economy.ResourceType>()
            .ToDictionary(t => t.ToString(), t => Session.Ledger.GetBalance(t));
        return Task.FromResult(new LedgerSnapshot(balances));
    }

    // The Collection screen's Artifact list: the full 12-Artifact catalog (SampleArtifacts, the
    // same "the full Artifact roster" source ShopService/the Delve simulation already use) joined
    // against this account's discovered/won-with stats. See AccountArtifactStatEntity's own
    // comment -- every account reads all-undiscovered today, since nothing yet writes to that
    // table (Treasure/Shop pickup and Boss-victory resolution aren't ported onto GameHub yet).
    public async Task<IReadOnlyList<ArtifactSummary>> GetArtifactCollection()
    {
        var stats = await artifactStatsRepo.LoadAsync(AccountId);

        return SampleArtifacts.AllFactories
            .Select(factory => factory())
            .Select(artifact =>
            {
                stats.TryGetValue(artifact.Name, out var stat);
                return new ArtifactSummary(artifact.Name, artifact.Description, stat is not null, stat?.DelvesWonWithCount ?? 0);
            })
            .ToList();
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
