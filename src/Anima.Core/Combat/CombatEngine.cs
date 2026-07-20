namespace Anima.Core.Combat;

using Anima.Core.Economy;
using Anima.Core.Enums;
using Anima.Core.Models;

public class CombatEngine
{
    private readonly CombatState _state;
    private readonly List<Artifact> _ownedArtifacts;
    private readonly Random _random = new();

    // A hard backstop inside DrawCards itself, distinct from HandMax (the 5/6 target size
    // TopUpHand actually draws to) -- normal play never gets anywhere near this under the current
    // top-up model, it just guards DrawCards (a general-purpose primitive) against ever
    // overfilling the hand if something else calls it directly in the future.
    private const int HandCap = 10;
    private const int CopiesPerSkill = 3;
    private const double CrestConditionalMultiplier = 1.25;
    private const double CourageDamageReduction = 0.8;
    private const double AmbushMultiplier = 2.0;
    private const double FrenzyMultiplier = 1.2;
    private const double BloodthirstLifestealPercent = 0.25;
    private const double SteadyAimMultiplier = 1.15;
    private const double ExposedMultiplier = 1.25;
    private const double InspireSharePercent = 0.3;
    private const int BarrierStoneShieldAmount = 5; // not specified anywhere -- a small, flat, first-pass value
    private const int FocusingLensInterval = 4;
    private const double FocusingLensMultiplier = 2.0;

    // This Round's Initiative order, captured once per Round so Ambush can check whether the
    // acting combatant is literally the last entry in it (see GetOffensiveCrestMultiplier).
    private List<ICombatant> _currentInitiativeOrder = new();

    // Silent Chime: set by TryActivateSilentChime, consumed by ActionPhase the moment this
    // Anima's normal turn resolves this Round -- see both for the full mechanic.
    private Anima? _pendingExtraActionAnima;

    // Narration hook — kept out of Core so callers (console, UI, tests) decide how/whether to surface it.
    public Action<string>? OnLog { get; set; }

    // Placeholder for real player input / AI; the harness supplies a hardcoded priority for now.
    public Func<Anima, CombatState, Skill?>? ChoosePlayerSkill { get; set; }

    // Sifting Stone: before each Round's top-up draw resolves, lets the player discard any number
    // of cards from their current hand (the top-up draw then replaces both played AND these
    // voluntarily-discarded cards). Same "placeholder decision hook" pattern as ChoosePlayerSkill
    // -- callers that don't set this (including every existing call site) simply discard nothing,
    // a safe no-op default. Only ever consulted if Sifting Stone is owned (see RoundStartPhase).
    public Func<CombatState, IReadOnlyList<Skill>>? ChooseSiftingStoneDiscards { get; set; }

    // ownedArtifacts: the Artifacts currently owned for this Delve (typically RunLedger.Artifacts)
    // -- optional so every existing call site with no Artifacts in play still compiles unchanged.
    public CombatEngine(CombatState state, IReadOnlyList<Artifact>? ownedArtifacts = null)
    {
        _state = state;
        _ownedArtifacts = ownedArtifacts?.ToList() ?? [];
    }

    private bool HasArtifact(string name) => _ownedArtifacts.Any(a => a.Name == name);

    // The hand's target size -- persistent hand, top-up draw model (replaces an earlier flat
    // "draw N/Round" design): the opening hand at combat start and every Round Start's top-up
    // draw both just fill the hand back up to this same number, via TopUpHand below. Weaver's
    // Thread: +1 (5 -> 6).
    private int HandMax => HasArtifact("Weaver's Thread") ? 6 : 5;

    // Draws exactly enough cards to bring Hand back up to HandMax -- a no-op if already at or
    // above it (e.g. Weaver's Thread was lost mid-combat, which can't currently happen, but this
    // stays correct regardless). Used identically at combat start (Hand starts empty, so this
    // naturally produces the opening hand) and at every Round Start (the actual top-up).
    private void TopUpHand()
    {
        var drawCount = HandMax - _state.Hand.Count;
        if (drawCount > 0) DrawCards(drawCount);
    }

    // Builds the shared 27-card team deck (Head/Frame/Tail x3 copies each, Crests excluded),
    // shuffles it, and draws the opening hand (HandMax -- 5, or 6 with Weaver's Thread). Call once
    // before the first RunRound().
    public void StartCombat()
    {
        BuildDeck();
        Shuffle(_state.DrawPile);

        // Fires every owned Artifact's OnCombatStart hook (e.g. Vanguard's Bell's +1 starting
        // Energy) -- the hook was already declared on the Artifact model but never actually
        // invoked anywhere until now.
        foreach (var artifact in _ownedArtifacts)
        {
            artifact.OnCombatStart?.Invoke(_state);
        }

        TopUpHand();
    }

    private void BuildDeck()
    {
        foreach (var anima in _state.PlayerTeam)
        {
            foreach (var skill in anima.DeckSkills)
            {
                for (var i = 0; i < CopiesPerSkill; i++)
                {
                    _state.DrawPile.Add(skill);
                }
            }
        }
    }

    // Fisher-Yates — the deck shuffle is an explicit, documented exception to the
    // "no randomness in combat" rule, so a plain Random is fine here.
    private void Shuffle(List<Skill> pile)
    {
        for (var i = pile.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (pile[i], pile[j]) = (pile[j], pile[i]);
        }
    }

    public void RunRound()
    {
        Log($"=== Round {_state.RoundNumber} ===");
        RoundStartPhase();
        var initiativeOrder = InitiativePhase();
        _currentInitiativeOrder = initiativeOrder;
        LogInitiativeOrder(initiativeOrder);
        ActionPhase(initiativeOrder);
        RoundEndPhase();
    }

    // Silent Chime: single-use per Delve, so activating it consumes the Artifact from runLedger
    // immediately (mirrors Withering Fang's "consumed on use" pattern in ArtifactService) --
    // returns false and does nothing if it isn't owned/already used. The extra action itself is
    // granted later, by ActionPhase, the moment `target`'s normal turn resolves in whichever
    // Round is running when this was called -- there's no mid-Round player-decision loop yet, so
    // the caller is expected to activate before calling RunRound() for the Round they want it in.
    public bool TryActivateSilentChime(Anima target, RunLedger runLedger)
    {
        var silentChime = runLedger.Artifacts.FirstOrDefault(a => a.Name == "Silent Chime");
        if (silentChime == null) return false;

        runLedger.Artifacts.Remove(silentChime);
        _pendingExtraActionAnima = target;
        return true;
    }

    private void RoundStartPhase()
    {
        // Energy refill (+3, capped)
        _state.SharedEnergy = Math.Min(_state.SharedEnergy + 3, 9);
        Log($"Energy: {_state.SharedEnergy}");

        // Sifting Stone: before the top-up draw resolves, the player may discard any number of
        // cards from their current hand -- the top-up draw right after then replaces both played
        // AND these voluntarily-discarded cards, same as any other top-up. ChooseSiftingStoneDiscards
        // returning null/empty (the default -- see its own comment) means discard nothing.
        if (HasArtifact("Sifting Stone"))
        {
            var toDiscard = ChooseSiftingStoneDiscards?.Invoke(_state) ?? [];
            foreach (var skill in toDiscard.Where(s => _state.Hand.Contains(s)).ToList())
            {
                _state.Hand.Remove(skill);
                _state.DiscardPile.Add(skill);
                Log($"  Sifting Stone: discarded {skill.Name} from hand.");
            }
        }

        // Persistent hand, top-up draw (opening hand handled identically at combat start).
        TopUpHand();

        // Tick down all active Durations across all combatants
        foreach (var combatant in AllCombatants())
        {
            TickStatusDurations(combatant);
        }

        // Barrier Stone: +5 flat Shield to the WHOLE owning team (not one slot) at every Round
        // Start -- an AoE-shaped ward, same reward shape as Ward's own AllAllies Shield skill,
        // rather than picking one arbitrary position to favor.
        if (HasArtifact("Barrier Stone"))
        {
            var playerTeam = _state.PlayerTeam.Cast<ICombatant>().ToList();
            foreach (var ally in playerTeam.Where(a => a.CurrentHp > 0))
            {
                var granted = GrantShield(ally, BarrierStoneShieldAmount);
                TriggerInspire(ally, granted, playerTeam);
            }
        }

        // Elite/Boss Enrage — universal safety net against stalemates.
        foreach (var enemy in _state.EnemyTeam.Where(e => e.CurrentHp > 0))
        {
            if (enemy.EnrageRound.HasValue && !enemy.IsEnraged && _state.RoundNumber >= enemy.EnrageRound.Value)
            {
                enemy.IsEnraged = true;
                enemy.EnrageTriggeredRound = _state.RoundNumber;
                Log($"{enemy.Name} becomes ENRAGED! Damage output increased.");
            }
        }
    }

