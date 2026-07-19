namespace Anima.Core.Economy;

// Keyed resource types tracked by PersistentLedger (and, where relevant, RunLedger). Adding a
// future crafting material is just adding a value here -- no ledger code changes needed.
public enum ResourceType
{
    Wisp,
    EchoShard,
    VesselShard,
}
