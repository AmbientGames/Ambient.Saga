namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Types of transactions that can occur in a Saga instance.
/// Each transaction represents a discrete state change that can be replayed.
/// </summary>
public enum SagaTransactionType
{
    // Saga lifecycle
    SagaDiscovered,
    SagaCompleted,

    // Trigger lifecycle
    TriggerActivated,
    TriggerCompleted,

    // Character lifecycle
    CharacterSpawned,
    CharacterDamaged,
    CharacterHealed,
    CharacterDefeated,
    CharacterDespawned,

    // Battle system
    BattleStarted,          // Battle initiated with equipment/affinity snapshot
    BattleTurnExecuted,     // A single turn (attack, spell, item, equipment change, etc.)
    BattleEnded,            // Battle concluded with victor
    StatusEffectApplied,    // Status effect applied (poison, stun, bleed, burn, etc.)
    StatusEffectRemoved,    // Status effect expired or cleansed
    CriticalHitDealt,       // Critical hit dealt in combat
    ComboExecuted,          // Combo chain attack executed

    // Avatar interactions
    AvatarEntered,
    AvatarExited,

    // Entity interactions
    EntityInteracted,
    DialogueStarted,
    DialogueNodeVisited,    // Tracks individual node visits for social achievement tracking
    DialogueCompleted,
    TraitAssigned,           // Tracks character trait changes (mood, relationship building)
    TraitRemoved,            // Tracks trait removal
    ReputationChanged,       // Faction reputation increased/decreased

    // Trading and economy
    ItemTraded,              // Tracks merchant trade transactions
    CurrencyChanged,         // Currency gained or lost

    // Party management
    PartyMemberJoined,       // Companion joined the party
    PartyMemberLeft,         // Companion left the party

    // Affinity management
    AffinityGranted,         // Character affinity granted to avatar

    // Equipment management (outside battle)
    EquipmentChanged,        // Equipment equipped/unequipped outside of battle

    // Consumable usage (outside battle)
    ConsumableUsed,          // Consumable item used (potion, food, etc.)

    // Tool maintenance
    ToolSharpened,           // Tool condition restored (costs currency)

    // Travel
    AvatarTeleported,        // Avatar teleported to new location (costs currency)

    // Loot and rewards
    LootAwarded,             // RETIRED: corpse looting removed 2026-07-04 (superseded by the
                             // MerchantTrade family). No producer remains; the member stays so
                             // enum value numbering is preserved and historical transactions
                             // still parse (stateless in the replay fold).
    EffectApplied,
    QuestTokenAwarded,

    // Quest system (multi-stage, multi-objective)
    QuestAccepted,           // Quest accepted from an NPC dialogue offer
    QuestObjectiveCompleted, // Individual objective within a stage completed
    QuestStageAdvanced,      // Advanced to next stage (all objectives complete)
    QuestBranchChosen,       // Avatar chose a branch in exclusive choice stage
    QuestCompleted,          // Quest finished successfully (all stages complete)
    QuestFailed,             // Quest failed (fail condition triggered or wrong choice)
    QuestAbandoned,          // Quest dropped by avatar

    // Structure interactions
    StructureDamaged,
    StructureRepaired,

    // Extension point for domain-specific transaction types (e.g., Ambient.Core voxel claims)
    // When Type = Extension, check ExtensionTypeName for the actual type identifier
    Extension,

    // Administrative
    StateSnapshot,      // Periodic snapshot for performance
    TransactionReversed // Compensating transaction for rollback
}