    // Enrage escalation: EnrageDamageMultiplier is the bonus at the triggering Round; every
    // Round after that, the bonus itself doubles (multiplicative on the bonus, not the
    // multiplier) — Round 18: +75%, Round 19: +150%, Round 20: +300%, Round 21: +600%, etc.
    private static double GetEnrageMultiplier(Enemy enemy, int currentRound)
    {
        if (!enemy.IsEnraged || enemy.EnrageTriggeredRound is not int triggeredRound)
        {
            return 1.0;
        }

        var baselineBonus = enemy.EnrageDamageMultiplier - 1.0;
        var roundsSinceTrigger = currentRound - triggeredRound;
        var escalatedBonus = baselineBonus * Math.Pow(2, roundsSinceTrigger);
        return 1.0 + escalatedBonus;
    }

    private IEnumerable<ICombatant> AllCombatants() =>
        _state.PlayerTeam.Cast<ICombatant>().Concat(_state.EnemyTeam.Cast<ICombatant>());

    private List<ICombatant> InitiativePhase()
    {
        // PvE tie-break (no PvP yet, so no need for a fairness-preserving roll): on equal
        // Speed, the player's team always goes first; within a tied team, lower position
        // (1 before 2 before 3) goes first. Fully deterministic — no randomness.
        var alive = AllCombatants().Where(c => c.CurrentHp > 0);

        // Providence: acts first regardless of Speed -- a direct check here rather than an
        // event subscription, since it's turn-order logic, not a reaction (see CLAUDE.md).
        // Providence-holders are still ordered amongst themselves (and the rest of the field
        // amongst themselves) by the same Speed/team/position rule, just as separate groups.
        return alive
            .OrderByDescending(HasProvidence)
            .ThenByDescending(GetEffectiveSpeed)
            .ThenBy(c => c is Enemy) // false (player) sorts before true (enemy)
            .ThenBy(c => c.Position)
            .ToList();
    }

    private static bool HasProvidence(ICombatant combatant) =>
        combatant is Anima { Crest.Name: "Providence" };

    // Frenzy (Crimson): +20% Speed for Initiative purposes while active. Kept separate from the
    // base Speed property (rather than mutating BaseStats.Speed) so the boost cleanly disappears
    // the moment Frenzy is consumed, with nothing to remember to revert.
    private double GetEffectiveSpeed(ICombatant combatant) =>
        combatant.ActiveStatuses.Any(s => s.Keyword == "Frenzy") ? combatant.Speed * FrenzyMultiplier : combatant.Speed;

    private void LogInitiativeOrder(List<ICombatant> initiativeOrder)
    {
        var order = string.Join(" > ", initiativeOrder.Select(c => $"{c.DisplayName}(Spd {GetEffectiveSpeed(c):0.##})"));
        Log($"Initiative: {order}");
    }

    private void ActionPhase(List<ICombatant> initiativeOrder)
    {
        foreach (var actor in initiativeOrder)
        {
            if (actor.CurrentHp <= 0) continue; // may have died mid-round
            if (ConsumeStun(actor)) continue; // skips this actor's turn entirely

            switch (actor)
            {
                case Anima anima:
                    ResolvePlayerTurn(anima);
                    break;
                case Enemy enemy:
                    ResolveEnemyTurn(enemy);
                    break;
            }

            // Silent Chime: grants the targeted Anima one immediate extra action right after
            // their current action resolves, within the same Round -- checked here, right after
            // the normal turn above, rather than re-entering the initiative loop.
            if (actor is Anima chimeTarget && ReferenceEquals(_pendingExtraActionAnima, chimeTarget) && chimeTarget.CurrentHp > 0)
            {
                _pendingExtraActionAnima = null;
                Log($"  Silent Chime grants {chimeTarget.DisplayName} an extra action!");
                ResolvePlayerTurn(chimeTarget);
            }
        }
    }

    // Pin: stuns its target, skipping their next turn entirely. Until-Consumed (not
    // Fixed-turn:1) for the same reason as Weak/Marked -- a stun applied by a slower
    // combatant must survive Round Start's tick to still skip the target's next actual turn.
    private bool ConsumeStun(ICombatant actor)
    {
        var stun = GetStatus(actor, "Stunned");
        if (stun == null) return false;

        actor.ActiveStatuses.Remove(stun);
        Log($"{actor.DisplayName} is Stunned and skips their turn.");
        return true;
    }

    // Melee (1-2) / Ranged (2-3) restrict which positions a skill can be used FROM; a narrower
    // UsableFromOverride (e.g. Marked Shot's position-3-only) wins when set. The field already
    // existed on Skill for this, but nothing actually checked it until now.
    private static bool IsSkillUsableFrom(Skill skill, int position)
    {
        if (skill.UsableFromOverride is { Length: > 0 } positions) return positions.Contains(position);

        return skill.Range switch
        {
            AttackRange.Melee => position is 1 or 2,
            AttackRange.Ranged => position is 2 or 3,
            _ => true,
        };
    }

    private void ResolvePlayerTurn(Anima anima)
    {
        var skill = ChoosePlayerSkill?.Invoke(anima, _state);
        if (skill == null)
        {
            Log($"{anima.DisplayName} passes.");
            return;
        }

        if (!IsSkillUsableFrom(skill, anima.Position))
        {
            Log($"{anima.DisplayName} cannot use {skill.Name} from position {anima.Position} -- passes.");
            return;
        }

        var energyCost = GetEffectiveEnergyCost(anima, skill);
        Log($"{anima.DisplayName} uses {skill.Name} ({energyCost} energy).");
        _state.SharedEnergy -= energyCost;

        // Focusing Lens: every 4th Attack-category skill the PLAYER TEAM plays this combat
        // (team-wide counter, same shape as SharedEnergy) deals double damage -- decided once
        // here, the moment the skill is confirmed to actually resolve, then threaded through
        // ResolveSkill/ResolveAttack/ResolveSingleTargetAttack the same way frenzied/weakMagnitude
        // already are, rather than re-checking a persistent "count % 4 == 0" state deeper in the
        // pipeline (which could spuriously re-trigger on an unrelated later hit, e.g. a
        // Retaliate/Thorns counter, while the counter happens to still read a multiple of 4).
        var focusingLensTriggered = false;
        if (skill.Category == SkillCategory.Attack && HasArtifact("Focusing Lens"))
        {
            _state.AttackSkillsPlayed++;
            focusingLensTriggered = _state.AttackSkillsPlayed % FocusingLensInterval == 0;
            if (focusingLensTriggered)
            {
                Log($"  Focusing Lens triggers: {anima.DisplayName}'s attack (#{_state.AttackSkillsPlayed}) deals double damage!");
            }
        }

        ResolveSkill(
            anima,
            skill,
            _state.EnemyTeam.Cast<ICombatant>().ToList(),
            _state.PlayerTeam.Cast<ICombatant>().ToList(),
            anima.BaseStats.DamageMultiplier,
            anima.BaseStats.SpiritMultiplier,
            focusingLensTriggered);

        _state.Hand.Remove(skill);
        _state.DiscardPile.Add(skill);
    }

    // Clarity (Verdant Cleansing kit): -1 energy cost (min 0) on the skill this Anima plays.
    // The design doc frames this as "the first skill this Anima plays each turn" -- under the
    // current one-action-per-Round engine (see CLAUDE.md's Action Phase), every skill a
    // combatant plays already IS that Anima's only (hence first) play of its turn, so this
    // simplifies to a flat per-use discount. Scoped to just this Anima's own energy spend, not
    // a team-wide discount -- other Animas acting the same Round still pay full cost.
    private static int GetEffectiveEnergyCost(Anima anima, Skill skill) =>
        anima.Crest.Name == "Clarity" ? Math.Max(0, skill.EnergyCost - 1) : skill.EnergyCost;

