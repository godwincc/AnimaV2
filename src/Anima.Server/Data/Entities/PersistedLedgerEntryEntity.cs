namespace Anima.Server.Data.Entities;

// One row per (account, ResourceType) balance -- PersistentLedger's real storage. Mirrors
// PersistentLedger's own Dictionary<ResourceType,int> shape directly; a missing row for a given
// ResourceType means balance 0, exactly like the dictionary's GetValueOrDefault today.
public class PersistedLedgerEntryEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string ResourceType { get; set; } // Anima.Core.Economy.ResourceType, stored as string
    public int Amount { get; set; }
    public int Version { get; set; }
}
