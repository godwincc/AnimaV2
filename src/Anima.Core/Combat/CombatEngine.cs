namespace Anima.Core.Combat;

using Anima.Core.Enums;
using Anima.Core.Models;

public class CombatEngine
{
    private readonly CombatState _state;
    private readonly Random _random = new();

    private const int HandCap = 10;
    private const int CopiesPerSkill = 3;
    private const double CrestConditionalMultiplier = 1.25;
    private const double CourageDamageReduction = 0.8;
    private const double AmbushMultiplier = 2.0;

    // This Round's Initiative order, captured once per Round so Ambush can check whether the
    // acting combatant is literally the last entry in it (see GetOffensiveCrestMultiplier).
    private List<ICombatant> _currentInitiativeOrder = new();

    // Narration hook — kept out of Core so callers (console, UI, tests) decide how/whether to surface it.
    public Action<string>? OnLog { get; set; }

    // Placeholder for real player input / AI; the harness supplies a hardcoded priority for now.
    public Func<Anima, CombatState, Skill?>? ChoosePlayerSkill { get; set; }

    public CombatEngine(CombatState state)
    {
        _state = state;
    }

    // Builds the shared 27-card team deck (Head/Frame/Tail x3 copies each, Crests excluded),
    // shuffles it, and draws the opening hand of 7. Call once before the first RunRound().
    public void StartCombat()
    {
        BuildDeck();
        Shuffle(_state.DrawPile);
        DrawCards(7);
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

    private void RoundStartPhase()
    {
        // Energy refill (+3, capped)
        _state.SharedEnergy = Math.Min(_state.SharedEnergy + 3, 9);
        Log($"Energy: {_state.SharedEnergy}");

        // Draw cards (opening hand 7 handled separately at combat start; +3/round here)
        DrawCards(3);

        // Tick down all active Durations across all combatants
        foreach (var combatant in AllCombatants())
        {
            TickStatusDurations(combatant);
        }

        // Elite/Boss Enrage — universal safety net against stalemates.
        foreach (var enemy in _state.EnemyTeam.Where(e => e.CurrentHp > 0))
        {
            if (enemy.EnrageRound.HasValue && !enemy.IsEnraged && _state.RoundNumber >= enemy.EnrageRound.Value)
            {
                enemy.IsEnraged = true;
                Log($"{enemy.Name} becomes ENRAGED! Damage output increased.");
            }
        }
    }

    private IEnumerable<ICombatant> AllCombatants() =>
        _state.PlayerTeam.Cast<ICombatant>().Concat(_state.EnemyTeam.Cast<ICombatant>());

    private List<ICombatant> InitiativePhase()
    {
        // TODO: handle Providence-style "always acts first" override
        //
        // PvE tie-break (no PvP yet, so no need for a fairness-preserving roll): on equal
        // Speed, the player's team always goes first; within a tied team, lower position
        // (1 before 2 before 3) goes first. Fully deterministic — no randomness.
        return AllCombatants()
            .Where(c => c.CurrentHp > 0)
            .OrderByDescending(c => c.Speed)
            .ThenBy(c => c is Enemy) // false (player) sorts before true (enemy)
            .ThenBy(c => c.Position)
            .ToList();
    }

    private void LogInitiativeOrder(List<ICombatant> initiativeOrder)
    {
        var order = string.Join(" > ", initiativeOrder.Select(c => $"{c.DisplayName}(Spd {c.Speed})"));
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

    private void ResolvePlayerTurn(Anima anima)
    {
        var skill = ChoosePlayerSkill?.Invoke(anima, _state);
        if (skill == null)
        {
            Log($"{anima.DisplayName} passes.");
            return;
        }

        Log($"{anima.DisplayName} uses {skill.Name} ({skill.EnergyCost} energy).");
        _state.SharedEnergy -= skill.EnergyCost;
        ResolveSkill(
            anima,
            skill,
            _state.EnemyTeam.Cast<ICombatant>().ToList(),
            _state.PlayerTeam.Cast<ICombatant>().ToList(),
            anima.BaseStats.DamageMultiplier,
            anima.BaseStats.SpiritMultiplier);

        _state.Hand.Remove(skill);
        _state.DiscardPile.Add(skill);
    }

    private void ResolveEnemyTurn(Enemy enemy)
    {
        var rule = enemy.BehaviorRules.FirstOrDefault(r => (!enemy.IsEnraged || !r.IsDefensive) && r.Condition(enemy, _state));
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
        double spiritMultiplier)
    {
        // Weak is Until-Consumed (same pattern as Shield/Primed) — it persists on its target
        // until that target's next skill use of any kind, regardless of Round/turn-order timing.
        var weakMagnitude = ConsumeWeak(actor);

        switch (skill.Category)
        {
            case SkillCategory.Attack:
                ResolveAttack(actor, skill, opposingTeam, friendlyTeam, damageMultiplier, spiritMultiplier, weakMagnitude);
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

        actor.ActiveStatuses.Remove(weak);
        Log($"  {actor.DisplayName}'s Weak is consumed.");
        return weak.Magnitude;
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
        int weakMagnitude)
    {
        var target = SelectTarget(skill.Target, opposingTeam);
        if (target == null)
        {
            Log("  No valid target.");
            return;
        }

        if (skill.Target is TargetType.Enemy or TargetType.Ally)
        {
            ConsumeMarked(target);
        }

        var enrageMultiplier = actor is Enemy { IsEnraged: true } enragedEnemy ? enragedEnemy.EnrageDamageMultiplier : 1.0;
        var raw = skill.BaseDamage * damageMultiplier * enrageMultiplier;

        // Self modifiers (Reckless/Vengeance, and eventually Primed) apply first; opponent-applied
        // debuffs (Weak) apply last — same multiplier chain, self before opponent.
        raw *= GetOffensiveCrestMultiplier(actor);

        if (weakMagnitude > 0)
        {
            raw *= 1 - weakMagnitude / 100.0;
            Log($"  {actor.DisplayName} is Weak: damage reduced by {weakMagnitude}%.");
        }

        var rawInt = (int)Math.Round(raw);
        var multLabel = enrageMultiplier > 1.0 ? $"{damageMultiplier:0.##} mult x {enrageMultiplier:0.##} enrage" : $"{damageMultiplier:0.##} mult";
        var final = ApplyDamage(target, rawInt, $"{skill.BaseDamage} base x {multLabel}");

        TriggerReactiveEffects(actor, target, skill);

        if (skill.OnHitStatusKeyword != null)
        {
            ApplyOnHitStatus(target, skill.OnHitStatusKeyword, skill.OnHitStatusMagnitude, skill.OnHitStatusDuration, skill.OnHitStatusDurationTurns ?? 0);
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
    }

    // Shared damage pipeline (Shield absorb, then Defense floor) — used by both direct attacks
    // and reactive counter-damage (Retaliate/Thorns), so counters respect the attacker's own
    // Shield/Defense rather than being a bypassing special case.
    private int ApplyDamage(ICombatant target, int rawDamage, string sourceDescription)
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
            }
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

        var final = remaining > 0 ? Math.Max(remaining - target.Defense, 1) : 0;
        target.CurrentHp = Math.Max(0, target.CurrentHp - final);

        Log($"  Target: {target.DisplayName}");
        var afterShield = absorbed > 0 ? $"{rawDamage} raw - {absorbed} shield = {preCourage}" : $"{rawDamage} raw";
        var afterCourage = courageMultiplier < 1.0 ? $" x{courageMultiplier:0.##} Courage = {remaining}" : "";
        Log($"  Damage: {sourceDescription} = {afterShield}{afterCourage} - {target.Defense} def = {final} dealt");
        Log($"  {target.DisplayName} HP: {target.CurrentHp}");

        PurgeDeadAnimaCards(target);

        return final;
    }

    // A dead Anima never takes another turn, so its cards would otherwise sit in Hand/DrawPile/
    // DiscardPile forever — never played, never discarded, permanently eating into the shared
    // hand cap. Removing them on death is a real gameplay rule, not just a test-harness fix.
    private void PurgeDeadAnimaCards(ICombatant combatant)
    {
        if (combatant is not Anima anima || anima.CurrentHp > 0) return;

        var deckSkills = anima.DeckSkills;
        var removed = _state.Hand.RemoveAll(s => deckSkills.Contains(s))
            + _state.DrawPile.RemoveAll(s => deckSkills.Contains(s))
            + _state.DiscardPile.RemoveAll(s => deckSkills.Contains(s));

        if (removed > 0)
        {
            Log($"  {anima.DisplayName} has fallen — their remaining cards are removed from the deck.");
        }
    }

    // Retaliate/Thorns: flat counter-damage back at a Melee attacker, through the same
    // Shield/Defense pipeline as any other hit. Doesn't itself re-trigger reactive effects
    // (no counter-to-a-counter) and only fires if the defender survived the original hit.
    private void TriggerReactiveEffects(ICombatant attacker, ICombatant target, Skill skill)
    {
        if (skill.Range != AttackRange.Melee) return;
        if (target.CurrentHp <= 0) return;

        var retaliate = GetStatus(target, "Retaliate");
        if (retaliate != null)
        {
            Log($"  {target.DisplayName} Retaliates!");
            ApplyDamage(attacker, retaliate.Magnitude, "Retaliate counter");
        }

        var thorns = GetStatus(target, "Thorns");
        if (thorns != null)
        {
            Log($"  {target.DisplayName}'s Thorns triggers!");
            ApplyDamage(attacker, thorns.Magnitude, "Thorns counter");
        }
    }

    private void ResolveHeal(ICombatant actor, Skill skill, List<ICombatant> friendlyTeam, double spiritMultiplier, int weakMagnitude)
    {
        var target = skill.Target == TargetType.SelfTarget ? actor : SelectTarget(skill.Target, friendlyTeam);
        if (target == null)
        {
            Log("  No valid target.");
            return;
        }

        ApplyHeal(actor, target, skill.BaseHeal, spiritMultiplier, weakMagnitude);

        if (skill.RemovesDebuff)
        {
            RemoveOneDebuff(target);
        }
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

    private void ResolveBuff(ICombatant actor, Skill skill, List<ICombatant> friendlyTeam)
    {
        if (skill.BaseShield > 0)
        {
            var existingShield = GetStatus(actor, "Shield");
            if (existingShield != null)
            {
                existingShield.Magnitude = Math.Min(existingShield.Magnitude + skill.BaseShield, MaxShieldMagnitude);
                Log($"  {actor.DisplayName} gains {skill.BaseShield} Shield (now {existingShield.Magnitude}).");
            }
            else
            {
                ApplyStatus(actor, "Shield", Math.Min(skill.BaseShield, MaxShieldMagnitude), skill.Duration, skill.DurationTurns ?? 0);
                Log($"  {actor.DisplayName} gains {skill.BaseShield} Shield.");
            }
            return;
        }

        if (skill.Name == "Taunt")
        {
            // Taunt is "apply Marked to self" -- it shares Marked's underlying status/slot
            // rather than maintaining a separate implementation.
            ApplyMarked(actor, friendlyTeam);
            return;
        }

        // Simplification: a non-Shield Buff skill applies a status named after itself to its caster.
        // BuffMagnitude carries a value for statuses that need one (e.g. Retaliate/Thorns counter amount).
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
            case "Pin":
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
    // Enemy-only for now -- no player-side summon skill exists yet. Placed into the first open
    // position (1-3, front to back); if the side is already full, the summon is skipped rather
    // than exceeding the normal 3-per-side limit. Added directly to _state.EnemyTeam (not a
    // team-list copy) so next Round's InitiativePhase -- which re-queries _state.PlayerTeam/
    // EnemyTeam fresh via AllCombatants() -- picks the new combatant up automatically.
    private void ResolveSummon(ICombatant actor, Skill skill)
    {
        if (skill.SummonFactory == null || actor is not Enemy) return;

        var occupied = _state.EnemyTeam.Where(e => e.CurrentHp > 0).Select(e => e.Position).ToHashSet();
        var openPosition = Enumerable.Range(1, 3).FirstOrDefault(p => !occupied.Contains(p));
        if (openPosition == 0)
        {
            Log("  No open position for the summon -- skipped.");
            return;
        }

        var summon = skill.SummonFactory();
        summon.Position = openPosition;
        _state.EnemyTeam.Add(summon);
        Log($"  {summon.DisplayName} is summoned into position {openPosition}!");
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
    private void ApplyOnHitStatus(ICombatant target, string keyword, int magnitude, DurationType duration, int durationTurns)
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

        ApplyStatus(target, keyword, magnitude, duration, durationTurns);
        Log($"  {target.DisplayName} is afflicted with {keyword} ({magnitude}%).");
    }

    private static void ApplyStatus(ICombatant target, string keyword, int magnitude, DurationType duration, int durationTurns)
    {
        target.ActiveStatuses.Add(new StatusEffectInstance
        {
            Keyword = keyword,
            Magnitude = magnitude,
            Duration = duration,
            RemainingTurns = durationTurns,
        });
    }

    private static StatusEffectInstance? GetStatus(ICombatant combatant, string keyword) =>
        combatant.ActiveStatuses.FirstOrDefault(s => s.Keyword == keyword);

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

            status.RemainingTurns--;
            if (status.RemainingTurns <= 0)
            {
                combatant.ActiveStatuses.Remove(status);
                Log($"  {combatant.DisplayName}'s {status.Keyword} expires.");
            }
        }
    }
}
