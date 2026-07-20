namespace Anima.Core.Economy;

// Account-level currency/fragment balances: Wisp, Echo/Vessel Shards, complete Vessels, and any
// future crafting material -- just add a ResourceType value and it's tracked for free, no ledger
// code changes needed. Survives between Delves (unlike RunLedger). No save/load exists yet (no
// database per CLAUDE.md) -- callers hold one instance across Delves for now.
//
// Ember is NOT tracked here -- it's a momentary drop, never a stored balance. See
// ResourceType's own comment and Anima.Core.Economy.EmberService.
public sealed class PersistentLedger
{
    private readonly Dictionary<ResourceType, int> _balances = new();

    public int GetBalance(ResourceType type) => _balances.GetValueOrDefault(type, 0);

    public bool CanAfford(ResourceType type, int amount) => GetBalance(type) >= amount;

    public void Add(ResourceType type, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Cannot add a negative amount.");
        _balances[type] = GetBalance(type) + amount;
    }

    // Returns false and leaves the balance untouched if the player can't afford it -- callers are
    // expected to check CanAfford (or handle a false return) before treating a spend as committed.
    public bool TrySpend(ResourceType type, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Cannot spend a negative amount.");
        if (!CanAfford(type, amount)) return false;
        _balances[type] -= amount;
        return true;
    }
}