    private void ResolveEnemyTurn(Enemy enemy)
    {
        // Turn-start heal-the-boss passive (e.g. Warden's DPS-race add) — fires before rule
        // selection, so it happens regardless of what (if anything) this enemy does this turn.
        if (enemy.HealsOnTurnStartTarget is { CurrentHp: > 0 } healTarget && enemy.HealsOnTurnStartAmount > 0)
        {
            var before = healTarget.CurrentHp;
            healTarget.CurrentHp = Math.Min(healTarget.MaxHp, healTarget.CurrentHp + enemy.HealsOnTurnStartAmount);
            Log($"  {enemy.DisplayName} channels {healTarget.CurrentHp - before} HP into {healTarget.DisplayName} ({healTarget.DisplayName} HP: {healTarget.CurrentHp}).");
        }

        var rule = enemy.BehaviorRules.FirstOrDefault(r =>
            (!enemy.IsEnraged || !r.IsDefensive) && r.Condition(enemy, _state) && IsSkillUsableFrom(r.Skill, enemy.Position));
        if (rule == null)
        {
            Log($"{enemy.DisplayName} has no valid action.");
            return;
        }

        Log($"{enemy.DisplayName} uses {rule.Skill.Name}.");
        ResolveSkill(
            enemy,
            rule.Skill,
            _state.PlayerTeam.Cast<ICombatant>().ToList(),
            _state.EnemyTeam.Cast<ICombatant>().ToList(),
            damageMultiplier: 1.0,
            spiritMultiplier: 1.0);
        rule.OnUsed?.Invoke(enemy);
    }

    private void ResolveSkill(
        ICombatant actor,
        Skill skill,
        List<ICombatant> opposingTeam,
        List<ICombatant> friendlyTeam,
        double damageMultiplier,
        double spiritMultiplier,
        bool focusingLensTriggered = false)
    {
        // Weak is Until-Consumed (same pattern as Shield/Primed) — it persists on its target
        // until that target's next skill use of any kind, regardless of Round/turn-order timing.
        var weakMagnitude = ConsumeWeak(actor);

        // Frenzy: same Until-Consumed reasoning -- consumed on the caster's own next action of
        // any kind (matching Weak's consumption scope), though the damage boost only manifests
        // for Attack.
        var frenzied = ConsumeFrenzy(actor);

        switch (skill.Category)
        {
            case SkillCategory.Attack:
                ResolveAttack(actor, skill, opposingTeam, friendlyTeam, damageMultiplier, spiritMultiplier, weakMagnitude, frenzied, focusingLensTriggered);
                break;
            case SkillCategory.Move:
                ResolveMove(actor, skill, opposingTeam, friendlyTeam);
                break;
            case SkillCategory.Buff:
                ResolveBuff(actor, skill, friendlyTeam);
                break;
            case SkillCategory.Debuff:
                ResolveDebuff(actor, skill, opposingTeam, friendlyTeam);
                break;
            case SkillCategory.Heal:
                ResolveHeal(actor, skill, friendlyTeam, spiritMultiplier, weakMagnitude);
                break;
            case SkillCategory.Summon:
                ResolveSummon(actor, skill);
                break;
            default:
                Log($"  ({skill.Category} skills aren't resolved yet.)");
                break;
        }
    }

    private int ConsumeWeak(ICombatant actor)
    {
        var weak = GetStatus(actor, "Weak");
        if (weak == null) return 0;

        var magnitude = weak.Magnitude;
        ReduceStatusCharge(actor, weak);
        Log(weak.Charges > 0
            ? $"  {actor.DisplayName}'s Weak is consumed ({weak.Charges} charge(s) remaining -- extended by Augment)."
            : $"  {actor.DisplayName}'s Weak is consumed.");
        return magnitude;
    }

    private bool ConsumeFrenzy(ICombatant actor)
    {
        var frenzy = GetStatus(actor, "Frenzy");
        if (frenzy == null) return false;

        actor.ActiveStatuses.Remove(frenzy);
        Log($"  {actor.DisplayName}'s Frenzy is consumed.");
        return true;
    }

    // Reckless (Crimson)/Vengeance (Onyx): +25% damage dealt while this Anima's own HP is
    // below 50% of MaxHp. Ambush (Azure): double damage when this Anima acts LAST in the
    // Round's Initiative order -- an intentionally ironic passive for Azure's high-Speed kit,
    // only realistically live once enough of the team has died that Azure ends up the slowest
    // survivor left to act. All self-referential, so checked here rather than via a status.
    private double GetOffensiveCrestMultiplier(ICombatant actor)
    {
        if (actor is not Anima anima) return 1.0;

        switch (anima.Crest.Name)
        {
            case "Reckless" or "Vengeance" when anima.CurrentHp < anima.MaxHp * 0.5:
                Log($"  {anima.DisplayName}'s {anima.Crest.Name} triggers: damage x{CrestConditionalMultiplier:0.##} (HP below 50%).");
                return CrestConditionalMultiplier;
            case "Ambush" when _currentInitiativeOrder.Count > 0 && ReferenceEquals(_currentInitiativeOrder[^1], actor):
                Log($"  {anima.DisplayName}'s Ambush triggers: damage x{AmbushMultiplier:0.##} (acted last this Round).");
                return AmbushMultiplier;
            case "Steady Aim" when anima.Position == 3:
                Log($"  {anima.DisplayName}'s Steady Aim triggers: damage x{SteadyAimMultiplier:0.##} (position 3).");
                return SteadyAimMultiplier;
            default:
                return 1.0;
        }
    }

    // Soul Link (Verdant): +25% healing done while this Anima's own HP is above 50% of MaxHp.
    private double GetHealingCrestMultiplier(ICombatant actor)
    {
        if (actor is not Anima anima) return 1.0;
        if (anima.Crest.Name != "Soul Link") return 1.0;
        if (anima.CurrentHp <= anima.MaxHp * 0.5) return 1.0;

        Log($"  {anima.DisplayName}'s Soul Link triggers: healing x{CrestConditionalMultiplier:0.##} (HP above 50%).");
        return CrestConditionalMultiplier;
    }

    // Courage (Onyx): -20% damage taken while this Anima is in position 1.
    private double GetCourageMultiplier(ICombatant target)
    {
        if (target is not Anima anima) return 1.0;
        if (anima.Crest.Name != "Courage") return 1.0;
        if (anima.Position != 1) return 1.0;

        return CourageDamageReduction;
    }

    private void ResolveAttack(
        ICombatant actor,
        Skill skill,
        List<ICombatant> opposingTeam,
        List<ICombatant> friendlyTeam,
        double damageMultiplier,
        double spiritMultiplier,
        int weakMagnitude,
        bool frenzied = false,
        bool focusingLensTriggered = false)
    {
        // AoE Attack (e.g. an "AoE Damage" augment converting Slash to hit everyone): repeats the
        // full single-target pipeline once per living enemy -- BaseDamage is expected to already
        // be pre-scaled (e.g. halved) by whatever set Target to AllEnemies, same as any other
        // BaseDamage value. Snapshotted via ToList() since a hit inside the loop can kill a later
        // target in the same list before its own turn comes up.
        if (skill.Target == TargetType.AllEnemies)
        {
            foreach (var aoeTarget in opposingTeam.Where(c => c.CurrentHp > 0).ToList())
            {
                if (aoeTarget.CurrentHp <= 0) continue; // may have died earlier in this same AoE sweep
                ResolveSingleTargetAttack(actor, skill, aoeTarget, friendlyTeam, damageMultiplier, spiritMultiplier, weakMagnitude, frenzied, focusingLensTriggered);
            }
            return;
        }

        var target = SelectTarget(skill.Target, opposingTeam);
        if (target == null)
        {
            Log("  No valid target.");
            return;
        }

        ResolveSingleTargetAttack(actor, skill, target, friendlyTeam, damageMultiplier, spiritMultiplier, weakMagnitude, frenzied, focusingLensTriggered);
    }

