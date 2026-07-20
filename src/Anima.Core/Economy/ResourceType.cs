namespace Anima.Core.Economy;

// Keyed resource types tracked by PersistentLedger (and, where relevant, RunLedger). Adding a
// future crafting material is just adding a value here -- no ledger code changes needed.
//
// Ember is deliberately NOT a value here. It used to be a per-color persistent balance, but it's
// now a momentary pickup-or-convert resource (see Anima.Core.Economy.EmberService /
// RewardService's AnimaColor-returning grant methods) that's never actually banked -- each drop is
// either spent on an Augment or converted to Wisp immediately, so there's no balance to track.
public enum ResourceType
{
    Wisp,
    EchoShard,
    VesselShard,

    // A complete Vessel (Boss-only guaranteed drop). No richer representation (e.g. a pre-rolled
    // genome payload) exists yet -- this is a plain count stub, per RewardService's scope note.
    Vessel,
}
