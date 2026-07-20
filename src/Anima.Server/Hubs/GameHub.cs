using Anima.Core.Data;
using Anima.Core.Economy;
using Anima.Core.Enums;
using Anima.Core.Map;
using Anima.Core.Models;
using Anima.Core.Run;
using Anima.Core.Weaving;
using Anima.Server.Auth;
using Anima.Server.Persistence;
using Anima.Server.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using AnimaUnit = Anima.Core.Models.Anima;

namespace Anima.Server.Hubs;

// Authenticated SignalR entry point. Sanctum/Collection, Weaving, and Resource/Treasure Delve
// nodes are ported as of this session; Combat/Shop/Reforge/Boss are not yet -- see CLAUDE.md's
// Server / Accounts / Auth section for the current real method-surface breakdown.
[Authorize]
public class GameHub(
    PlayerSessionRegistry sessions,
    SanctumRosterRepository rosterRepo,
    PersistentLedgerRepository ledgerRepo,
    AccountRepository accountRepo,
    AccountArtifactStatRepository artifactStatsRepo,
    PendingWeaveRepository pendingWeaveRepo) : Hub
{
    private Guid AccountId =>
        Guid.Parse(Context.User!.FindFirst(JwtTokenService.AccountIdClaimType)!.Value);

    private string Username => Context.User!.Identity!.Name ?? Context.User!.FindFirst("unique_name")!.Value;

    private PlayerSession Session =>
        sessions.Get(Context.ConnectionId) ?? throw new HubException("Session not initialized.");

    public override async Task OnConnectedAsync()
    {
        await sessions.CreateAsync(Context.ConnectionId, AccountId, Username, rosterRepo, ledgerRepo, accountRepo, pendingWeaveRepo);
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
        var summaries = Session.Roster.Animas.Select(ToSummary).ToList();
        return Task.FromResult<IReadOnlyList<AnimaSummary>>(summaries);
    }

    private AnimaSummary ToSummary(AnimaUnit a) => new(
        a.Id, a.Name, a.Color.ToString(), a.Gen, a.WeaveCount, a.CurrentHp, a.MaxHp,
        Session.TeamAnimaIds.Contains(a.Id), BuildParts(a));

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
        var balances = Enum.GetValues<ResourceType>()
            .ToDictionary(t => t.ToString(), t => Session.Ledger.GetBalance(t));
        return Task.FromResult(new LedgerSnapshot(balances));
    }

    // The Collection screen's Artifact list: the full 12-Artifact catalog (SampleArtifacts, the
    // same "the full Artifact roster" source ShopService/the Delve simulation already use) joined
    // against this account's discovered/won-with stats. Discovered is now real for anything ever
    // granted by ClaimTreasureNode (see AccountArtifactStatRepository.RecordDiscoveryAsync);
    // DelvesWonWith is still always 0 -- that write path needs Boss-victory resolution (Phase 5).
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

    // Anima Profile's dedicated read: this account's roster is already fully loaded in-memory
    // (Session.Roster), so Parent/Echo-Twin NAMES are resolved here too rather than making the
    // client issue a second lookup per link. GenomeFactory.CreateGenome is the single "genome for
    // any roster Anima" entry point (real stored R1/R2 for Weave/Boss-hatch, the synthesized
    // placeholder for the starter trio) -- see its own comment.
    public Task<AnimaDetail> GetAnimaDetail(string animaId)
    {
        var anima = Session.Roster.FindById(animaId) ?? throw new HubException($"Anima {animaId} not found in this account's roster.");
        var genome = GenomeFactory.CreateGenome(anima);

        string? ResolveName(string? id) => id is null ? null : Session.Roster.FindById(id)?.Name;

        IReadOnlyList<PartGenomeSummary> parts =
        [
            ToPartGenomeSummary(nameof(Part.Head), genome.Head),
            ToPartGenomeSummary(nameof(Part.Frame), genome.Frame),
            ToPartGenomeSummary(nameof(Part.Tail), genome.Tail),
            ToPartGenomeSummary(nameof(Part.Crest), genome.Crest),
        ];

        return Task.FromResult(new AnimaDetail(
            anima.Id, anima.Name, anima.Color.ToString(), anima.Gen, anima.WeaveCount, anima.CurrentHp, anima.MaxHp,
            Session.TeamAnimaIds.Contains(anima.Id), parts,
            anima.ParentAId, ResolveName(anima.ParentAId),
            anima.ParentBId, ResolveName(anima.ParentBId),
            anima.EchoTwinId, ResolveName(anima.EchoTwinId)));
    }

    private static SkillSummary ToSkillSummary(Skill s) => new(s.Name, s.Category.ToString(), s.Color?.ToString() ?? "");

    private static PartGenomeSummary ToPartGenomeSummary(string part, PartGenome genome) =>
        new(part, ToSkillSummary(genome.Dominant), ToSkillSummary(genome.R1), ToSkillSummary(genome.R2));

    private static WeaveGenomePreview ToPreview(WeavingResult result) =>
        new(result.Genome.Color.ToString(), result.HybridTriggered,
        [
            ToPartGenomeSummary(nameof(Part.Head), result.Genome.Head),
            ToPartGenomeSummary(nameof(Part.Frame), result.Genome.Frame),
            ToPartGenomeSummary(nameof(Part.Tail), result.Genome.Tail),
            ToPartGenomeSummary(nameof(Part.Crest), result.Genome.Crest),
        ]);

    // Runs the real Weave roll (and its Wisp/Echo Shard cost + WeaveCount charge) immediately --
    // only the resulting Vessel(s)' own roster row waits for ConfirmWeave's mandatory naming step,
    // per the Anima Reveal screen's contract. Refuses to start a second Weave while one is still
    // unnamed (see PendingWeave's own comment for why: silently overwriting it would orphan an
    // already-paid-for roll).
    public async Task<WeaveRevealSnapshot> AttemptWeave(AttemptWeaveRequest request)
    {
        if (Session.PendingWeave is not null)
            throw new HubException("A Weave is already pending a name -- confirm it before starting another.");

        if (request.ParentAId == request.ParentBId)
            throw new HubException("Cannot Weave an Anima with itself.");

        var parentA = Session.Roster.FindById(request.ParentAId) ?? throw new HubException($"Anima {request.ParentAId} not found in this account's roster.");
        var parentB = Session.Roster.FindById(request.ParentBId) ?? throw new HubException($"Anima {request.ParentBId} not found in this account's roster.");

        var genomeA = GenomeFactory.CreateGenome(parentA);
        var genomeB = GenomeFactory.CreateGenome(parentB);

        var result = WeavingService.AttemptWeave(parentA, parentB, genomeA, genomeB, Session.Ledger, Random.Shared, spendEchoShards: request.SpendEchoShards);
        if (!result.Success)
            throw new HubException($"Weave rejected: {result.RejectionReason}.");

        // Committed: WeavingService.AttemptWeave already spent Wisp/Echo Shards from Session.Ledger
        // and incremented both parents' WeaveCount in place -- write through immediately, same as
        // every other real economic effect in this hub (not gated behind ConfirmWeave).
        await rosterRepo.SaveAnimaAsync(AccountId, parentA);
        await rosterRepo.SaveAnimaAsync(AccountId, parentB);
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);

        Session.PendingWeave = new PendingWeave
        {
            ParentAId = parentA.Id,
            ParentBId = parentB.Id,
            WispCost = result.WispCost,
            Primary = result.Primary!,
            Twin = result.Twin,
        };
        // Persisted, not just held in-memory on Session: see PendingWeaveEntity's own comment --
        // a dropped connection between AttemptWeave and ConfirmWeave used to silently lose this
        // already-paid-for roll entirely (Phase 3 audit finding). Loaded back by
        // PlayerSessionRegistry.CreateAsync on the next (re)connect.
        await pendingWeaveRepo.SaveAsync(AccountId, Session.PendingWeave);

        return new WeaveRevealSnapshot(
            result.WispCost, result.EchoTriggered,
            ToPreview(result.Primary!),
            result.Twin is null ? null : ToPreview(result.Twin));
    }

    // The mandatory naming step: materializes the pending Weave's Primary (and Twin, if Echo
    // triggered) via AnimaMaterializationService -- which now correctly carries R1/R2 through
    // (see its own comment) -- cross-links a Twin pair's EchoTwinId, and persists both only now,
    // never before this Confirm.
    public async Task<WeaveConfirmResult> ConfirmWeave(ConfirmWeaveRequest request)
    {
        var pending = Session.PendingWeave ?? throw new HubException("No pending Weave to confirm.");

        if (string.IsNullOrWhiteSpace(request.PrimaryName))
            throw new HubException("A name is required.");
        if (pending.Twin is not null && string.IsNullOrWhiteSpace(request.TwinName))
            throw new HubException("This Weave produced an Echo Twin -- both Vessels need a name.");

        var parentA = Session.Roster.FindById(pending.ParentAId) ?? throw new HubException("Parent A no longer in this account's roster.");
        var parentB = Session.Roster.FindById(pending.ParentBId) ?? throw new HubException("Parent B no longer in this account's roster.");

        var primary = AnimaMaterializationService.Create(pending.Primary, parentA, parentB, request.PrimaryName, Session.Roster);

        AnimaUnit? twin = null;
        if (pending.Twin is not null)
        {
            twin = AnimaMaterializationService.Create(pending.Twin, parentA, parentB, request.TwinName!, Session.Roster);
            primary.EchoTwinId = twin.Id;
            twin.EchoTwinId = primary.Id;
        }

        await rosterRepo.SaveAnimaAsync(AccountId, primary);
        if (twin is not null) await rosterRepo.SaveAnimaAsync(AccountId, twin);

        Session.PendingWeave = null;
        await pendingWeaveRepo.DeleteAsync(AccountId);

        return new WeaveConfirmResult(ToSummary(primary), twin is null ? null : ToSummary(twin));
    }

    // Lets a client that just reconnected re-render the Anima Reveal screen for a Weave that was
    // already resolved (and paid for) before the disconnect -- without this, ConfirmWeave would be
    // callable but the player would be naming a genome they can no longer see. Null when nothing
    // is pending.
    public Task<WeaveRevealSnapshot?> GetPendingWeave()
    {
        var pending = Session.PendingWeave;
        if (pending is null) return Task.FromResult<WeaveRevealSnapshot?>(null);

        return Task.FromResult<WeaveRevealSnapshot?>(new WeaveRevealSnapshot(
            pending.WispCost, pending.Twin is not null,
            ToPreview(pending.Primary),
            pending.Twin is null ? null : ToPreview(pending.Twin)));
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

    // The traversal primitive Resource/Treasure (and every future node type) need -- nothing
    // exposed this before (a Phase 3 audit finding: DelveRun.TryMoveTo existed but no hub method
    // ever called it). Resolves the client's target against DelveRun.AvailableNodes by
    // (FloorIndex, Column), the same identity DelveStatus.AvailableNodes already reports, so a raw
    // MapNode reference never has to cross the wire.
    public Task<DelveStatus> MoveToNode(MoveToNodeRequest request)
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        var node = run.AvailableNodes.FirstOrDefault(n => n.FloorIndex == request.FloorIndex && n.Column == request.Column)
            ?? throw new HubException("That node is not currently available to move to.");

        run.TryMoveTo(node);

        return Task.FromResult(BuildStatus(run));
    }

    // Shared guard for every node-resolution method (Collect/Claim/...): confirms there's a node
    // to resolve, that it's the expected type, and -- the anti-double-claim check the Phase 3 audit
    // specifically asked for -- that it hasn't already been cleared. Node-clearing itself
    // (DelveRun.MarkCurrentNodeCleared) happens INSIDE each resolution method, in the same call
    // that grants the reward, never left for the client to report separately -- a second Collect/
    // Claim call on the same node fails here, before anything is granted a second time.
    private static void RequireUnclearedNode(DelveRun run, MapNodeType expected)
    {
        var node = run.CurrentNode ?? throw new HubException("Not currently standing on a node.");
        if (node.Type != expected) throw new HubException($"Current node is {node.Type?.ToString() ?? "untyped"}, not {expected}.");
        if (run.ClearedNodes.Contains(node)) throw new HubException("This node has already been cleared.");
    }

    // Resource's Collect action: +30 Wisp (Wisp Charm-adjusted) plus a 15% chance of 1 bonus Ember,
    // queued for pickup resolution (see ConvertPendingEmberToWisp/AugmentPendingEmber) same as any
    // other Ember drop. ArtifactService.OnNodeVisited runs first (Withering Fang/Sapling Charm),
    // matching every non-combat node's behavior in the existing Delve-simulation reference.
    public async Task<CollectResourceResult> CollectResourceNode()
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        RequireUnclearedNode(run, MapNodeType.Resource);

        ArtifactService.OnNodeVisited(run.RunLedger, run.Team);
        foreach (var member in run.Team) await rosterRepo.SaveAnimaAsync(AccountId, member);

        var wispBefore = Session.Ledger.GetBalance(ResourceType.Wisp);
        var emberDrops = RewardService.GrantResourceNode(Session.Ledger, Random.Shared, run.RunLedger);
        var wispGranted = Session.Ledger.GetBalance(ResourceType.Wisp) - wispBefore;

        run.MarkCurrentNodeCleared();
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);

        foreach (var color in emberDrops) Session.PendingEmbers.Enqueue(color);

        return new CollectResourceResult(wispGranted, Session.PendingEmbers.Select(c => c.ToString()).ToList());
    }

    // Treasure's Claim action: reveals one uniformly-random Artifact from the full 12-Artifact
    // catalog, skipped/lost if the account is already at the 3-Artifact cap. The node is marked
    // cleared EITHER WAY (even on a cap-loss) -- per ArtifactService's own "intentional punish for
    // a wasted node" comment, so a lost-to-cap claim can't just be retried by revisiting. First
    // real write to AccountArtifactStatEntity's "discovered" column (Phase 1 built the table
    // read-only) -- the "won a Delve while held" count is untouched here, still needs Boss-victory
    // resolution (Phase 5).
    public async Task<ClaimTreasureResult> ClaimTreasureNode()
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        RequireUnclearedNode(run, MapNodeType.Treasure);

        ArtifactService.OnNodeVisited(run.RunLedger, run.Team);
        foreach (var member in run.Team) await rosterRepo.SaveAnimaAsync(AccountId, member);

        run.MarkCurrentNodeCleared();

        if (!ArtifactService.HasArtifactCapacity(run.RunLedger))
        {
            return new ClaimTreasureResult(null, null, true, Session.PendingEmbers.Select(c => c.ToString()).ToList());
        }

        var artifact = SampleArtifacts.AllFactories[Random.Shared.Next(SampleArtifacts.AllFactories.Count)]();
        var droppedEmber = ArtifactService.Grant(run.RunLedger, artifact, Session.Ledger, Random.Shared);

        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);
        await artifactStatsRepo.RecordDiscoveryAsync(AccountId, artifact.Name);

        if (droppedEmber is { } color) Session.PendingEmbers.Enqueue(color);

        return new ClaimTreasureResult(artifact.Name, artifact.Description, false, Session.PendingEmbers.Select(c => c.ToString()).ToList());
    }

    // Ember pickup-choice flow, option 1 of 2: "Convert to Wisp." Resolves the FRONT of the
    // pending queue only (see PlayerSession.PendingEmbers's own comment for why it's a queue, not
    // a single slot) -- CLAUDE.md's locked spec: "sequential if multiple dropped, never batched."
    public async Task<IReadOnlyList<string>> ConvertPendingEmberToWisp()
    {
        if (Session.PendingEmbers.Count == 0) throw new HubException("No pending Ember to resolve.");

        Session.PendingEmbers.Dequeue();
        EmberService.ConvertToWisp(Session.Ledger);
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);

        return Session.PendingEmbers.Select(c => c.ToString()).ToList();
    }

    // Ember pickup-choice flow, option 2 of 2: "Augment now." Spends the FRONT of the pending
    // queue's Ember on one skill; AugmentService itself checks the skill's color actually matches
    // that Ember's color (among everything else it validates) before committing anything.
    public async Task<IReadOnlyList<string>> AugmentPendingEmber(AugmentPendingEmberRequest request)
    {
        if (Session.PendingEmbers.Count == 0) throw new HubException("No pending Ember to resolve.");
        var emberColor = Session.PendingEmbers.Peek();

        var anima = Session.Roster.FindById(request.AnimaId) ?? throw new HubException($"Anima {request.AnimaId} not found in this account's roster.");
        var skill = GetSkillForPart(anima, request.Part);

        if (!Enum.TryParse<AugmentType>(request.AugmentType, out var augmentType))
            throw new HubException($"Unknown augment type '{request.AugmentType}'.");

        var result = AugmentService.TryApplyAugment(skill, augmentType, emberColor, Session.Ledger, Session.ActiveDelveRun?.RunLedger);
        if (!result.Success)
            throw new HubException($"Augment rejected: {result.RejectionReason}.");

        Session.PendingEmbers.Dequeue();
        await rosterRepo.SaveAnimaAsync(AccountId, anima);
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);

        return Session.PendingEmbers.Select(c => c.ToString()).ToList();
    }

    private static Skill GetSkillForPart(AnimaUnit a, string part) => part switch
    {
        nameof(Part.Head) => a.Head,
        nameof(Part.Frame) => a.Frame,
        nameof(Part.Tail) => a.Tail,
        nameof(Part.Crest) => a.Crest,
        _ => throw new HubException($"Unknown part '{part}'."),
    };

    private static DelveStatus BuildStatus(DelveRun run)
    {
        NodeRef ToRef(MapNode n) => new(n.FloorIndex, n.Column, n.Type?.ToString());

        return new DelveStatus(
            run.CurrentNode is null ? null : ToRef(run.CurrentNode),
            run.AvailableNodes.Select(ToRef).ToList(),
            run.WispEarnedSoFar);
    }
}