    private void ResolveSingleTargetAttack(
        ICombatant actor,
        Skill skill,
        ICombatant target,
        List<ICombatant> friendlyTeam,
        double damageMultiplier,
        double spiritMultiplier,
        int weakMagnitude,
        bool frenzied,
        bool focusingLensTriggered = false)
    {
        if (skill.Target is TargetType.Enemy or TargetType.Ally)
        {
            ConsumeMarked(target);
        }

        // Shatter (Onyx Bulwark kit): BaseDamage is ignored -- damage instead equals the caster's
        // current Shield magnitude, read at cast time, with the Shield then removed entirely
        // (not just consumed by absorption). Dynamic-damage skills read their source status BEFORE
        // the normal multiplier chain runs, same as any other skill's BaseDamage would.
        var baseDamage = skill.BaseDamage;
        if (skill.DamageEqualsOwnShield)
        {
            var ownShield = GetStatus(actor, "Shield");
            baseDamage = ownShield?.Magnitude ?? 0;
            if (ownShield != null)
            {
                actor.ActiveStatuses.Remove(ownShield);
                Log($"  {actor.DisplayName}'s Shield ({baseDamage}) is consumed by {skill.Name}.");
            }
        }

        var enrageMultiplier = actor is Enemy enrageActor ? GetEnrageMultiplier(enrageActor, _state.RoundNumber) : 1.0;
        // Phase-transition buffs (e.g. Warden's Reckless Fury) — flat and permanent once
        // triggered, stacked alongside (not instead of) the Round-based Enrage multiplier above.
        var permanentMultiplier = actor is Enemy permActor ? permActor.PermanentDamageMultiplier : 1.0;
        var raw = baseDamage * damageMultiplier * enrageMultiplier * permanentMultiplier;

        // Self modifiers (Reckless/Vengeance, Frenzy, and eventually Primed) apply first;
        // opponent-applied debuffs (Weak) apply last — same multiplier chain, self before opponent.
        raw *= GetOffensiveCrestMultiplier(actor);

        if (frenzied)
        {
            raw *= FrenzyMultiplier;
            Log($"  {actor.DisplayName}'s Frenzy triggers: damage x{FrenzyMultiplier:0.##}.");
        }

        // Focusing Lens: the "is this the 4th/8th/12th attack" decision was already made once,
        // upstream in ResolvePlayerTurn -- this just applies it, same as frenzied above.
        if (focusingLensTriggered)
        {
            raw *= FocusingLensMultiplier;
            Log($"  {actor.DisplayName}'s Focusing Lens triggers: damage x{FocusingLensMultiplier:0.##}.");
        }

        if (weakMagnitude > 0)
        {
            raw *= 1 - weakMagnitude / 100.0;
            Log($"  {actor.DisplayName} is Weak: damage reduced by {weakMagnitude}%.");
        }

        var rawInt = (int)Math.Round(raw);
        var multLabel = $"{damageMultiplier:0.##} mult";
        if (enrageMultiplier > 1.0) multLabel += $" x {enrageMultiplier:0.##} enrage";
        if (permanentMultiplier > 1.0) multLabel += $" x {permanentMultiplier:0.##} phase2";
        var final = ApplyDamage(target, rawInt, $"{baseDamage} base x {multLabel}", actor);

        TriggerReactiveEffects(actor, target, skill);

        if (skill.OnHitStatusKeyword != null)
        {
            ApplyOnHitStatus(target, skill.OnHitStatusKeyword, skill.OnHitStatusMagnitude, skill.OnHitStatusDuration, skill.OnHitStatusDurationTurns ?? 0, skill.OnHitStatusExtraCharges);
        }

        if (skill.RemovesBuff)
        {
            RemoveOneBuff(target);
        }

        if (skill.BaseHeal > 0 && skill.SecondaryTarget != null)
        {
            var healTarget = SelectTarget(skill.SecondaryTarget.Value, friendlyTeam);
            if (healTarget != null)
            {
                ApplyHeal(actor, healTarget, skill.BaseHeal, spiritMultiplier, weakMagnitude);
            }
        }

        if (skill.SelfHealPercentOfDamage is double pct && final > 0)
        {
            var healAmount = (int)Math.Round(final * pct);
            var before = actor.CurrentHp;
            actor.CurrentHp = Math.Min(actor.CurrentHp + healAmount, actor.MaxHp);
            Log($"  {actor.DisplayName} drains {actor.CurrentHp - before} HP from the hit ({actor.DisplayName} HP: {actor.CurrentHp}).");
        }

        // Guard Strike (Onyx Bulwark kit): grants Shield equal to a percentage of damage dealt --
        // routes through the same GrantShield/TriggerInspire pipeline as any other Shield source,
        // so Inspire triggers off this too.
        if (skill.SelfShieldPercentOfDamage is double shieldPct && final > 0)
        {
            var shieldAmount = (int)Math.Round(final * shieldPct);
            var granted = GrantShield(actor, shieldAmount);
            TriggerInspire(actor, granted, friendlyTeam);
        }

        // Deflect (Azure Pseudo-Heal kit): a flat self-Shield grant bolted onto an Attack skill --
        // distinct from SelfShieldPercentOfDamage above (Guard Strike), which scales off the
        // damage just dealt; this is just BaseShield, the same flat field Buff skills
        // (Hardened/Fortify) use, routed through the same GrantShield/TriggerInspire pipeline.
        if (skill.BaseShield > 0)
        {
            var granted = GrantShield(actor, skill.BaseShield);
            TriggerInspire(actor, granted, friendlyTeam);
        }

        // Bloodthirst (Crimson): unlike SelfHealPercentOfDamage above (a per-skill effect, e.g.
        // Leech Mother's Draining Claw), this is a Crest passive -- applies to every attack this
        // Anima lands, not just one specific skill.
        if (GetLifestealCrestPercent(actor) is double lifestealPct && final > 0)
        {
            var healAmount = (int)Math.Round(final * lifestealPct);
            var before = actor.CurrentHp;
            actor.CurrentHp = Math.Min(actor.CurrentHp + healAmount, actor.MaxHp);
            Log($"  {actor.DisplayName}'s Bloodthirst triggers: heals {actor.CurrentHp - before} HP ({actor.DisplayName} HP: {actor.CurrentHp}).");
        }

        // Lunge (Crimson): moves the caster forward one position as part of the attack itself,
        // rather than being a separate Move-category skill -- reuses MoveOffset, the same field
        // Push/Pull/Ally-advance use for a Move skill's target shift.
        if (skill.MoveOffset is int selfMoveOffset)
        {
            var beforePosition = actor.Position;
            actor.Position = Math.Clamp(actor.Position + selfMoveOffset, 1, 3);
            Log($"  {actor.DisplayName} moves from position {beforePosition} to {actor.Position}.");
        }

        // Shove (Azure Pseudo-Tank kit): shifts the ATTACK'S TARGET instead of the caster --
        // distinct from MoveOffset above, which Lunge uses to move the caster itself.
        if (skill.TargetMoveOffset is int targetMoveOffset)
        {
            var beforeTargetPosition = target.Position;
            target.Position = Math.Clamp(target.Position + targetMoveOffset, 1, 3);
            Log($"  {target.DisplayName} is shoved from position {beforeTargetPosition} to {target.Position}.");
        }

        // Bloom (Verdant Sustain kit): a Ranged attack that also refreshes an existing HOT on an
        // ally, if one is currently active -- see RefreshHotOnAlly.
        if (skill.RefreshesHotKeyword != null)
        {
            RefreshHotOnAlly(friendlyTeam, skill.RefreshesHotKeyword, skill.RefreshesHotDurationTurns ?? 0);
        }
    }

    // Bloodthirst (Crimson): heals 25% of the damage this Anima deals, on every hit.
    private static double? GetLifestealCrestPercent(ICombatant actor) =>
        actor is Anima { Crest.Name: "Bloodthirst" } ? BloodthirstLifestealPercent : null;

