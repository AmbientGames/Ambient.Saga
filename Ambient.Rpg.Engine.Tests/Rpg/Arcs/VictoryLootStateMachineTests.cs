using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Tests.Rpg.Arcs;

/// <summary>
/// Victory-loot folds in the state machine:
/// - CharacterSpawned seeds the live CurrentInventory from the template's
///   Interactable.Loot ("the stuff they give you"), NOT from Capabilities (combat gear).
/// - CharacterDefeated records the victor (VictorAvatarId) on the spawn instance.
/// - Stock is spawn-instance-scoped: a respawned character (new CharacterInstanceId)
///   comes back with fresh template Loot while the previous life's takes stay taken.
/// </summary>
public class VictoryLootStateMachineTests
{
    private static World CreateWorldWithBandit()
    {
        var bandit = new Character
        {
            RefName = "Bandit",
            DisplayName = "Highway Bandit",
            // Combat gear — must NOT become takeable stock
            Capabilities = new ItemCollection
            {
                Equipment = new[]
                {
                    new EquipmentEntry { EquipmentRef = "IronSword", Condition = 0.8f }
                }
            },
            // Victory drop / giveable stock
            Interactable = new Interactable
            {
                Loot = new ItemCollection
                {
                    Consumables = new[]
                    {
                        new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 2 }
                    }
                }
            }
        };

        var arc = new Arc
        {
            RefName = "BanditArc",
            DisplayName = "Bandit Ambush",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var world = new World();
        world.ArcLookup[arc.RefName] = arc;
        world.CharactersLookup[bandit.RefName] = bandit;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger>();
        world.ConsumablesLookup["HealthPotion"] = new Consumable
        {
            RefName = "HealthPotion",
            DisplayName = "Health Potion",
            BaseValue = 20
        };
        world.EquipmentLookup["IronSword"] = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            BaseValue = 100
        };
        return world;
    }

    private static ArcStateMachine CreateStateMachine(World world)
        => new(world.ArcLookup["BanditArc"], world.ArcTriggersLookup["BanditArc"], world);

    private static ArcTransaction CreateTransaction(ArcTransactionType type, long seq, string avatarId, Dictionary<string, string> data)
        => new()
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            AvatarId = avatarId,
            Status = TransactionStatus.Committed,
            LocalTimestamp = DateTime.UtcNow,
            SequenceNumber = seq,
            Data = data
        };

    private static ArcTransaction Spawn(long seq, Guid instanceId, string avatarId = "system")
        => CreateTransaction(ArcTransactionType.CharacterSpawned, seq, avatarId, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "Bandit",
            [TransactionDataKeys.CharacterInstanceId] = instanceId.ToString()
        });

    private static ArcTransaction Defeat(long seq, Guid instanceId, string victorAvatarId)
        => CreateTransaction(ArcTransactionType.CharacterDefeated, seq, victorAvatarId, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterInstanceId] = instanceId.ToString(),
            [TransactionDataKeys.CharacterRef] = "Bandit",
            [TransactionDataKeys.VictorAvatarId] = victorAvatarId
        });

    private static ArcTransaction Take(long seq, Guid instanceId, string avatarId, int quantity = 1)
        => CreateTransaction(ArcTransactionType.ItemTraded, seq, avatarId, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterInstanceId] = instanceId.ToString(),
            [TransactionDataKeys.ItemRef] = "HealthPotion",
            [TransactionDataKeys.Quantity] = quantity.ToString(),
            [TransactionDataKeys.IsBuying] = "True",
            [TransactionDataKeys.PricePerItem] = "0",
            [TransactionDataKeys.TotalPrice] = "0"
        });

    [Fact]
    public void Spawn_SeedsCurrentInventoryFromInteractableLoot_NotCapabilities()
    {
        var world = CreateWorldWithBandit();
        var instanceId = Guid.NewGuid();

        var state = CreateStateMachine(world).Replay(new List<ArcTransaction> { Spawn(1, instanceId) });

        var inventory = state.Characters[instanceId.ToString()].CurrentInventory;
        Assert.NotNull(inventory);
        // Loot consumable present…
        Assert.Equal(2, inventory!.Consumables!.Single(c => c.ConsumableRef == "HealthPotion").Quantity);
        // …combat gear absent: Capabilities is what the character USES, not what it gives
        Assert.True(inventory.Equipment == null || inventory.Equipment.Length == 0);
    }

    [Fact]
    public void CharacterDefeated_RecordsVictorOnSpawnInstance()
    {
        var world = CreateWorldWithBandit();
        var instanceId = Guid.NewGuid();
        var victor = Guid.NewGuid().ToString();

        var state = CreateStateMachine(world).Replay(new List<ArcTransaction>
        {
            Spawn(1, instanceId),
            Defeat(2, instanceId, victor)
        });

        var character = state.Characters[instanceId.ToString()];
        Assert.False(character.IsAlive);
        Assert.Equal(victor, character.DefeatedByAvatarId);
    }

    [Fact]
    public void Respawn_NewInstanceGetsFreshLoot_WhilePreviousTakesStayTaken()
    {
        var world = CreateWorldWithBandit();
        var firstLife = Guid.NewGuid();
        var secondLife = Guid.NewGuid();
        var victor = Guid.NewGuid().ToString();

        var state = CreateStateMachine(world).Replay(new List<ArcTransaction>
        {
            Spawn(1, firstLife),
            Defeat(2, firstLife, victor),
            Take(3, firstLife, victor, quantity: 2),   // victor empties the drop
            Spawn(4, secondLife)                        // respawn = new instance id
        });

        // The looted life stays looted (spawn-instance-scoped stock)
        var firstInventory = state.Characters[firstLife.ToString()].CurrentInventory;
        Assert.True(firstInventory?.Consumables == null ||
                    firstInventory.Consumables.All(c => c.ConsumableRef != "HealthPotion"));

        // The respawned instance comes back with fresh template Loot and no victor
        var second = state.Characters[secondLife.ToString()];
        Assert.True(second.IsAlive);
        Assert.Null(second.DefeatedByAvatarId);
        Assert.Equal(2, second.CurrentInventory!.Consumables!.Single(c => c.ConsumableRef == "HealthPotion").Quantity);
    }
}
