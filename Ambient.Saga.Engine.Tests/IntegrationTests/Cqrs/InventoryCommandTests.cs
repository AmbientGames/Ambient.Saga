using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Tests.Helpers;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for inventory management commands (equip/unequip, use consumables).
/// Tests outside-of-battle equipment changes and consumable usage.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class InventoryCommandTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public InventoryCommandTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithEquipmentAndConsumables();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<EquipItemOutsideBattleCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
        });

        services.AddSingleton(_world);
        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
    }

    private World CreateWorldWithEquipmentAndConsumables()
    {
        var sagaArc = new SagaArc
        {
            RefName = "PlayerLife",
            DisplayName = "Player Life",
            Latitude = 35.0,
            Longitude = 139.0
        };

        // Equipment
        var ironSword = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            SlotRef = "MainHand",
            Effects = new Attributes { Strength = 0.1f }
        };

        var steelShield = new Equipment
        {
            RefName = "SteelShield",
            DisplayName = "Steel Shield",
            SlotRef = "OffHand",
            Effects = new Attributes { Defense = 0.15f }
        };

        var greatAxe = new Equipment
        {
            RefName = "GreatAxe",
            DisplayName = "Great Axe",
            SlotRef = "BothHands",
            Effects = new Attributes { Strength = 0.25f }
        };

        var ironHelmet = new Equipment
        {
            RefName = "IronHelmet",
            DisplayName = "Iron Helmet",
            SlotRef = "Head",
            Effects = new Attributes { Defense = 0.05f }
        };

        // Consumables
        var healthPotion = new Consumable
        {
            RefName = "HealthPotion",
            DisplayName = "Health Potion",
            Effects = new Attributes { Health = 0.3f }
        };

        var staminaPotion = new Consumable
        {
            RefName = "StaminaPotion",
            DisplayName = "Stamina Potion",
            Effects = new Attributes { Stamina = 0.25f }
        };

        var manaPotion = new Consumable
        {
            RefName = "ManaPotion",
            DisplayName = "Mana Potion",
            Effects = new Attributes { Mana = 0.4f }
        };

        var fullRestorePotion = new Consumable
        {
            RefName = "FullRestorePotion",
            DisplayName = "Full Restore Potion",
            Effects = new Attributes { Health = 1.0f, Stamina = 1.0f, Mana = 1.0f }
        };

        // Loadout slots
        var loadoutSlots = new[]
        {
            new LoadoutSlot { RefName = "Head", DisplayName = "Head" },
            new LoadoutSlot { RefName = "MainHand", DisplayName = "Main Hand" },
            new LoadoutSlot { RefName = "OffHand", DisplayName = "Off Hand" },
            new LoadoutSlot { RefName = "BothHands", DisplayName = "Both Hands" }
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = new[] { sagaArc },
                    Equipment = new[] { ironSword, steelShield, greatAxe, ironHelmet },
                    Consumables = new[] { healthPotion, staminaPotion, manaPotion, fullRestorePotion },
                    LoadoutSlots = loadoutSlots
                }
            }
        };

        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger>();

        // Build equipment lookup
        foreach (var equip in world.Gameplay.Equipment)
        {
            world.EquipmentLookup[equip.RefName] = equip;
        }

        // Build consumables lookup
        foreach (var consumable in world.Gameplay.Consumables)
        {
            world.ConsumablesLookup[consumable.RefName] = consumable;
        }

        // Build loadout slots lookup
        foreach (var slot in loadoutSlots)
        {
            world.LoadoutSlotsLookup[slot.RefName] = slot;
        }

        return world;
    }

    private AvatarEntity CreateTestAvatar(Guid avatarId)
    {
        return new AvatarEntity
        {
            Id = avatarId,
            AvatarId = avatarId,
            ArchetypeRef = "Warrior",
            DisplayName = "Test Avatar",
            Stats = new CharacterStats
            {
                Health = 0.5f,
                Stamina = 0.6f,
                Mana = 0.4f,
                Credits = 1000
            },
            Capabilities = new ItemCollection
            {
                Equipment = new[]
                {
                    new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f },
                    new EquipmentEntry { EquipmentRef = "SteelShield", Condition = 1.0f },
                    new EquipmentEntry { EquipmentRef = "GreatAxe", Condition = 1.0f },
                    new EquipmentEntry { EquipmentRef = "IronHelmet", Condition = 1.0f }
                },
                Consumables = new[]
                {
                    new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 5 },
                    new ConsumableEntry { ConsumableRef = "StaminaPotion", Quantity = 3 },
                    new ConsumableEntry { ConsumableRef = "ManaPotion", Quantity = 2 },
                    new ConsumableEntry { ConsumableRef = "FullRestorePotion", Quantity = 1 }
                }
            },
            CombatProfile = new Dictionary<string, string>()
        };
    }

    #region EquipItemOutsideBattle Tests

    [Fact]
    public async Task EquipItem_ValidEquipment_CreatesEquipmentChangedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);

        var command = new EquipItemOutsideBattleCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            EquipmentRef = "IronSword",
            SlotRef = "MainHand"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.TransactionIds);

        // Verify EquipmentChanged transaction was created
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "PlayerLife");
        var equipTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == SagaTransactionType.EquipmentChanged);

        Assert.NotNull(equipTx);
        Assert.Equal("MainHand", equipTx.Data["SlotRef"]);
        Assert.Equal("IronSword", equipTx.Data["NewEquipmentRef"]);
        Assert.Equal("False", equipTx.Data["IsUnequipping"]);
    }

    [Fact]
    public async Task EquipItem_UpdatesCombatProfile()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);

        var command = new EquipItemOutsideBattleCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            EquipmentRef = "IronHelmet",
            SlotRef = "Head"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);
        Assert.NotNull(result.UpdatedAvatar);
        Assert.True(result.UpdatedAvatar.CombatProfile.ContainsKey("Head"));
        Assert.Equal("IronHelmet", result.UpdatedAvatar.CombatProfile["Head"]);
    }

    [Fact]
    public async Task UnequipItem_ValidSlot_CreatesEquipmentChangedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.CombatProfile["MainHand"] = "IronSword"; // Pre-equip

        var command = new EquipItemOutsideBattleCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            EquipmentRef = null, // Unequip
            SlotRef = "MainHand"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "PlayerLife");
        var equipTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == SagaTransactionType.EquipmentChanged);

        Assert.NotNull(equipTx);
        Assert.Equal("True", equipTx.Data["IsUnequipping"]);
        Assert.Equal("IronSword", equipTx.Data["PreviousEquipmentRef"]);
    }

    [Fact]
    public async Task EquipBothHands_ClearsMainHandAndOffHand()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.CombatProfile["MainHand"] = "IronSword";
        avatar.CombatProfile["OffHand"] = "SteelShield";

        var command = new EquipItemOutsideBattleCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            EquipmentRef = "GreatAxe",
            SlotRef = "BothHands"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);
        Assert.NotNull(result.UpdatedAvatar);

        // BothHands should be equipped
        Assert.True(result.UpdatedAvatar.CombatProfile.ContainsKey("BothHands"));
        Assert.Equal("GreatAxe", result.UpdatedAvatar.CombatProfile["BothHands"]);

        // MainHand and OffHand should be cleared
        Assert.False(result.UpdatedAvatar.CombatProfile.ContainsKey("MainHand"));
        Assert.False(result.UpdatedAvatar.CombatProfile.ContainsKey("OffHand"));
    }

    [Fact]
    public async Task EquipMainHand_ClearsBothHands()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.CombatProfile["BothHands"] = "GreatAxe";

        var command = new EquipItemOutsideBattleCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            EquipmentRef = "IronSword",
            SlotRef = "MainHand"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);
        Assert.NotNull(result.UpdatedAvatar);

        // MainHand should be equipped
        Assert.True(result.UpdatedAvatar.CombatProfile.ContainsKey("MainHand"));
        Assert.Equal("IronSword", result.UpdatedAvatar.CombatProfile["MainHand"]);

        // BothHands should be cleared
        Assert.False(result.UpdatedAvatar.CombatProfile.ContainsKey("BothHands"));
    }

    [Fact]
    public async Task EquipItem_InvalidSlot_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);

        var command = new EquipItemOutsideBattleCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            EquipmentRef = "IronSword",
            SlotRef = "InvalidSlot"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("Invalid equipment slot", result.ErrorMessage);
    }

    [Fact]
    public async Task EquipItem_WrongSlot_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);

        // Try to equip sword in Head slot
        var command = new EquipItemOutsideBattleCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            EquipmentRef = "IronSword", // SlotRef is MainHand
            SlotRef = "Head" // Wrong slot
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("cannot be equipped in slot", result.ErrorMessage);
    }

    [Fact]
    public async Task EquipItem_NotOwned_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.Capabilities.Equipment = Array.Empty<EquipmentEntry>(); // No equipment

        var command = new EquipItemOutsideBattleCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            EquipmentRef = "IronSword",
            SlotRef = "MainHand"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("does not own", result.ErrorMessage);
    }

    [Fact]
    public async Task EquipItem_AlreadyEquipped_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.CombatProfile["MainHand"] = "IronSword"; // Already equipped

        var command = new EquipItemOutsideBattleCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            EquipmentRef = "IronSword",
            SlotRef = "MainHand"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("already equipped", result.ErrorMessage);
    }

    #endregion

    #region UseConsumable Tests

    [Fact]
    public async Task UseConsumable_ValidConsumable_CreatesConsumableUsedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);

        var command = new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "HealthPotion"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.TransactionIds);

        // Verify ConsumableUsed transaction was created
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "PlayerLife");
        var useTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == SagaTransactionType.ConsumableUsed);

        Assert.NotNull(useTx);
        Assert.Equal("HealthPotion", useTx.Data["ConsumableRef"]);
        Assert.Equal("5", useTx.Data["QuantityBefore"]);
        Assert.Equal("4", useTx.Data["QuantityAfter"]);
    }

    [Fact]
    public async Task UseConsumable_RestoresHealth()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.Stats.Health = 0.5f; // 50% health

        var command = new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "HealthPotion" // Restores 0.3 health
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);
        Assert.NotNull(result.UpdatedAvatar);
        Assert.Equal(0.8f, result.UpdatedAvatar.Stats.Health, 2); // 0.5 + 0.3 = 0.8
    }

    [Fact]
    public async Task UseConsumable_HealthCappedAtOne()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.Stats.Health = 0.9f; // 90% health

        var command = new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "HealthPotion" // Would restore to 1.2, but capped
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);
        Assert.NotNull(result.UpdatedAvatar);
        Assert.Equal(1.0f, result.UpdatedAvatar.Stats.Health, 2); // Capped at 1.0
    }

    [Fact]
    public async Task UseConsumable_DecrementsQuantity()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var initialQuantity = avatar.Capabilities.Consumables!
            .First(c => c.ConsumableRef == "HealthPotion").Quantity;

        var command = new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "HealthPotion"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);
        Assert.NotNull(result.UpdatedAvatar);

        var remaining = result.UpdatedAvatar.Capabilities?.Consumables?
            .FirstOrDefault(c => c.ConsumableRef == "HealthPotion")?.Quantity ?? 0;
        Assert.Equal(initialQuantity - 1, remaining);
    }

    [Fact]
    public async Task UseConsumable_LastOne_RemovesFromInventory()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);

        // Set quantity to 1
        var entry = avatar.Capabilities.Consumables!.First(c => c.ConsumableRef == "FullRestorePotion");
        entry.Quantity = 1;

        var command = new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "FullRestorePotion"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);
        Assert.NotNull(result.UpdatedAvatar);

        // FullRestorePotion should be removed from inventory
        var remaining = result.UpdatedAvatar.Capabilities?.Consumables?
            .FirstOrDefault(c => c.ConsumableRef == "FullRestorePotion");
        Assert.Null(remaining);
    }

    [Fact]
    public async Task UseConsumable_RestoresMultipleStats()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.Stats.Health = 0.3f;
        avatar.Stats.Stamina = 0.2f;
        avatar.Stats.Mana = 0.1f;

        var command = new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "FullRestorePotion" // Restores all to 1.0
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);
        Assert.NotNull(result.UpdatedAvatar);
        Assert.Equal(1.0f, result.UpdatedAvatar.Stats.Health, 2);
        Assert.Equal(1.0f, result.UpdatedAvatar.Stats.Stamina, 2);
        Assert.Equal(1.0f, result.UpdatedAvatar.Stats.Mana, 2);
    }

    [Fact]
    public async Task UseConsumable_NotOwned_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.Capabilities.Consumables = Array.Empty<ConsumableEntry>(); // No consumables

        var command = new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "HealthPotion"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("does not have", result.ErrorMessage);
    }

    [Fact]
    public async Task UseConsumable_ZeroQuantity_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);

        // Set quantity to 0
        var entry = avatar.Capabilities.Consumables!.First(c => c.ConsumableRef == "HealthPotion");
        entry.Quantity = 0;

        var command = new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "HealthPotion"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("does not have", result.ErrorMessage);
    }

    [Fact]
    public async Task UseConsumable_NonExistentConsumable_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);

        // Add a consumable entry that doesn't exist in world
        avatar.Capabilities.Consumables = new[]
        {
            new ConsumableEntry { ConsumableRef = "NonExistentPotion", Quantity = 5 }
        };

        var command = new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "NonExistentPotion"
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task UseConsumable_MultipleUses_AllTracked()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        avatar.Stats.Health = 0.2f;

        // Act - Use health potion twice
        var result1 = await _mediator.Send(new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "HealthPotion"
        });

        // Update avatar for second use
        if (result1.UpdatedAvatar != null)
        {
            avatar = result1.UpdatedAvatar;
        }

        var result2 = await _mediator.Send(new UseConsumableCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "PlayerLife",
            ConsumableRef = "HealthPotion"
        });

        // Assert
        Assert.True(result1.Successful);
        Assert.True(result2.Successful);

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "PlayerLife");
        var useTransactions = instance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.ConsumableUsed)
            .ToList();

        Assert.Equal(2, useTransactions.Count);

        // Verify sequence numbers are properly ordered
        Assert.True(useTransactions[0].SequenceNumber < useTransactions[1].SequenceNumber);
    }

    #endregion

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
