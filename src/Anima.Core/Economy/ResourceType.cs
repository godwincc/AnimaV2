namespace Anima.Core.Economy;

// Keyed resource types tracked by PersistentLedger (and, where relevant, RunLedger). Adding a
// future crafting material is just adding a value here -- no ledger code changes needed.
public enum ResourceType
{
    Wisp,
    EchoShard,
    VesselShard,

    // A complete Vessel (Boss-only guaranteed drop). No richer representation (e.g. a pre-rolled
    // genome payload) exists yet -- this is a plain count stub, per RewardService's scope note.
    Vessel,

    // Ember is genuinely per-color (drives a 25%-per-color drop roll, an unwanted-color-to-Wisp
    // conversion, and Reforge's higher-cost "choose color" option elsewhere in the design) --
    // Vulcan/Mirage never drop their own Ember since they're hybrid-only outcomes, never a directly
    // rollable base color.
    EmberCrimson,
    EmberOnyx,
    EmberVerdant,
    EmberAzure,
}