    // Shared damage pipeline (Shield absorb, then Defense floor) — used by both direct attacks
    // and reactive counter-damage (Retaliate/Thorns), so counters respect the attacker's own
    // Shield/Defense rather than being a bypassing special case. `source` is whoever dealt THIS
    // particular hit (the original attacker for a direct hit, or the counter-caster for a
    // Retaliate/Thorns reflection) -- nullable/optional since most callers don't need Retribution
    // to see it, but ResolveAttack and TriggerReactiveEffects both pass theirs through.
    private int ApplyDamage(ICombatant target, int rawDamage, string sourceDescription, ICombatant? source = null)
    {
        var remaining = rawDamage;

        var shield = GetStatus(target, "Shield");
        var absorbed = 0;
        if (shield != null && remaining > 0)
        {
            absorbed = Math.Min(shield.Magnitude, remaining);
            shield.Magnitude -= absorbed;
            remaining -= absorbed;
            if (shield.Magnitude <= 0)
            {
                target.ActiveStatuses.Remove(shield);
                TriggerRetribution(target, source);
            }
        }

        // Exposed (Crimson Ranged kit, Marked Shot): an external vulnerability debuff, +25%
        // incoming damage. Until-Consumed, consumed here the moment it actually affects a hit --
        // same "consumed on use" reasoning as Shield/Guarded, since this protects against (well,
        // worsens) INCOMING damage rather than the target's own next action.
        var exposed = GetStatus(target, "Exposed");
        var preExposed = remaining;
        if (exposed != null && remaining > 0)
        {
            remaining = (int)Math.Round(remaining * ExposedMultiplier);
            ReduceStatusCharge(target, exposed);
            Log(exposed.Charges > 0
                ? $"  {target.DisplayName} is Exposed: incoming damage x{ExposedMultiplier:0.##} ({exposed.Charges} charge(s) remaining -- extended by Augment)."
                : $"  {target.DisplayName} is Exposed: incoming damage x{ExposedMultiplier:0.##}.");
        }

        // Courage: a percentage reduction on the target's own side, applied before Defense's
        // flat subtraction (not after) so it stays meaningful even against hits that would
        // otherwise floor out at 1 — a post-Defense percentage cut would round away to nothing.
        var courageMultiplier = GetCourageMultiplier(target);
        var preCourage = remaining;
        if (remaining > 0 && courageMultiplier < 1.0)
        {
            remaining = (int)Math.Round(remaining * courageMultiplier);
            Log($"  {target.DisplayName}'s Courage triggers: incoming damage x{courageMultiplier:0.##} (position 1).");
        }

        // Guarded (Crimson Ranged kit, Retreat): a flat, single-hit Defense bonus. Until-Consumed,
        // consumed here the moment it actually reduces a hit -- same pattern as Shield/Exposed.
        var guarded = GetStatus(target, "Guarded");
        var effectiveDefense = target.Defense;
        if (guarded != null && remaining > 0)
        {
            effectiveDefense += guarded.Magnitude;
            target.ActiveStatuses.Remove(guarded);
            Log($"  {target.DisplayName}'s Guarded triggers: +{guarded.Magnitude} Defense for this hit.");
        }

        var final = remaining > 0 ? Math.Max(remaining - effectiveDefense, 1) : 0;
        var wasAlive = target.CurrentHp > 0;

        // Twin Flame: once per combat, a hit that would drop a player Anima to 0 HP or below
        // instead leaves them at exactly 1 -- checked here, the single choke point every
        // HP-reducing hit (direct attacks AND reactive Retaliate/Thorns counters) already passes
        // through. Scoped to the player's own team only (Artifacts are player-owned gear) and to
        // "hits" specifically -- Bleed's DOT tick applies HP loss directly in
        // TickStatusDurations, bypassing this the same way it already bypasses Defense/Shield by
        // design, so it's deliberately NOT covered here.
        if (wasAlive && final >= target.CurrentHp && target is Anima && !_state.TwinFlameUsed && HasArtifact("Twin Flame"))
        {
            _state.TwinFlameUsed = true;
            target.CurrentHp = 1;
            Log($"  {target.DisplayName}'s Twin Flame artifact triggers! Left at 1 HP instead of being defeated.");
        }
        else
        {
            target.CurrentHp = Math.Max(0, target.CurrentHp - final);
        }

        Log($"  Target: {target.DisplayName}");
        var afterShield = absorbed > 0 ? $"{rawDamage} raw - {absorbed} shield = {preExposed}" : $"{rawDamage} raw";
        var afterExposed = exposed != null ? $" x{ExposedMultiplier:0.##} Exposed = {preCourage}" : "";
        var afterCourage = courageMultiplier < 1.0 ? $" x{courageMultiplier:0.##} Courage = {remaining}" : "";
        Log($"  Damage: {sourceDescription} = {afterShield}{afterExposed}{afterCourage} - {effectiveDefense} def = {final} dealt");
        Log($"  {target.DisplayName} HP: {target.CurrentHp}");

        PurgeDeadAnimaCards(target);

        if (target is Enemy enemyTarget)
        {
            if (wasAlive && enemyTarget.CurrentHp <= 0)
            {
                enemyTarget.OnDeath?.Invoke(_state);
            }
            else if (enemyTarget.CurrentHp > 0)
            {
                CheckPhaseTwoTransition(enemyTarget);
            }
        }

        return final;
    }

    private void CheckPhaseTwoTransition(Enemy enemy)
    {
        if (enemy.PhaseTwoTriggered) return;
        if (enemy.PhaseTwoHpThreshold is not int threshold || enemy.CurrentHp > threshold) return;

        enemy.PhaseTwoTriggered = true;

        var shield = GetStatus(enemy, "Shield");
        if (shield != null)
        {
            enemy.ActiveStatuses.Remove(shield);
            Log($"  {enemy.DisplayName}'s Shield is stripped to 0 as it enters Phase 2.");
        }

        enemy.PermanentDamageMultiplier *= enemy.PhaseTwoDamageMultiplier;
        Log($"  {enemy.DisplayName} enters PHASE 2! Damage output permanently increased.");
    }

    private const int RetributionWeakMagnitude = 20; // matches Bash/Enfeeble's own Weak magnitude -- no other value specified in the design brief

    // Retribution (Azure Pseudo-Heal kit): when this Anima's own Shield is fully broken (reduced
    // to 0, not just chipped down) by a hit, applies Weak to whoever broke it. Checked from
    // inside ApplyDamage's shield-absorption block, which only reaches this line once the
    // Shield's Magnitude actually hits 0 -- a hit that merely reduces it without exhausting it
    // never calls this at all, so partial breaks correctly don't trigger Retribution.
    private void TriggerRetribution(ICombatant target, ICombatant? source)
    {
        if (target is not Anima { Crest.Name: "Retribution" } anima || source == null) return;

        ApplyStatus(source, "Weak", RetributionWeakMagnitude, DurationType.UntilConsumed, 0);
        Log($"  {anima.DisplayName}'s Retribution triggers: {source.DisplayName} is afflicted with Weak ({RetributionWeakMagnitude}%).");
    }

    // A dead Anima never takes another turn, so its cards would otherwise sit in Hand/DrawPile/
    // DiscardPile forever — never played, never discarded, permanently eating into the shared
    // hand cap. Removing them on death is a real gameplay rule, not just a test-harness fix.
    private void PurgeDeadAnimaCards(ICombatant combatant)
    {
        if (combatant is not Anima anima || anima.CurrentHp > 0) return;

        // A card only ever moves between Hand/DrawPile/DiscardPile -- it's never removed except
        // here -- so every one of this Anima's 9 cards is guaranteed to still be somewhere in
        // one of those three piles the FIRST time this runs for them, and guaranteed gone every
        // time after. That conservation invariant makes this an exact "has this Anima's death
        // already been processed?" check -- the gate both PurgeDeadAnimaCards' own log message
        // and TriggerLastLaugh below need to fire exactly once per death.
        var deckSkills = anima.DeckSkills;
        var stillInDeck = _state.Hand.Any(s => deckSkills.Contains(s))
            || _state.DrawPile.Any(s => deckSkills.Contains(s))
            || _state.DiscardPile.Any(s => deckSkills.Contains(s));
        if (!stillInDeck) return;

        _state.Hand.RemoveAll(s => deckSkills.Contains(s));
        _state.DrawPile.RemoveAll(s => deckSkills.Contains(s));
        _state.DiscardPile.RemoveAll(s => deckSkills.Contains(s));

        Log($"  {anima.DisplayName} has fallen — their remaining cards are removed from the deck.");
        TriggerLastLaugh(anima);
    }

    private const int BleedDurationTurns = 3; // matches Exploit/Rend's own Bleed application -- the "full" value Last Laugh refreshes back to

    // Last Laugh (Azure Pseudo-Tank kit): on this Anima's death, refreshes every currently-active
    // debuff on the enemy team back to full. In practice this only has a visible effect on Bleed
    // (the only FixedTurn debuff in the game so far) -- Weak/Marked are Until-Consumed, so
    // they're already "full" for as long as they're active; there's no decayed state to restore.
    private void TriggerLastLaugh(Anima fallenAnima)
    {
        if (fallenAnima.Crest.Name != "Last Laugh") return;

        foreach (var enemy in _state.EnemyTeam.Where(e => e.CurrentHp > 0))
        {
            foreach (var status in enemy.ActiveStatuses.Where(s => DebuffKeywords.Contains(s.Keyword) && s.Duration == DurationType.FixedTurn))
            {
                status.RemainingTurns = BleedDurationTurns;
                Log($"  {fallenAnima.DisplayName}'s Last Laugh triggers: {enemy.DisplayName}'s {status.Keyword} is refreshed ({status.RemainingTurns} turns left).");
            }
        }
    }

