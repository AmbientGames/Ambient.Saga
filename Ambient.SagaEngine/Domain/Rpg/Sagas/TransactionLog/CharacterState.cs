using Ambient.Domain;

namespace Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Runtime state of a character spawned in a Saga instance.
/// Derived by replaying transactions.
/// </summary>
public class CharacterState
{
    /// <summary>
    /// Unique identifier for this character instance.
    /// Not the same as CharacterRef - one CharacterRef can spawn multiple instances.
    /// </summary>
    public Guid CharacterInstanceId { get; set; }

    /// <summary>
    /// Reference to the character template in the catalog.
    /// </summary>
    public string CharacterRef { get; set; } = string.Empty;

    /// <summary>
    /// Which trigger spawned this character.
    /// </summary>
    public string SpawnedByTriggerRef { get; set; } = string.Empty;

    /// <summary>
    /// Whether this character is currently alive.
    /// </summary>
    public bool IsAlive { get; set; } = true;

    /// <summary>
    /// Whether this character is currently spawned in the world.
    /// </summary>
    public bool IsSpawned { get; set; } = true;

    /// <summary>
    /// Current health (0.0 to 1.0 multiplier of base health).
    /// </summary>
    public double CurrentHealth { get; set; } = 1.0;

    /// <summary>
    /// When this character was spawned.
    /// </summary>
    public DateTime SpawnedAt { get; set; }

    /// <summary>
    /// When this character was defeated (null if still alive).
    /// </summary>
    public DateTime? DefeatedAt { get; set; }

    /// <summary>
    /// When this character was despawned (null if still spawned).
    /// </summary>
    public DateTime? DespawnedAt { get; set; }

    /// <summary>
    /// Total damage dealt to this character by each player.
    /// Key: AvatarId, Value: Total damage
    /// </summary>
    public Dictionary<string, double> DamageByPlayer { get; set; } = new();

    /// <summary>
    /// Whether this character has been looted.
    /// </summary>
    public bool HasBeenLooted { get; set; }

    /// <summary>
    /// When this character was looted (null if not yet looted).
    /// </summary>
    public DateTime? LootedAt { get; set; }

    /// <summary>
    /// Current stats for this character instance (copied from template on spawn).
    /// </summary>
    public CharacterStats? CurrentStats { get; set; }

    /// <summary>
    /// Current inventory items for this character (copied from template on spawn).
    /// </summary>
    public ItemCollection? CurrentInventory { get; set; }

    /// <summary>
    /// Currently equipped items: SlotRef -> EquipmentRef mapping.
    /// Copied from template on spawn.
    /// Example: { "Head": "BasicHelm", "Chest": "TravelVest" }
    /// </summary>
    public Dictionary<string, string>? CombatProfile { get; set; }

    /// <summary>
    /// Current position of this character in the world (latitude).
    /// </summary>
    public double CurrentLatitudeZ { get; set; }

    /// <summary>
    /// Current position of this character in the world (longitude).
    /// </summary>
    public double CurrentLongitudeX { get; set; }

    /// <summary>
    /// Current altitude/height of this character.
    /// </summary>
    public double CurrentY { get; set; }

    /// <summary>
    /// Character traits assigned through dialogue or game events.
    /// Key: Trait name (e.g., "Hostile", "Friendly", "BossFight")
    /// Value: Trait value (null for boolean flags, int for numeric traits like "Aggression")
    /// </summary>
    public Dictionary<string, int?> Traits { get; set; } = new();
}
