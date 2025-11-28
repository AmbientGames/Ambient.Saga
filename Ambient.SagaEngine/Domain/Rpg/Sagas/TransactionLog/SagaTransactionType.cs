namespace Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

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

    // Player interactions
    PlayerEntered,
    PlayerExited,

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

    // Loot and rewards
    LootAwarded,
    EffectApplied,
    QuestTokenAwarded,

    // Quest system (multi-stage, multi-objective)
    QuestAccepted,           // Quest accepted from signpost/NPC
    QuestObjectiveCompleted, // Individual objective within a stage completed
    QuestStageAdvanced,      // Advanced to next stage (all objectives complete)
    QuestBranchChosen,       // Player chose a branch in exclusive choice stage
    QuestCompleted,          // Quest finished successfully (all stages complete)
    QuestFailed,             // Quest failed (fail condition triggered or wrong choice)
    QuestAbandoned,          // Quest dropped by player
    QuestProgressed,         // DEPRECATED: Use QuestObjectiveCompleted instead

    // Structure interactions
    StructureDamaged,
    StructureRepaired,

    // Landmark interactions
    LandmarkDiscovered,

    // Voxel mining and building (claims-based for anti-cheat)
    LocationClaimed,         // Player position update with movement validation
    ToolWearClaimed,         // Tool condition delta with wear rate validation
    MiningSessionClaimed,    // Batch of blocks mined with plausibility checks
    BuildingSessionClaimed,  // Batch of blocks placed with material validation
    InventorySnapshot,       // Periodic full inventory state for validation baseline

    // Administrative
    StateSnapshot,      // Periodic snapshot for performance
    TransactionReversed // Compensating transaction for rollback
}