    // Retaliate/Thorns: flat counter-damage back at a Melee attacker, through the same
    // Shield/Defense pipeline as any other hit. Doesn't itself re-trigger reactive effects
    // (no counter-to-a-counter) and only fires if the defender survived the original hit.
    private void TriggerReactiveEffects(ICombatant attacker, ICombatant target, Skill skill)
    {
        if (skill.Range != AttackRange.Melee) return;
        if (target.CurrentHp <= 0) return;

        // Retaliate/Thorns counters route through GetOffensiveCrestMultiplier the same as a
        // normal attack -- so Vengeance (and any future offense-boosting crest) applies to
        // reactive counter-damage too, not just direct attacks. Enrage is deliberately NOT
        // applied here (it's ResolveAttack's own separate multiplier) -- no current enemy has
        // both Enrage and Retaliate/Thorns, so it hasn't come up.
        var retaliate = GetStatus(target, "Retaliate");
        if (retaliate != null)
        {
            target.ActiveStatuses.Remove(retaliate);
            var retaliateDamage = (int)Math.Round(retaliate.Magnitude * GetOffensiveCrestMultiplier(target));
            Log($"  {target.DisplayName} Retaliates!");
            ApplyDamage(attacker, retaliateDamage, "Retaliate counter", target);
        }

        var thorns = GetStatus(target, "Thorns");
        if (thorns != null)
        {
            target.ActiveStatuses.Remove(thorns);
            var thornsDamage = (int)Math.Round(thorns.Magnitude * GetOffensiveCrestMultiplier(target));
            Log($"  {target.DisplayName}'s Thorns triggers!");
            ApplyDamage(attacker, thornsDamage, "Thorns counter", target);
        }
    }

    private void ResolveHeal(ICombatant actor, Skill skill, List<ICombatant> friendlyTeam, double spiritMultiplier, int weakMagnitude)
    {
        // Healing Rain (Verdant Sustain kit): instant AoE heal, hits every living ally rather
        // than a single SelectTarget result.
        if (skill.Target == TargetType.AllAllies)
        {
            foreach (var ally in friendlyTeam.Where(c => c.CurrentHp > 0))
            {
                ApplyHeal(actor, ally, skill.BaseHeal, spiritMultiplier, weakMagnitude);
            }
            return;
        }

        var target = skill.Target == TargetType.SelfTarget ? actor : SelectTarget(skill.Target, friendlyTeam);
        if (target == null)
        {
            Log("  No valid target.");
            return;
        }

        if (skill.BaseHeal > 0)
        {
            ApplyHeal(actor, target, skill.BaseHeal, spiritMultiplier, weakMagnitude);
        }

        if (skill.RemovesDebuff)
        {
            RemoveOneDebuff(target);
        }

        // Renew (Verdant Sustain kit): Head skill whose entire effect IS the HOT (BaseHeal is 0),
        // applied/refreshed on its target here.
        if (skill.HotKeyword != null)
        {
            ApplyOrRefreshHot(target, skill.HotKeyword, skill.HotMagnitude, skill.HotDurationTurns ?? 0);
        }
    }

    // Renew-style HOT: refreshes (not stacks) if the target already carries this keyword's HOT,
    // same "refresh, don't duplicate" idea as Bleed's own refresh in ApplyOnHitStatus.
    private void ApplyOrRefreshHot(ICombatant target, string keyword, int magnitude, int durationTurns)
    {
        if (TryRefreshHot(target, keyword, durationTurns)) return;

        ApplyStatus(target, keyword, magnitude, DurationType.FixedTurn, durationTurns);
        Log($"  {target.DisplayName} gains {keyword} ({magnitude} healing/turn, {durationTurns} turns).");
    }

    private bool TryRefreshHot(ICombatant target, string keyword, int durationTurns)
    {
        var existing = GetStatus(target, keyword);
        if (existing == null) return false;

        existing.RemainingTurns = durationTurns;
        Log($"  {target.DisplayName}'s {keyword} is refreshed ({existing.Magnitude} healing/turn, {durationTurns} turns left).");
        return true;
    }

    // Bloom (Verdant Sustain kit): searches the whole friendly team for an existing HOT to
    // extend -- a no-op (not a fresh application) if nobody currently carries one, since Bloom
    // is a combo/support piece, not its own HOT source.
    private void RefreshHotOnAlly(List<ICombatant> friendlyTeam, string keyword, int durationTurns)
    {
        var carrier = friendlyTeam.FirstOrDefault(c => c.CurrentHp > 0 && GetStatus(c, keyword) != null);
        if (carrier == null)
        {
            Log($"  No active {keyword} to refresh.");
            return;
        }

        TryRefreshHot(carrier, keyword, durationTurns);
    }

    // Debuffs eligible for Cleanse-style removal — the negative statuses one Anima can impose
    // on another. Shield/Retaliate/Thorns are self-applied buffs, not debuffs, so they're excluded.
    private static readonly string[] DebuffKeywords = { "Weak", "Bleed", "Marked" };

    private void RemoveOneDebuff(ICombatant target)
    {
        var debuff = target.ActiveStatuses.FirstOrDefault(s => DebuffKeywords.Contains(s.Keyword));
        if (debuff == null) return;

        target.ActiveStatuses.Remove(debuff);
        Log($"  {target.DisplayName}'s {debuff.Keyword} is cleansed.");
    }

    // Purge (Verdant Cleansing kit): the offensive mirror of Cleanse -- strips one beneficial
    // status instead of a harmful one. Every self-applied/positive status in the game so far.
    private static readonly string[] BuffKeywords = { "Shield", "Primed", "Guarded", "Retaliate", "Thorns", "Frenzy", "Renew" };

    private void RemoveOneBuff(ICombatant target)
    {
        var buff = target.ActiveStatuses.FirstOrDefault(s => BuffKeywords.Contains(s.Keyword));
        if (buff == null) return;

        target.ActiveStatuses.Remove(buff);
        Log($"  {target.DisplayName}'s {buff.Keyword} is purged.");
    }

    private void ApplyHeal(ICombatant caster, ICombatant target, int baseHeal, double spiritMultiplier, int weakMagnitude)
    {
        var raw = baseHeal * spiritMultiplier;

        // Self modifiers (Soul Link) apply first; opponent-applied debuffs (Weak) apply last —
        // same ordering as the damage chain.
        raw *= GetHealingCrestMultiplier(caster);

        if (weakMagnitude > 0)
        {
            raw *= 1 - weakMagnitude / 100.0;
            Log($"  {caster.DisplayName} is Weak: healing reduced by {weakMagnitude}%.");
        }

        var healAmount = (int)Math.Round(raw);
        var before = target.CurrentHp;
        target.CurrentHp = Math.Min(target.CurrentHp + healAmount, target.MaxHp);
        var applied = target.CurrentHp - before;

        Log($"  Target: {target.DisplayName}");
        Log($"  Heal: {baseHeal} base x {spiritMultiplier:0.##} mult = {healAmount} healed ({applied} applied)");
        Log($"  {target.DisplayName} HP: {target.CurrentHp}");
    }

    private void ResolveMove(ICombatant actor, Skill skill, List<ICombatant> opposingTeam, List<ICombatant> friendlyTeam)
    {
        if (skill.TargetPositionOverride is { Length: > 0 } positions)
        {
            actor.Position = positions[0];
            Log($"  {actor.DisplayName} moves to position {actor.Position}.");
            return;
        }

        if (skill.MoveOffset is int offset)
        {
            var team = IsFriendlyTargetType(skill.Target) ? friendlyTeam : opposingTeam;
            var target = skill.Target == TargetType.SelfTarget ? actor : SelectTarget(skill.Target, team);
            if (target == null)
            {
                Log("  No valid target.");
                return;
            }

            var before = target.Position;
            target.Position = Math.Clamp(target.Position + offset, 1, 3);
            Log($"  {target.DisplayName} moves from position {before} to {target.Position}.");
        }
    }

    private static bool IsFriendlyTargetType(TargetType targetType) =>
        targetType is TargetType.Ally or TargetType.ChosenAny or TargetType.LowestHpAlly or TargetType.AllAllies or TargetType.SelfTarget;

    private const int MaxShieldMagnitude = 50;

