namespace Anima.Core.Models;

// The player's full owned-Anima collection, shown on the Sanctum screen -- this is the structural
// gap AnimaMaterializationService.Create needed and didn't have anywhere else to write into: there
// was no roster/storage concept anywhere in the codebase before this (only ever ad-hoc
// List<Anima> locals, e.g. a Delve's 3-Anima team). Persistent across Delves, same as
// PersistentLedger -- no save/load exists yet (no database per CLAUDE.md), so callers hold one
// instance for the whole session, same caveat as PersistentLedger's own.
public sealed class SanctumRoster
{
    public List<Anima> Animas { get; } = new();

    public Anima? FindById(string id) => Animas.FirstOrDefault(a => a.Id == id);
}
