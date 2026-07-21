using Anima.Core.Combat;
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

// Authenticated SignalR entry point. Sanctum/Collection, Weaving, Resource/Treasure/Shop, and the
// core Combat loop (Phase 5a) are ported, and Phase 5b (this session) closes out Victory/Defeat/
// Retreat rewards, DelveEndService, Boss-hatch + Anima Reveal, and the Artifact win-count stat --
// all directly inside SubmitAction's terminal-outcome handling, plus ConfirmBossHatch/
// RetreatFromDelve. Reforge stays deliberately deferred (0% map odds, unresolved open items) -- see
// CLAUDE.md's Server / Accounts / Auth section for the current real method-surface breakdown.
[Authorize]
public class GameHub(
    PlayerSessionRegistry sessions,
    SanctumRosterRepository rosterRepo,
    PersistentLedgerRepository ledgerRepo,
    AccountRepository accountRepo,
    AccountArtifactStatRepository artifactStatsRepo,
    PendingWeaveRepository pendingWeaveRepo,
    PendingPurchasedEmberRepository purchasedEmberRepo,
    PendingBossHatchRepository pendingBossHatchRepo) : Hub
{
    private Guid AccountId =>
        Guid.Parse(Context.User!.FindFirst(JwtTokenService.AccountIdClaimType)!.Value);

    private string Username => Context.User!.Identity!.Name ?? Context.User!.FindFirst("unique_name")!.Value;

    private PlayerSession Session =>
        sessions.Get(Context.ConnectionId) ?? throw new HubException("Session not initialized.");

    public override async Task OnConnectedAsync()
    {
        await sessions.CreateAsync(Context.ConnectionId, AccountId, Username, rosterRepo, ledgerRepo, accountRepo, pendingWeaveRepo, pendingBossHatchRepo);
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
    // granted by ClaimTreasureNode or BuyWaresArtifact (both just call the one shared
    // AccountArtifactStatRepository.RecordDiscoveryAsync); DelvesWonWith is still always 0 -- that
    // write path needs Boss-victory resolution (Phase 5).
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

    private static WeaveGenomePreview ToPreview(WeavingResult result) => ToPreview(result.Genome, result.HybridTriggered);

    // Shared by Weaving's Reveal preview (via the WeavingResult overload above) and Boss-hatch's --
    // BossHatchService.Roll only ever returns a bare AnimaGenome (Boss-hatch has no HybridTriggered
    // concept at all: hybrids are excluded entirely from Boss-hatch's body-color roll, see its own
    // comment), so hybridTriggered defaults to false for that call site rather than being omitted
    // from the shared WeaveGenomePreview shape.
    private static WeaveGenomePreview ToPreview(AnimaGenome genome, bool hybridTriggered = false) =>
        new(genome.Color.ToString(), hybridTriggered,
        [
            ToPartGenomeSummary(nameof(Part.Head), genome.Head),
            ToPartGenomeSummary(nameof(Part.Frame), genome.Frame),
            ToPartGenomeSummary(nameof(Part.Tail), genome.Tail),
            ToPartGenomeSummary(nameof(Part.Crest), genome.Crest),
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
        // Same guard shape as AttemptWeave's "one pending at a time" rule -- a confirmed-but-unnamed
        // Boss-hatch Anima is a real, already-granted-in-substance reward (see PendingBossHatch's
        // own comment); starting a fresh Delve over it would leave it permanently unreachable
        // (ConfirmBossHatch is the only thing that clears it), not just inconvenient to revisit.
        if (Session.PendingBossHatch is not null)
            throw new HubException("A Boss-hatch Anima is still pending a name -- confirm it before starting another Delve.");

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

        return new CollectResourceResult(wispGranted, await GetPendingEmberColorsAsync());
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
            return new ClaimTreasureResult(null, null, true, await GetPendingEmberColorsAsync());
        }

        var artifact = SampleArtifacts.AllFactories[Random.Shared.Next(SampleArtifacts.AllFactories.Count)]();
        var droppedEmber = ArtifactService.Grant(run.RunLedger, artifact, Session.Ledger, Random.Shared);

        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);
        await artifactStatsRepo.RecordDiscoveryAsync(AccountId, artifact.Name);

        if (droppedEmber is { } color) Session.PendingEmbers.Enqueue(color);

        return new ClaimTreasureResult(artifact.Name, artifact.Description, false, await GetPendingEmberColorsAsync());
    }

    // The account's full current "next Ember to resolve" queue, front-to-back -- PAID (DB-backed
    // PendingPurchasedEmberEntity) ones first, then FREE (in-memory Session.PendingEmbers) ones.
    // See ConvertPendingEmberToWisp/AugmentPendingEmber for why paid resolves first: a purchased
    // Ember represents real spent Wisp, so it's the higher-stakes item to clear from the queue.
    private async Task<IReadOnlyList<string>> GetPendingEmberColorsAsync()
    {
        var purchased = await purchasedEmberRepo.LoadAsync(AccountId);
        return purchased.Select(c => c.ToString()).Concat(Session.PendingEmbers.Select(c => c.ToString())).ToList();
    }

    // Ember pickup-choice flow, option 1 of 2: "Convert to Wisp." Resolves the FRONT of the
    // combined queue -- a paid (Wares-bought) Ember first if any is pending, per
    // PendingPurchasedEmberEntity's own comment; otherwise the FREE queue's front (see
    // PlayerSession.PendingEmbers's own comment for why it's a queue, not a single slot) --
    // CLAUDE.md's locked spec: "sequential if multiple dropped, never batched."
    public async Task<IReadOnlyList<string>> ConvertPendingEmberToWisp()
    {
        var purchased = await purchasedEmberRepo.LoadAsync(AccountId);
        if (purchased.Count > 0)
        {
            await purchasedEmberRepo.RemoveOldestAsync(AccountId);
        }
        else if (Session.PendingEmbers.Count > 0)
        {
            Session.PendingEmbers.Dequeue();
        }
        else
        {
            throw new HubException("No pending Ember to resolve.");
        }

        EmberService.ConvertToWisp(Session.Ledger);
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);

        return await GetPendingEmberColorsAsync();
    }

    // Ember pickup-choice flow, option 2 of 2: "Augment now." Spends the FRONT of the combined
    // queue (paid first, same order as ConvertPendingEmberToWisp) on one skill; AugmentService
    // itself checks the skill's color actually matches that Ember's color (among everything else
    // it validates) before committing anything.
    public async Task<IReadOnlyList<string>> AugmentPendingEmber(AugmentPendingEmberRequest request)
    {
        var purchased = await purchasedEmberRepo.LoadAsync(AccountId);
        AnimaColor emberColor;
        bool resolvingPurchased;
        if (purchased.Count > 0)
        {
            emberColor = purchased[0];
            resolvingPurchased = true;
        }
        else if (Session.PendingEmbers.Count > 0)
        {
            emberColor = Session.PendingEmbers.Peek();
            resolvingPurchased = false;
        }
        else
        {
            throw new HubException("No pending Ember to resolve.");
        }

        var anima = Session.Roster.FindById(request.AnimaId) ?? throw new HubException($"Anima {request.AnimaId} not found in this account's roster.");
        var skill = GetSkillForPart(anima, request.Part);

        if (!Enum.TryParse<AugmentType>(request.AugmentType, out var augmentType))
            throw new HubException($"Unknown augment type '{request.AugmentType}'.");

        var result = AugmentService.TryApplyAugment(skill, augmentType, emberColor, Session.Ledger, Session.ActiveDelveRun?.RunLedger);
        if (!result.Success)
            throw new HubException($"Augment rejected: {result.RejectionReason}.");

        if (resolvingPurchased) await purchasedEmberRepo.RemoveOldestAsync(AccountId);
        else Session.PendingEmbers.Dequeue();

        await rosterRepo.SaveAnimaAsync(AccountId, anima);
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);

        return await GetPendingEmberColorsAsync();
    }

    // Shared entry point for every Shop action (GetShopStock/RestAtShop/BuyWaresEmber/
    // BuyWaresArtifact) -- ensures ArtifactService.OnNodeVisited (Withering Fang/Sapling Charm),
    // the Wares roll, and the map-visited marker (MarkCurrentNodeCleared) all happen EXACTLY ONCE
    // per Shop visit, the first time the player does ANYTHING at this node, regardless of which
    // action that happens to be. Idempotent on repeat calls for the same node -- returns the
    // already-rolled ShopVisitState instead of rerolling (see ShopVisitState's own comment: "each
    // Shop node rolls its own independent stock on entry", not on every read).
    private async Task<ShopVisitState> EnsureShopVisited(DelveRun run)
    {
        var node = run.CurrentNode ?? throw new HubException("Not currently standing on a node.");
        if (node.Type != MapNodeType.Shop) throw new HubException($"Current node is {node.Type?.ToString() ?? "untyped"}, not Shop.");

        if (Session.CurrentShopStock is { } existing && existing.Node == node) return existing;

        ArtifactService.OnNodeVisited(run.RunLedger, run.Team);
        foreach (var member in run.Team) await rosterRepo.SaveAnimaAsync(AccountId, member);

        var stock = ShopService.Roll(run.RunLedger, Random.Shared);
        var state = new ShopVisitState
        {
            Node = node,
            EmberSlots = stock.EmberOffers.Cast<AnimaColor?>().ToList(),
            ArtifactOffer = stock.ArtifactOffer,
        };
        run.MarkCurrentNodeCleared();
        Session.CurrentShopStock = state;

        return state;
    }

    private static ShopStockSnapshot BuildShopSnapshot(ShopVisitState state, RunLedger runLedger)
    {
        var emberSlots = state.EmberSlots.Select((c, i) => new ShopEmberSlot(i, c?.ToString())).ToList();

        var emberPrice = ArtifactService.ApplyEmberCoreDiscount(EmberService.ShopPrice, runLedger);
        var artifactPrice = ArtifactService.ApplyEmberCoreDiscount(ShopService.ArtifactWaresPrice, runLedger);
        var restPrice = ArtifactService.ApplyEmberCoreDiscount(ShopService.RestWispCost, runLedger);

        return new ShopStockSnapshot(emberSlots, state.ArtifactOffer?.Name, state.ArtifactOffer?.Description, emberPrice, artifactPrice, restPrice);
    }

    // Reads (rolling fresh stock only on the first call for this node, see EnsureShopVisited) the
    // current Wares offer.
    public async Task<ShopStockSnapshot> GetShopStock()
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        var state = await EnsureShopVisited(run);
        return BuildShopSnapshot(state, run.RunLedger);
    }

    // Rest: heal the whole team 40% max HP for Wisp (see ShopService.TryRest's own comment for the
    // whole-team/price judgment calls). Repeatable as many times as affordable in one visit --
    // there's no stated once-per-visit cap.
    public async Task<RestAtShopResult> RestAtShop()
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        await EnsureShopVisited(run);

        var wispBefore = Session.Ledger.GetBalance(ResourceType.Wisp);
        if (!ShopService.TryRest(run.Team, Session.Ledger, run.RunLedger))
            throw new HubException("Insufficient Wisp.");
        var wispSpent = wispBefore - Session.Ledger.GetBalance(ResourceType.Wisp);

        foreach (var member in run.Team) await rosterRepo.SaveAnimaAsync(AccountId, member);
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);

        return new RestAtShopResult(wispSpent);
    }

    // Buying an Ember from Wares is the SAME shape as the pending-Weave bug (Phase 4 audit): Wisp
    // is spent immediately (EmberService.TryBuyEmber), before the Augment/Convert choice is ever
    // made. Unlike a free node-dropped Ember, this one goes into the DB-backed
    // PendingPurchasedEmberEntity queue, not the in-memory Session.PendingEmbers one -- see that
    // entity's own comment.
    public async Task<IReadOnlyList<string>> BuyWaresEmber(BuyWaresEmberRequest request)
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        var state = await EnsureShopVisited(run);

        if (request.SlotIndex < 0 || request.SlotIndex >= state.EmberSlots.Count)
            throw new HubException("Invalid Ember slot index.");
        var color = state.EmberSlots[request.SlotIndex] ?? throw new HubException("That Ember slot has already been bought.");

        if (!EmberService.TryBuyEmber(Session.Ledger, run.RunLedger))
            throw new HubException("Insufficient Wisp.");

        state.EmberSlots[request.SlotIndex] = null;
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);
        await purchasedEmberRepo.AddAsync(AccountId, color);

        return await GetPendingEmberColorsAsync();
    }

    // Buying the Wares Artifact: same AccountArtifactStatRepository.RecordDiscoveryAsync call
    // ClaimTreasureNode already uses (Phase 3), not a duplicated call site -- both just call
    // through to that one shared repository method.
    public async Task<BuyWaresArtifactResult> BuyWaresArtifact()
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        var state = await EnsureShopVisited(run);

        var artifact = state.ArtifactOffer ?? throw new HubException("No Artifact is currently offered.");

        var (success, droppedEmber) = ShopService.TryBuyArtifact(artifact, run.RunLedger, Session.Ledger, Random.Shared);
        if (!success) throw new HubException("Insufficient Wisp.");

        state.ArtifactOffer = null;
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);
        await artifactStatsRepo.RecordDiscoveryAsync(AccountId, artifact.Name);

        // The Artifact's own OnPickup bonus (e.g. Marked Coin) -- an incidental bonus from the
        // purchase already paid for, not something separately bought, so it's FREE from this
        // Ember's own perspective -- same handling ClaimTreasureNode already gives an
        // OnPickup-dropped Ember (the in-memory queue, not the paid one).
        if (droppedEmber is { } color) Session.PendingEmbers.Enqueue(color);

        return new BuyWaresArtifactResult(artifact.Name, artifact.Description, await GetPendingEmberColorsAsync());
    }

    // ---- Combat (Phase 5a: core loop only) ----
    //
    // Request-response, matching every other GameHub method so far -- no SignalR server-push in
    // this pass (real push-based live turn streaming is a decision for whenever the actual Godot
    // client build reveals whether it's needed, not built speculatively here). SubmitAction
    // resolves one player action, then auto-resolves any consecutive enemy turns/Round transitions
    // server-side immediately via CombatEngine.AdvanceUntilPlayerActionNeeded, returning the full
    // resulting state + event log in the one response.

    // Which enemy encounter a node type fights -- mirrors the console harness's own Delve-
    // simulation convention exactly (Combat = Grovehide+Quillfang with Quillfang at position 2,
    // Boss = Warden of the Hollow) with one deliberate simplification: Elite alternates
    // Sentinel/LeechMother in the harness (a counter, for even test coverage); here it's a random
    // 50/50 pick instead, since a hub-driven real Delve has no such "even coverage" requirement.
    private static List<Enemy> BuildEncounter(MapNodeType type) => type switch
    {
        MapNodeType.Boss => [SampleEnemies.CreateWardenOfTheHollow()],
        MapNodeType.Elite => Random.Shared.Next(2) == 0
            ? [SampleEnemies.CreateSentinel()]
            : [SampleEnemies.CreateLeechMother()],
        MapNodeType.Combat => BuildBasicCombatEncounter(),
        _ => throw new HubException($"{type} is not a Combat-capable node type."),
    };

    private static List<Enemy> BuildBasicCombatEncounter()
    {
        var quillfang = SampleEnemies.CreateQuillfang();
        quillfang.Position = 2;
        return [SampleEnemies.CreateGrovehide(), quillfang];
    }

    // Starts (or, on a repeat call for the same still-uncleared node, idempotently resumes) combat
    // for the current Combat/Elite/Boss node. CombatState.PlayerTeam is built from run.Team
    // DIRECTLY, never a rebuilt/copied list -- the exact reference DelveRun.Team already holds (see
    // CLAUDE.md's own HP-attrition gotcha) -- so HP mutations during the fight persist for free
    // through the shared Anima instances, same as every other node type already relies on.
    // ArtifactService.OnNodeVisited runs before the fight starts (Withering Fang's pre-fight snipe,
    // Sapling Charm's on-entry heal), matching the harness's own combat-node sequence. Rolls Round
    // 1's turn order and auto-resolves anything before the first player turn (e.g. a faster enemy
    // going first) via AdvanceUntilPlayerActionNeeded -- see that method's own comment.
    public async Task<CombatStatus> StartCombat()
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        var node = run.CurrentNode ?? throw new HubException("Not currently standing on a node.");
        if (node.Type is not (MapNodeType.Combat or MapNodeType.Elite or MapNodeType.Boss))
            throw new HubException($"Current node is {node.Type?.ToString() ?? "untyped"}, not a Combat/Elite/Boss node.");
        if (run.ClearedNodes.Contains(node)) throw new HubException("This node has already been cleared.");

        if (Session.ActiveCombat is { } existing) return BuildCombatStatus(existing);

        var state = new CombatState { PlayerTeam = run.Team, EnemyTeam = BuildEncounter(node.Type.Value) };

        ArtifactService.OnNodeVisited(run.RunLedger, run.Team, state);
        foreach (var member in run.Team) await rosterRepo.SaveAnimaAsync(AccountId, member);

        var engine = new CombatEngine(state, run.RunLedger.Artifacts);
        var log = new List<string>();
        engine.OnLog = log.Add;
        engine.StartCombat();
        engine.AdvanceUntilPlayerActionNeeded();

        Session.ActiveCombat = state;
        return BuildCombatStatus(state, log);
    }

    // Resume/reconnect support, same spirit as GetPendingWeave -- but see PlayerSession.
    // ActiveCombat's own comment for why this is in-memory-only rather than DB-backed like
    // PendingWeave: nothing valuable is uniquely at risk yet in Phase 5a.
    public Task<CombatStatus> GetCombatState()
    {
        var state = Session.ActiveCombat ?? throw new HubException("No active combat for this connection.");
        return Task.FromResult(BuildCombatStatus(state));
    }

    // The legal target set for a hand card, from the CURRENT actor's perspective -- what the
    // client's highlight-then-confirm flow (CLAUDE.md's Combat Screen Design) reads before
    // presenting a "choose a target" prompt. No ownership check on the card here (read-only,
    // nothing committed) -- SubmitAction is the actual enforcement point for "does this card
    // belong to the acting Anima."
    public Task<IReadOnlyList<CombatantRef>> GetLegalTargets(int handIndex)
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        var state = Session.ActiveCombat ?? throw new HubException("No active combat for this connection.");
        if (state.CurrentActor is not AnimaUnit anima) throw new HubException("It is not a player Anima's turn.");
        if (handIndex < 0 || handIndex >= state.Hand.Count) throw new HubException("Invalid hand index.");

        var engine = new CombatEngine(state, run.RunLedger.Artifacts);
        var targets = engine.GetLegalTargets(anima, state.Hand[handIndex]);

        return Task.FromResult<IReadOnlyList<CombatantRef>>(targets.Select(t => ToCombatantRef(state, t)).ToList());
    }

    // Plays a card + target (or Passes) for whoever CombatState.CurrentActor currently is --
    // rejects if request.AnimaId doesn't match (not that Anima's turn) or the target isn't in
    // GetLegalTargets' set for that card (illegal target). Auto-resolves subsequent enemy turns
    // and Round transitions immediately afterward via AdvanceUntilPlayerActionNeeded, so the
    // response already reflects everything up to the next real player decision (or a terminal
    // Victory/Defeat). On a terminal outcome, rewards are resolved and granted INSIDE this same
    // call (Phase 5b) -- same "no separate claim step, no double-claim window" pattern every other
    // node-resolution method (CollectResourceNode/ClaimTreasureNode) already uses:
    // - Victory (Combat/Elite): Wisp/Ember/Vessel-Shard granted via GrantNonBossVictoryRewardAsync,
    //   surfaced on CombatStatus.VictoryReward.
    // - Victory (Boss): Wisp/Echo-Shard granted, the Artifact win-count stat bumped for every
    //   currently-held Artifact, and the guaranteed hatched Anima rolled (not yet materialized --
    //   see GrantBossVictoryRewardAsync/ConfirmBossHatch) via GrantBossVictoryRewardAsync, surfaced
    //   on VictoryReward + BossHatchPreview. The whole Delve ends here (Session.ActiveDelveRun is
    //   cleared) -- Boss is the map's terminal node, nothing left to visit past it.
    // - Defeat: DelveEndService.ResolveDefeat's 50%-Wisp-kept math, surfaced on DefeatSummary. The
    //   Delve also ends here.
    public async Task<CombatStatus> SubmitAction(SubmitActionRequest request)
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        var state = Session.ActiveCombat ?? throw new HubException("No active combat for this connection.");

        if (state.CurrentActor is not AnimaUnit anima || anima.Id != request.AnimaId)
            throw new HubException("It is not that Anima's turn.");

        var engine = new CombatEngine(state, run.RunLedger.Artifacts);
        var log = new List<string>();
        engine.OnLog = log.Add;

        try
        {
            if (request.HandIndex is not { } handIndex)
            {
                engine.ResolvePlayerPass(anima);
            }
            else
            {
                if (handIndex < 0 || handIndex >= state.Hand.Count) throw new HubException("Invalid hand index.");
                var skill = state.Hand[handIndex];

                var legalTargets = engine.GetLegalTargets(anima, skill);
                var explicitTarget = request.Target is { } t ? ResolveCombatantRef(state, t) : null;
                if (legalTargets.Count > 0 && (explicitTarget == null || !legalTargets.Contains(explicitTarget)))
                    throw new HubException("Illegal target for that skill.");

                engine.ResolvePlayerAction(anima, skill, explicitTarget);
            }
        }
        catch (InvalidOperationException ex)
        {
            // CombatEngine's own validation (turn ownership, skill ownership, position-usability,
            // Energy affordability) -- see ResolvePlayerAction's own comment for why the engine
            // enforces these itself here but not on RunRound's callback-driven path.
            throw new HubException(ex.Message);
        }

        var outcome = engine.AdvanceUntilPlayerActionNeeded();

        foreach (var member in run.Team) await rosterRepo.SaveAnimaAsync(AccountId, member);

        CombatVictoryReward? victoryReward = null;
        WeaveGenomePreview? bossHatchPreview = null;
        DelveEndSummary? defeatSummary = null;

        switch (outcome)
        {
            case CombatOutcome.Victory:
            {
                // StartCombat already required the current node to be Combat/Elite/Boss, so Type
                // is guaranteed set here.
                var nodeType = run.CurrentNode!.Type!.Value;

                // Marked cleared BEFORE granting the reward (reordered in Phase 5c) so a Boss
                // Victory's DelveCompleteSnapshot -- captured inside GrantBossVictoryRewardAsync --
                // correctly counts the Boss node itself as cleared. Harmless for Combat/Elite too;
                // clearing is just bookkeeping, not gated on anything reward-related.
                run.MarkCurrentNodeCleared();
                Session.ActiveCombat = null;

                if (nodeType == MapNodeType.Boss)
                {
                    var (reward, preview) = await GrantBossVictoryRewardAsync(run);
                    victoryReward = reward;
                    bossHatchPreview = preview;
                    // Boss is the map's terminal node -- nothing left to visit past it, so the
                    // Delve itself ends here too (unlike a Combat/Elite Victory, which just clears
                    // the node and lets the player keep moving through the same DelveRun).
                    Session.ActiveDelveRun = null;
                }
                else
                {
                    victoryReward = await GrantNonBossVictoryRewardAsync(run, nodeType);
                }
                break;
            }
            case CombatOutcome.Defeat:
            {
                // Captured BEFORE MarkCurrentNodeCleared below, so the summary reports nodes
                // actually cleared before the fatal fight, not counting the one that ended the run.
                var nodesClearedBeforeThis = run.ClearedNodes.Count;
                var floorIndexReached = run.CurrentNode?.FloorIndex ?? 0;

                var result = DelveEndService.ResolveDefeat(Session.Ledger, run.RunLedger);
                await ledgerRepo.SaveAsync(AccountId, Session.Ledger);

                defeatSummary = new DelveEndSummary(result.WispEarnedThisRun, result.WispKept, result.WispForfeited, floorIndexReached, nodesClearedBeforeThis);

                run.MarkCurrentNodeCleared();
                Session.ActiveCombat = null;
                Session.ActiveDelveRun = null;
                break;
            }
        }

        return BuildCombatStatus(state, log, victoryReward, bossHatchPreview, defeatSummary);
    }

    // Combat/Elite Victory's reward grant -- Wisp (Wisp Charm-adjusted, applied inside
    // RewardService itself) + Ember drop(s) queued for the same pickup-choice flow every other
    // Ember drop uses (Resource/Treasure/Shop), + Elite's independent 25% Vessel Shard chance.
    // Diffs the ledger before/after (same pattern CollectResourceNode already uses for its own
    // WispGranted) since RewardService grants straight into the ledger rather than returning
    // amounts granted.
    private async Task<CombatVictoryReward> GrantNonBossVictoryRewardAsync(DelveRun run, MapNodeType nodeType)
    {
        var wispBefore = Session.Ledger.GetBalance(ResourceType.Wisp);
        var vesselShardBefore = Session.Ledger.GetBalance(ResourceType.VesselShard);

        var emberDrops = nodeType == MapNodeType.Elite
            ? RewardService.GrantEliteWin(Session.Ledger, Random.Shared, run.RunLedger)
            : RewardService.GrantCombatWin(Session.Ledger, Random.Shared, run.RunLedger);

        var wispGranted = Session.Ledger.GetBalance(ResourceType.Wisp) - wispBefore;
        var vesselShardGranted = Session.Ledger.GetBalance(ResourceType.VesselShard) > vesselShardBefore;

        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);
        foreach (var color in emberDrops) Session.PendingEmbers.Enqueue(color);

        return new CombatVictoryReward(wispGranted, vesselShardGranted, false, await GetPendingEmberColorsAsync());
    }

    // Boss Victory's reward grant: Wisp (300, Wisp Charm-adjusted) + a guaranteed Echo Shard via
    // RewardService.GrantBossWin -- no Ember (confirmed by reading GrantBossWin itself: Boss's
    // reward tier doesn't include one, unlike Combat/Elite/Resource) -- plus the guaranteed hatched
    // Anima via BossHatchService.Roll. The genome is stashed as a DB-backed PendingBossHatch (NOT
    // materialized into SanctumRoster yet) until ConfirmBossHatch supplies the mandatory name --
    // same "granted/won, not yet resolved" treatment PendingWeave already gets for an AttemptWeave
    // roll, see PendingBossHatch's own comment for why. Also bumps the Artifact win-count stat
    // (AccountArtifactStatEntity.DelvesWonWithCount) for every Artifact currently held -- the write
    // path that entity's own comment flagged as blocked since Phase 1, pending Boss-victory
    // resolution.
    private async Task<(CombatVictoryReward Reward, WeaveGenomePreview Preview)> GrantBossVictoryRewardAsync(DelveRun run)
    {
        var wispBefore = Session.Ledger.GetBalance(ResourceType.Wisp);
        var echoShardBefore = Session.Ledger.GetBalance(ResourceType.EchoShard);

        RewardService.GrantBossWin(Session.Ledger, Random.Shared, run.RunLedger);

        var wispGranted = Session.Ledger.GetBalance(ResourceType.Wisp) - wispBefore;
        var echoShardGranted = Session.Ledger.GetBalance(ResourceType.EchoShard) > echoShardBefore;

        var genome = BossHatchService.Roll(Random.Shared);

        // Delve Complete summary (Phase 5c) -- captured HERE, the last point run (== Session.
        // ActiveDelveRun) is reachable before the caller nulls it. FloorIndexReached/NodesCleared
        // read run.CurrentNode/run.ClearedNodes AFTER the caller's MarkCurrentNodeCleared() already
        // ran (see SubmitAction's reordering comment), so the Boss node itself counts as cleared.
        // WispEarnedSoFar already reflects the Wisp just granted above (it reads the live
        // in-memory ledger, not a DB round-trip). See DelveCompleteSnapshot's own comment for why
        // this is deliberately NOT persisted to PendingBossHatchEntity/DB.
        var completeSummary = new DelveCompleteSnapshot(
            run.CurrentNode!.FloorIndex,
            run.ClearedNodes.Count,
            run.Team.Select(a => a.Name).ToList(),
            run.WispEarnedSoFar);

        Session.PendingBossHatch = new PendingBossHatch { Genome = genome, CompleteSummary = completeSummary };
        await pendingBossHatchRepo.SaveAsync(AccountId, Session.PendingBossHatch);
        await ledgerRepo.SaveAsync(AccountId, Session.Ledger);

        foreach (var artifact in run.RunLedger.Artifacts)
        {
            await artifactStatsRepo.RecordWinAsync(AccountId, artifact.Name);
        }

        var reward = new CombatVictoryReward(wispGranted, false, echoShardGranted, await GetPendingEmberColorsAsync());
        return (reward, ToPreview(genome));
    }

    // The mandatory naming step for a Boss Victory's guaranteed hatched Anima -- mirrors
    // ConfirmWeave exactly (see its own comment): materializes via AnimaMaterializationService only
    // now, adds to SanctumRoster, persists, and clears the pending row. No "Twin" concept here --
    // Boss-hatch always produces exactly one Anima, unlike a Weave's Echo Twin possibility.
    //
    // DelveComplete (Phase 5c) rides along on this SAME response rather than needing its own
    // confirmation step or a separate hub method -- it's a read of already-resolved end-of-run
    // state (floors/Anima/Wisp), not another mandatory action, and the locked design's own
    // sequencing wants it to appear only once naming is done ("Anima Reveal confirms first, THEN
    // Delve Complete appends"). Null only on the rare reconnect-before-confirming case (see
    // DelveCompleteSnapshot's own comment).
    public async Task<BossHatchConfirmResult> ConfirmBossHatch(ConfirmBossHatchRequest request)
    {
        var pending = Session.PendingBossHatch ?? throw new HubException("No pending Boss-hatch Anima to confirm.");
        if (string.IsNullOrWhiteSpace(request.Name)) throw new HubException("A name is required.");

        var anima = AnimaMaterializationService.Create(pending.Genome, request.Name, Session.Roster);
        await rosterRepo.SaveAnimaAsync(AccountId, anima);

        Session.PendingBossHatch = null;
        await pendingBossHatchRepo.DeleteAsync(AccountId);

        var delveComplete = pending.CompleteSummary is { } snapshot
            ? new DelveCompleteSummary(snapshot.FloorIndexReached, snapshot.NodesCleared, snapshot.AnimaUsedNames, snapshot.TotalWispEarnedThisRun)
            : null;

        return new BossHatchConfirmResult(ToSummary(anima), delveComplete);
    }

    // Reconnect support, same spirit as GetPendingWeave -- lets a client that just reconnected
    // re-render the Anima Reveal screen for a Boss-hatch that already resolved (and was already
    // granted) before the disconnect. Null when nothing is pending.
    public Task<WeaveGenomePreview?> GetPendingBossHatch()
    {
        var pending = Session.PendingBossHatch;
        return Task.FromResult(pending is null ? null : ToPreview(pending.Genome));
    }

    // Voluntary mid-run exit, per the Match Result & Retreat System's locked design: 0% Wisp
    // penalty (100% of this-run Wisp kept, unlike a Defeat's 50%) -- Artifacts are still lost
    // (run-only state, same as Defeat; nothing to do here since RunLedger is simply discarded along
    // with the rest of DelveRun). Only valid standing on the map between nodes: rejects if
    // mid-combat (Session.ActiveCombat set) -- there's deliberately no other "mid-node" state to
    // guard against today, since every other node type (Resource/Treasure/Shop) resolves atomically
    // in one call and leaves nothing lingering to be "inside" of.
    public Task<DelveEndSummary> RetreatFromDelve()
    {
        var run = Session.ActiveDelveRun ?? throw new HubException("No active Delve for this connection.");
        if (Session.ActiveCombat is not null)
            throw new HubException("Cannot Retreat while a Combat is in progress.");

        var nodesCleared = run.ClearedNodes.Count;
        var floorIndexReached = run.CurrentNode?.FloorIndex ?? 0;
        var result = DelveEndService.ResolveRetreat(Session.Ledger, run.RunLedger);

        Session.ActiveDelveRun = null;

        return Task.FromResult(new DelveEndSummary(result.WispEarnedThisRun, result.WispKept, result.WispForfeited, floorIndexReached, nodesCleared));
    }

    private static ICombatant ResolveCombatantRef(CombatState state, CombatantRef reference)
    {
        var list = reference.Side switch
        {
            "Player" => state.PlayerTeam.Cast<ICombatant>().ToList(),
            "Enemy" => state.EnemyTeam.Cast<ICombatant>().ToList(),
            _ => throw new HubException($"Unknown target side '{reference.Side}'."),
        };
        if (reference.Index < 0 || reference.Index >= list.Count) throw new HubException("Target index out of range.");
        return list[reference.Index];
    }

    private static CombatantRef ToCombatantRef(CombatState state, ICombatant combatant)
    {
        var playerIndex = state.PlayerTeam.FindIndex(a => ReferenceEquals(a, combatant));
        if (playerIndex >= 0) return new CombatantRef("Player", playerIndex);

        var enemyIndex = state.EnemyTeam.FindIndex(e => ReferenceEquals(e, combatant));
        return new CombatantRef("Enemy", enemyIndex);
    }

    private static CombatStatus BuildCombatStatus(
        CombatState state,
        IReadOnlyList<string>? log = null,
        CombatVictoryReward? victoryReward = null,
        WeaveGenomePreview? bossHatchPreview = null,
        DelveEndSummary? defeatSummary = null)
    {
        CombatantSummary ToSummary(string side, int index, ICombatant c) => new(
            side, index, c.DisplayName, c.CurrentHp, c.MaxHp, c.Position, c.CurrentHp > 0,
            c.ActiveStatuses.Select(s => s.Keyword).ToList());

        var playerSummaries = state.PlayerTeam.Select((a, i) => ToSummary("Player", i, a)).ToList();
        var enemySummaries = state.EnemyTeam.Select((e, i) => ToSummary("Enemy", i, e)).ToList();

        var hand = state.Hand
            .Select((s, i) =>
            {
                var owner = state.PlayerTeam.First(a => a.DeckSkills.Contains(s));
                return new HandCardSummary(i, owner.Id, s.Name, s.Category.ToString(), s.Color?.ToString() ?? "", s.EnergyCost, s.Target.ToString());
            })
            .ToList();

        var turnOrder = state.TurnOrder.Select(c => ToTurnEntry(state, c)).ToList();

        var outcome = CombatEngine.GetOutcome(state);
        var currentActor = outcome == CombatOutcome.InProgress ? state.CurrentActor as AnimaUnit : null;

        return new CombatStatus(
            state.RoundNumber, state.SharedEnergy, playerSummaries, enemySummaries, hand,
            state.DrawPile.Count, state.DiscardPile.Count, turnOrder, state.TurnIndex,
            currentActor?.Id, outcome.ToString(), log ?? Array.Empty<string>(),
            victoryReward, bossHatchPreview, defeatSummary);
    }

    private static CombatTurnEntry ToTurnEntry(CombatState state, ICombatant combatant)
    {
        var reference = ToCombatantRef(state, combatant);
        return new CombatTurnEntry(reference.Side, reference.Index, combatant.DisplayName);
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