    // Grants Shield to a target, stacking additively onto any existing Shield (capped at 50) --
    // shared by ResolveBuff's BaseShield skills (Hardened/Fortify), Guard Strike's
    // damage-dealt Shield, and Inspire's ally-share. Returns the actual amount added, which may
    // be less than requested once the cap is hit.
    private int GrantShield(ICombatant target, int amount)
    {
        if (amount <= 0) return 0;

        var existing = GetStatus(target, "Shield");
        if (existing != null)
        {
            var before = existing.Magnitude;
            existing.Magnitude = Math.Min(existing.Magnitude + amount, MaxShieldMagnitude);
            var added = existing.Magnitude - before;
            Log($"  {target.DisplayName} gains {added} Shield (now {existing.Magnitude}).");
            return added;
        }

        var granted = Math.Min(amount, MaxShieldMagnitude);
        ApplyStatus(target, "Shield", granted, DurationType.UntilConsumed, 0);
        Log($"  {target.DisplayName} gains {granted} Shield.");
        return granted;
    }

    // Inspire (Onyx Bulwark kit): whenever this Anima gains Shield (from any source), all OTHER
    // living allies gain 30% of the actual (post-cap) amount just granted. Shares via GrantShield
    // directly rather than this wrapper, so a hypothetical second Inspire-bearer on the same team
    // wouldn't chain-trigger off their own shared amount.
    private void TriggerInspire(ICombatant actor, int shieldGranted, List<ICombatant> friendlyTeam)
    {
        if (actor is not Anima anima || anima.Crest.Name != "Inspire" || shieldGranted <= 0) return;

        var shareAmount = (int)Math.Round(shieldGranted * InspireSharePercent);
        if (shareAmount <= 0) return;

        foreach (var ally in friendlyTeam.Where(a => a.CurrentHp > 0 && !ReferenceEquals(a, actor)))
        {
            var allyGranted = GrantShield(ally, shareAmount);
            if (allyGranted > 0)
            {
                Log($"  {anima.DisplayName}'s Inspire triggers: {ally.DisplayName} shares in the Shield.");
            }
        }
    }

    private void ResolveBuff(ICombatant actor, Skill skill, List<ICombatant> friendlyTeam)
    {
        // Ward (Azure Pseudo-Heal kit): instant AoE Shield, hits every living ally rather than a
        // single SelectTarget result -- same shape as Healing Rain's AoE heal in ResolveHeal.
        if (skill.Target == TargetType.AllAllies)
        {
            foreach (var ally in friendlyTeam.Where(c => c.CurrentHp > 0))
            {
                var granted = GrantShield(ally, skill.BaseShield);
                TriggerInspire(ally, granted, friendlyTeam);
            }
            return;
        }

        // Safeguard (Azure Pseudo-Heal kit): the first Buff skill to target someone OTHER than
        // its own caster -- `target` resolves to an ally for those, and to `actor` (unchanged
        // behavior) for every existing SelfTarget Buff skill.
        var target = skill.Target == TargetType.SelfTarget ? actor : SelectTarget(skill.Target, friendlyTeam);
        if (target == null)
        {
            Log("  No valid target.");
            return;
        }

        // Move component, shared by every Buff variant below (Hardened/Charge/Retreat pair a
        // self-move with their buff; Safeguard moves an ALLY instead) -- same MoveOffset field
        // ResolveAttack reads for Lunge, applied here before any of the category-specific
        // branches (which return early).
        if (skill.MoveOffset is int selfMoveOffset)
        {
            var beforePosition = target.Position;
            target.Position = Math.Clamp(target.Position + selfMoveOffset, 1, 3);
            Log($"  {target.DisplayName} moves from position {beforePosition} to {target.Position}.");
        }

        if (skill.BaseShield > 0)
        {
            var granted = GrantShield(target, skill.BaseShield);
            TriggerInspire(target, granted, friendlyTeam);
            return;
        }

        if (skill.Name == "Taunt")
        {
            // Taunt is "apply Marked to self" -- it shares Marked's underlying status/slot
            // rather than maintaining a separate implementation.
            ApplyMarked(actor, friendlyTeam);
            return;
        }

        if (skill.Name == "Retreat")
        {
            // Guarded: a flat, single-hit Defense bonus -- Until-Consumed (not Fixed-turn) and
            // consumed by ApplyDamage the moment it actually reduces an incoming hit, same
            // reasoning/pattern as Shield rather than Weak/Frenzy (this protects against
            // INCOMING damage, not the caster's own next outgoing action).
            ApplyStatus(actor, "Guarded", skill.BuffMagnitude, DurationType.UntilConsumed, 0);
            Log($"  {actor.DisplayName} gains Guarded (+{skill.BuffMagnitude} Defense for the next hit).");
            return;
        }

        if (skill.Name is "Intercept" or "Bristle")
        {
            // Retaliate/Thorns: Until-Consumed (not Fixed-turn), consumed by TriggerReactiveEffects
            // the moment they actually fire a counter -- same "next incoming hit" reasoning as
            // Guarded/Exposed. A Fixed-turn:1 duration (as the design doc's status table literally
            // says) would tick away at the very next Round Start, before a slower Onyx bearer's
            // reactive counter ever got a chance to matter against a faster attacker -- the same
            // class of timing bug already found for Weak and Taunt/Marked, just never triggered
            // before now since no sample Anima had used Retaliate/Thorns until this kit.
            var keyword = skill.Name == "Intercept" ? "Retaliate" : "Thorns";
            ApplyStatus(actor, keyword, skill.BuffMagnitude, DurationType.UntilConsumed, 0);
            Log($"  {actor.DisplayName} gains {keyword} ({skill.BuffMagnitude}).");
            return;
        }

        // Simplification: a non-Shield Buff skill applies a status named after itself to its caster.
        ApplyStatus(actor, skill.Name, skill.BuffMagnitude, skill.Duration, skill.DurationTurns ?? 0);
        Log(skill.BuffMagnitude > 0
            ? $"  {actor.DisplayName} gains {skill.Name} ({skill.BuffMagnitude})."
            : $"  {actor.DisplayName} gains {skill.Name}.");
    }

    // Non-Attack, non-self debuffs that pick a target other than the caster (Pin's Stun,
    // Misdirect's Marked) -- distinct from ResolveBuff, which always targets the caster.
    private void ResolveDebuff(ICombatant actor, Skill skill, List<ICombatant> opposingTeam, List<ICombatant> friendlyTeam)
    {
        var team = IsFriendlyTargetType(skill.Target) ? friendlyTeam : opposingTeam;
        var target = SelectTarget(skill.Target, team);
        if (target == null)
        {
            Log("  No valid target.");
            return;
        }

        if (skill.Target is TargetType.Enemy or TargetType.Ally)
        {
            ConsumeMarked(target);
        }

        switch (skill.Name)
        {
            case "Pin" or "Disarm":
                ApplyStatus(target, "Stunned", 0, DurationType.UntilConsumed, 0);
                Log($"  {target.DisplayName} is Stunned.");
                break;
            case "Misdirect":
                ApplyMarked(target, team);
                break;
            default:
                Log($"  ({skill.Name} debuff isn't resolved yet.)");
                break;
        }
    }

    // Adds a new combatant to the actor's own side mid-fight (e.g. Leech Mother's Spawn Brood).
    // Enemy-only for now -- no player-side summon skill exists yet. Added directly to
    // _state.EnemyTeam (not a team-list copy) so next Round's InitiativePhase -- which
    // re-queries _state.PlayerTeam/EnemyTeam fresh via AllCombatants() -- picks the new
    // combatant up automatically.
    private void ResolveSummon(ICombatant actor, Skill skill)
    {
        if (actor is not Enemy enemyActor) return;

        // SummonFactoryChoices (e.g. Warden's 50/50 add roll) takes priority over a single fixed
        // SummonFactory when both would somehow be set -- an explicit, documented RNG use via
        // the same _random the deck shuffle already uses.
        Func<Enemy>? factory = skill.SummonFactoryChoices is { Length: > 0 } choices
            ? choices[_random.Next(choices.Length)]
            : skill.SummonFactory;
        if (factory == null) return;

        var summon = factory();

        if (skill.SummonInFront)
        {
            // Guard-style summon (e.g. Rustling Swarm): the add takes position 1 and the caster
            // is pushed back to the next open position, so default front-only targeting now
            // hits the add instead of the real threat -- Misdirect becomes the tool that cuts
            // past it.
            var occupiedBehind = _state.EnemyTeam.Where(e => e.CurrentHp > 0 && e != enemyActor).Select(e => e.Position).ToHashSet();
            var pushedPosition = Enumerable.Range(2, 2).FirstOrDefault(p => !occupiedBehind.Contains(p));
            if (pushedPosition == 0)
            {
                Log("  No open position to push back into -- summon skipped.");
                return;
            }

            Log($"  {enemyActor.DisplayName} is pushed back to position {pushedPosition} as {summon.DisplayName} takes the front!");
            enemyActor.Position = pushedPosition;
            summon.Position = 1;
        }
        else
        {
            var occupied = _state.EnemyTeam.Where(e => e.CurrentHp > 0).Select(e => e.Position).ToHashSet();
            var openPosition = Enumerable.Range(1, 3).FirstOrDefault(p => !occupied.Contains(p));
            if (openPosition == 0)
            {
                Log("  No open position for the summon -- skipped.");
                return;
            }

            summon.Position = openPosition;
        }

        _state.EnemyTeam.Add(summon);
        Log($"  {summon.DisplayName} is summoned into position {summon.Position}!");
    }

    // A team can only have one Marked target at a time -- applying a new Marked (whether via
    // Taunt targeting self, or a future Misdirect-style skill targeting an ally) strips any
    // existing Marked on that team first. Until-Consumed: persists until the next opposing
    // action that would target this team, redirects it here, then is removed by ConsumeMarked
    // -- fixing the same turn-order timing bug already found for Weak, where a 1-turn status
    // applied by the slower combatant died before the faster enemy's next turn ever used it.
    private void ApplyMarked(ICombatant target, List<ICombatant> team)
    {
        foreach (var member in team)
        {
            var existing = GetStatus(member, "Marked");
            if (existing != null)
            {
                member.ActiveStatuses.Remove(existing);
            }
        }

        ApplyStatus(target, "Marked", 0, DurationType.UntilConsumed, 0);
        Log($"  {target.DisplayName} is Marked.");
    }

    // Consumes Marked at the moment it actually redirects a targeting decision (i.e. only for
    // the Enemy/Ally target types SelectTarget gives Marked priority on) -- not for e.g. a
    // LowestHpEnemy skill that happens to land on the Marked combatant for unrelated reasons.
    private void ConsumeMarked(ICombatant target)
    {
        var marked = GetStatus(target, "Marked");
        if (marked == null) return;

        target.ActiveStatuses.Remove(marked);
        Log($"  {target.DisplayName}'s Marked is consumed by the redirected action.");
    }

    // On-hit status application for Attack skills (Weak, Bleed, ...). Bleed refreshes its
    // existing instance's remaining duration instead of stacking a second simultaneous DOT --
    // same "don't duplicate" idea as Shield's magnitude-stacking, just applied to duration.
    // Every other on-hit status is a fresh, independent application.
    private void ApplyOnHitStatus(ICombatant target, string keyword, int magnitude, DurationType duration, int durationTurns, int extraCharges = 0)
    {
        if (keyword == "Bleed")
        {
            var existingBleed = GetStatus(target, "Bleed");
            if (existingBleed != null)
            {
                existingBleed.RemainingTurns = durationTurns;
                Log($"  {target.DisplayName}'s Bleed is refreshed ({magnitude} dmg/turn, {durationTurns} turns left).");
                return;
            }

            ApplyStatus(target, keyword, magnitude, duration, durationTurns);
            Log($"  {target.DisplayName} is afflicted with Bleed ({magnitude} dmg/turn, {durationTurns} turns).");
            return;
        }

        // Extend Augment: extraCharges is only ever nonzero for a genuinely UntilConsumed keyword
        // (AugmentService rejects Extend otherwise), so a FixedTurn status like Bleed above never
        // reaches this line with a nonzero value to begin with.
        ApplyStatus(target, keyword, magnitude, duration, durationTurns, 1 + extraCharges);
        Log(extraCharges > 0
            ? $"  {target.DisplayName} is afflicted with {keyword} ({magnitude}%, {1 + extraCharges} charges -- extended by Augment)."
            : $"  {target.DisplayName} is afflicted with {keyword} ({magnitude}%).");
    }

    private static void ApplyStatus(ICombatant target, string keyword, int magnitude, DurationType duration, int durationTurns, int charges = 1)
    {
        target.ActiveStatuses.Add(new StatusEffectInstance
        {
            Keyword = keyword,
            Magnitude = magnitude,
            Duration = duration,
            RemainingTurns = durationTurns,
            Charges = charges,
        });
    }

    private static StatusEffectInstance? GetStatus(ICombatant combatant, string keyword) =>
        combatant.ActiveStatuses.FirstOrDefault(s => s.Keyword == keyword);

    // Extend Augment: an UntilConsumed status survives being consumed this many extra times
    // before it's actually removed, instead of vanishing the moment it's first used. Shared by
    // every UntilConsumed consumption point that Extend can target (currently Weak and Exposed --
    // see their own call sites) so charge bookkeeping lives in exactly one place.
    private static void ReduceStatusCharge(ICombatant combatant, StatusEffectInstance status)
    {
        status.Charges--;
        if (status.Charges <= 0) combatant.ActiveStatuses.Remove(status);
    }

    private static ICombatant? SelectTarget(TargetType targetType, List<ICombatant> team)
    {
        var alive = team.Where(c => c.CurrentHp > 0).ToList();
        return targetType switch
        {
            // Marked (which Taunt applies to self) forces the opposing team's next action onto
            // this combatant, taking priority over plain position order.
            TargetType.Enemy or TargetType.Ally =>
                alive.FirstOrDefault(c => c.ActiveStatuses.Any(s => s.Keyword == "Marked"))
                    ?? alive.OrderBy(c => c.Position).FirstOrDefault(),
            TargetType.LowestHpEnemy or TargetType.LowestHpAlly => alive.OrderBy(c => c.CurrentHp).FirstOrDefault(),
            _ => alive.FirstOrDefault(),
        };
    }

    private void RoundEndPhase()
    {
        _state.RoundNumber++;
        // Win/loss condition is checked by the caller after RunRound() returns.
    }

    private void Log(string message) => OnLog?.Invoke(message);

    // Low-level draw primitive -- TopUpHand is the only caller under the current model, computing
    // how many to draw; this just draws that many, one at a time. Already reshuffles the discard
    // pile back into the draw pile the moment the draw pile runs dry mid-draw (rather than ending
    // the draw early), so a persistent hand can keep drawing indefinitely across a long combat
    // without ever truly running out of cards.
    private void DrawCards(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (_state.Hand.Count >= HandCap) break; // cap reached — remaining draws are skipped/lost

            if (_state.DrawPile.Count == 0)
            {
                if (_state.DiscardPile.Count == 0) break; // nothing left anywhere to draw

                _state.DrawPile.AddRange(_state.DiscardPile);
                _state.DiscardPile.Clear();
                Shuffle(_state.DrawPile);
                Log("  Discard pile shuffled back into the draw pile.");
            }

            var card = _state.DrawPile[0];
            _state.DrawPile.RemoveAt(0);
            _state.Hand.Add(card);
        }
    }

    private void TickStatusDurations(ICombatant combatant)
    {
        // Until-Consumed statuses (Shield, Primed, Weak) never decrement here — they're removed
        // only by ConsumeWeak / shield absorption / etc. at the point they're actually used.
        foreach (var status in combatant.ActiveStatuses.ToList())
        {
            if (status.Duration != DurationType.FixedTurn) continue;

            // Bleed is a DOT — applies its Magnitude directly to HP, bypassing Defense and
            // Shield entirely, rather than going through the normal damage pipeline.
            if (status.Keyword == "Bleed")
            {
                combatant.CurrentHp = Math.Max(0, combatant.CurrentHp - status.Magnitude);
                Log($"  {combatant.DisplayName} bleeds for {status.Magnitude} ({combatant.DisplayName} HP: {combatant.CurrentHp}).");
                PurgeDeadAnimaCards(combatant);
            }

            // Renew (Verdant Sustain kit): the HOT counterpart to Bleed -- ticks flat healing
            // (not scaled by any Spirit multiplier, same as Bleed's damage isn't scaled by any
            // Damage multiplier), capped at MaxHp.
            if (status.Keyword == "Renew")
            {
                var before = combatant.CurrentHp;
                combatant.CurrentHp = Math.Min(combatant.MaxHp, combatant.CurrentHp + status.Magnitude);
                Log($"  {combatant.DisplayName} regenerates {combatant.CurrentHp - before} HP from Renew ({combatant.DisplayName} HP: {combatant.CurrentHp}).");
            }

            status.RemainingTurns--;
            if (status.RemainingTurns <= 0)
            {
                combatant.ActiveStatuses.Remove(status);
                Log($"  {combatant.DisplayName}'s {status.Keyword} expires.");
            }
        }
    }
}
