using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;

namespace Ambient.Saga.Engine.Tests.Rpg.Dialogue;

/// <summary>
/// Tests for DirectDialogueStateProvider using the new provider-based API.
/// NOTE: Tools and BuildingMaterials are now provided by IGameplayItemProvider in Core,
/// not by Saga. Tests for those types have been removed.
/// </summary>
public class DirectDialogueStateProviderTests
{
    private readonly IWorld _world;
    private readonly AvatarBase _avatar;
    private readonly DirectDialogueStateProvider _provider;

    public DirectDialogueStateProviderTests()
    {
        _world = CreateMinimalWorld();
        _avatar = new AvatarBase
        {
            Capabilities = new ItemCollection
            {
                QuestTokens = Array.Empty<QuestTokenEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Equipment = Array.Empty<EquipmentEntry>(),
                Spells = Array.Empty<SpellEntry>()
            },
            Achievements = Array.Empty<AchievementEntry>(),
            Stats = new CharacterStats { Credits = 0, Health = 1.0f, Stamina = 1.0f, Mana = 1.0f }
        };
        _provider = new DirectDialogueStateProvider(_world, _avatar);
    }

    private static World CreateMinimalWorld()
    {
        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    Characters = Array.Empty<Character>(),
                    DialogueTrees = Array.Empty<DialogueTree>(),
                    Equipment = Array.Empty<Equipment>(),
                    Consumables = Array.Empty<Consumable>(),
                    QuestTokens = Array.Empty<QuestToken>(),
                    Factions = Array.Empty<Faction>(),
                    SagaArcs = Array.Empty<SagaArc>()
                }
            }
        };
        return world;
    }

    #region Quest Tokens (via Provider API)

    [Fact]
    public void HasItem_QuestTokens_InitiallyEmpty_ReturnsFalse()
    {
        Assert.False(_provider.HasItem("QuestTokens", "quest_001"));
    }

    [Fact]
    public void GiveItem_QuestTokens_AddsNewToken()
    {
        _provider.GiveItem("QuestTokens", "quest_001");

        Assert.True(_provider.HasItem("QuestTokens", "quest_001"));
        Assert.Single(_avatar.Capabilities.QuestTokens);
        Assert.Equal("quest_001", _avatar.Capabilities.QuestTokens[0].QuestTokenRef);
    }

    [Fact]
    public void GiveItem_QuestTokens_DoesNotAddDuplicates()
    {
        _provider.GiveItem("QuestTokens", "quest_001");
        _provider.GiveItem("QuestTokens", "quest_001");

        Assert.Single(_avatar.Capabilities.QuestTokens);
    }

    [Fact]
    public void TakeItem_QuestTokens_RemovesExistingToken()
    {
        _provider.GiveItem("QuestTokens", "quest_001");
        _provider.TakeItem("QuestTokens", "quest_001");

        Assert.False(_provider.HasItem("QuestTokens", "quest_001"));
        Assert.Empty(_avatar.Capabilities.QuestTokens);
    }

    [Fact]
    public void TakeItem_QuestTokens_NonExistent_DoesNothing()
    {
        _provider.TakeItem("QuestTokens", "quest_999");
        Assert.Empty(_avatar.Capabilities.QuestTokens);
    }

    [Fact]
    public void QuestTokens_MultipleTokens_WorksCorrectly()
    {
        _provider.GiveItem("QuestTokens", "quest_001");
        _provider.GiveItem("QuestTokens", "quest_002");
        _provider.GiveItem("QuestTokens", "quest_003");

        Assert.True(_provider.HasItem("QuestTokens", "quest_001"));
        Assert.True(_provider.HasItem("QuestTokens", "quest_002"));
        Assert.True(_provider.HasItem("QuestTokens", "quest_003"));
        Assert.Equal(3, _avatar.Capabilities.QuestTokens.Length);

        _provider.TakeItem("QuestTokens", "quest_002");

        Assert.True(_provider.HasItem("QuestTokens", "quest_001"));
        Assert.False(_provider.HasItem("QuestTokens", "quest_002"));
        Assert.True(_provider.HasItem("QuestTokens", "quest_003"));
        Assert.Equal(2, _avatar.Capabilities.QuestTokens.Length);
    }

    #endregion

    #region Consumables (Stackable via Provider API)

    [Fact]
    public void GetItemQuantity_Consumables_InitiallyEmpty_ReturnsZero()
    {
        Assert.Equal(0, _provider.GetItemQuantity("Consumables", "health_potion"));
    }

    [Fact]
    public void GiveItem_Consumables_CreatesNewEntry()
    {
        _provider.GiveItem("Consumables", "health_potion", 5);

        Assert.Equal(5, _provider.GetItemQuantity("Consumables", "health_potion"));
        Assert.Single(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void GiveItem_Consumables_ExistingItem_StacksQuantity()
    {
        _provider.GiveItem("Consumables", "health_potion", 5);
        _provider.GiveItem("Consumables", "health_potion", 3);

        Assert.Equal(8, _provider.GetItemQuantity("Consumables", "health_potion"));
        Assert.Single(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void GiveItem_Consumables_ZeroOrNegative_DoesNothing()
    {
        _provider.GiveItem("Consumables", "health_potion", 0);
        _provider.GiveItem("Consumables", "mana_potion", -5);

        Assert.Equal(0, _provider.GetItemQuantity("Consumables", "health_potion"));
        Assert.Equal(0, _provider.GetItemQuantity("Consumables", "mana_potion"));
        Assert.Empty(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void TakeItem_Consumables_ReducesQuantity()
    {
        _provider.GiveItem("Consumables", "health_potion", 10);
        _provider.TakeItem("Consumables", "health_potion", 3);

        Assert.Equal(7, _provider.GetItemQuantity("Consumables", "health_potion"));
        Assert.Single(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void TakeItem_Consumables_ReducesToZero_RemovesEntry()
    {
        _provider.GiveItem("Consumables", "health_potion", 5);
        _provider.TakeItem("Consumables", "health_potion", 5);

        Assert.Equal(0, _provider.GetItemQuantity("Consumables", "health_potion"));
        Assert.Empty(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void TakeItem_Consumables_MoreThanAvailable_ClampsToZeroAndRemoves()
    {
        _provider.GiveItem("Consumables", "health_potion", 3);
        _provider.TakeItem("Consumables", "health_potion", 10);

        Assert.Equal(0, _provider.GetItemQuantity("Consumables", "health_potion"));
        Assert.Empty(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void TakeItem_Consumables_NonExistent_DoesNothing()
    {
        _provider.TakeItem("Consumables", "health_potion", 5);
        Assert.Empty(_avatar.Capabilities.Consumables);
    }

    #endregion

    // NOTE: Materials tests removed - BuildingMaterials are now provided by IGameplayItemProvider in Core,
    // not by Saga. ItemCollection no longer has BuildingMaterials property.
    // DirectDialogueStateProvider no longer has AddMaterial/RemoveMaterial/GetMaterialQuantity methods.
    #region Materials (Stackable) - REMOVED
    // Tests removed: Materials are now in IGameplayItemProvider, not Saga.
    #endregion

    #region Equipment (Degradable via Provider API)

    [Fact]
    public void HasItem_Equipment_InitiallyEmpty_ReturnsFalse()
    {
        Assert.False(_provider.HasItem("Equipment", "iron_sword"));
    }

    [Fact]
    public void GiveItem_Equipment_AddsNewItem_WithFullCondition()
    {
        _provider.GiveItem("Equipment", "iron_sword");

        Assert.True(_provider.HasItem("Equipment", "iron_sword"));
        Assert.Single(_avatar.Capabilities.Equipment);
        Assert.Equal(1.0f, _avatar.Capabilities.Equipment[0].Condition);
    }

    [Fact]
    public void GiveItem_Equipment_DoesNotAddDuplicates()
    {
        _provider.GiveItem("Equipment", "iron_sword");
        _provider.GiveItem("Equipment", "iron_sword");

        Assert.Single(_avatar.Capabilities.Equipment);
    }

    [Fact]
    public void TakeItem_Equipment_RemovesExistingItem()
    {
        _provider.GiveItem("Equipment", "iron_sword");
        _provider.TakeItem("Equipment", "iron_sword");

        Assert.False(_provider.HasItem("Equipment", "iron_sword"));
        Assert.Empty(_avatar.Capabilities.Equipment);
    }

    [Fact]
    public void TakeItem_Equipment_NonExistent_DoesNothing()
    {
        _provider.TakeItem("Equipment", "iron_sword");
        Assert.Empty(_avatar.Capabilities.Equipment);
    }

    [Fact]
    public void Equipment_MultipleItems_WorksCorrectly()
    {
        _provider.GiveItem("Equipment", "iron_sword");
        _provider.GiveItem("Equipment", "steel_armor");
        _provider.GiveItem("Equipment", "leather_boots");

        Assert.True(_provider.HasItem("Equipment", "iron_sword"));
        Assert.True(_provider.HasItem("Equipment", "steel_armor"));
        Assert.True(_provider.HasItem("Equipment", "leather_boots"));
        Assert.Equal(3, _avatar.Capabilities.Equipment.Length);

        _provider.TakeItem("Equipment", "steel_armor");

        Assert.True(_provider.HasItem("Equipment", "iron_sword"));
        Assert.False(_provider.HasItem("Equipment", "steel_armor"));
        Assert.True(_provider.HasItem("Equipment", "leather_boots"));
        Assert.Equal(2, _avatar.Capabilities.Equipment.Length);
    }

    #endregion

    // NOTE: Tools tests removed - Tools are now provided by IGameplayItemProvider in Core,
    // not by Saga. ItemCollection no longer has Tools property.
    // DirectDialogueStateProvider no longer has AddTool/RemoveTool/HasTool methods.
    #region Tools (Degradable) - REMOVED
    // Tests removed: Tools are now in IGameplayItemProvider, not Saga.
    #endregion

    #region Spells (Degradable via Provider API)

    [Fact]
    public void HasItem_Spells_InitiallyEmpty_ReturnsFalse()
    {
        Assert.False(_provider.HasItem("Spells", "fireball"));
    }

    [Fact]
    public void GiveItem_Spells_AddsNewSpell_WithFullCondition()
    {
        _provider.GiveItem("Spells", "fireball");

        Assert.True(_provider.HasItem("Spells", "fireball"));
        Assert.Single(_avatar.Capabilities.Spells);
        Assert.Equal(1.0f, _avatar.Capabilities.Spells[0].Condition);
    }

    [Fact]
    public void GiveItem_Spells_DoesNotAddDuplicates()
    {
        _provider.GiveItem("Spells", "fireball");
        _provider.GiveItem("Spells", "fireball");

        Assert.Single(_avatar.Capabilities.Spells);
    }

    [Fact]
    public void TakeItem_Spells_RemovesExistingSpell()
    {
        _provider.GiveItem("Spells", "fireball");
        _provider.TakeItem("Spells", "fireball");

        Assert.False(_provider.HasItem("Spells", "fireball"));
        Assert.Empty(_avatar.Capabilities.Spells);
    }

    #endregion

    #region Achievements

    [Fact]
    public void HasAchievement_InitiallyEmpty_ReturnsFalse()
    {
        Assert.False(_provider.HasAchievement("first_kill"));
    }

    [Fact]
    public void UnlockAchievement_AddsNewAchievement()
    {
        _provider.UnlockAchievement("first_kill");

        Assert.True(_provider.HasAchievement("first_kill"));
        Assert.Single(_avatar.Achievements);
        Assert.Equal("first_kill", _avatar.Achievements[0].AchievementRef);
    }

    [Fact]
    public void UnlockAchievement_DoesNotAddDuplicates()
    {
        _provider.UnlockAchievement("first_kill");
        _provider.UnlockAchievement("first_kill");

        Assert.Single(_avatar.Achievements);
    }

    [Fact]
    public void Achievements_MultipleAchievements_WorksCorrectly()
    {
        _provider.UnlockAchievement("first_kill");
        _provider.UnlockAchievement("level_10");
        _provider.UnlockAchievement("legendary_weapon");

        Assert.True(_provider.HasAchievement("first_kill"));
        Assert.True(_provider.HasAchievement("level_10"));
        Assert.True(_provider.HasAchievement("legendary_weapon"));
        Assert.Equal(3, _avatar.Achievements.Length);
    }

    #endregion

    #region Currency & Health

    [Fact]
    public void GetCredits_ReturnsInitialValue()
    {
        Assert.Equal(0, _provider.GetCredits());
    }

    [Fact]
    public void TransferCurrency_Positive_IncreasesCredits()
    {
        _provider.TransferCurrency(100);

        Assert.Equal(100, _provider.GetCredits());
        Assert.Equal(100, _avatar.Stats.Credits);
    }

    [Fact]
    public void TransferCurrency_Negative_DecreasesCredits()
    {
        _avatar.Stats.Credits = 100;
        _provider.TransferCurrency(-30);

        Assert.Equal(70, _provider.GetCredits());
    }

    [Fact]
    public void TransferCurrency_Multiple_Accumulates()
    {
        _provider.TransferCurrency(50);
        _provider.TransferCurrency(30);
        _provider.TransferCurrency(-20);

        Assert.Equal(60, _provider.GetCredits());
    }

    [Fact]
    public void GetHealth_ReturnsInitialValue()
    {
        Assert.Equal(1, _provider.GetHealth());
    }

    [Fact]
    public void ModifyHealth_Positive_IncreasesHealth()
    {
        _avatar.Stats.Health = 50;
        _provider.ModifyHealth(20);

        Assert.Equal(70, _provider.GetHealth());
        Assert.Equal(70, _avatar.Stats.Health);
    }

    [Fact]
    public void ModifyHealth_Negative_DecreasesHealth()
    {
        _avatar.Stats.Health = 100;
        _provider.ModifyHealth(-30);

        Assert.Equal(70, _provider.GetHealth());
    }

    [Fact]
    public void ModifyHealth_BelowZero_ClampsToZero()
    {
        _avatar.Stats.Health = 20;
        _provider.ModifyHealth(-50);

        Assert.Equal(0, _provider.GetHealth());
    }

    #endregion

    #region Dialogue History

    [Fact]
    public void GetPlayerVisitCount_NotVisited_ReturnsZero()
    {
        Assert.Equal(0, _provider.GetPlayerVisitCount("merchant_dialogue"));
    }

    [Fact]
    public void RecordNodeVisit_FirstVisit_IncreasesVisitCount()
    {
        _provider.RecordNodeVisit("merchant_dialogue", "greeting");

        Assert.Equal(1, _provider.GetPlayerVisitCount("merchant_dialogue"));
    }

    [Fact]
    public void WasNodeVisited_NotVisited_ReturnsFalse()
    {
        Assert.False(_provider.WasNodeVisited("merchant_dialogue", "greeting"));
    }

    [Fact]
    public void WasNodeVisited_AfterVisit_ReturnsTrue()
    {
        _provider.RecordNodeVisit("merchant_dialogue", "greeting");

        Assert.True(_provider.WasNodeVisited("merchant_dialogue", "greeting"));
    }

    [Fact]
    public void DialogueHistory_MultipleNodes_TracksIndependently()
    {
        _provider.RecordNodeVisit("merchant_dialogue", "greeting");
        _provider.RecordNodeVisit("merchant_dialogue", "shop");
        _provider.RecordNodeVisit("merchant_dialogue", "farewell");

        Assert.True(_provider.WasNodeVisited("merchant_dialogue", "greeting"));
        Assert.True(_provider.WasNodeVisited("merchant_dialogue", "shop"));
        Assert.True(_provider.WasNodeVisited("merchant_dialogue", "farewell"));
        Assert.False(_provider.WasNodeVisited("merchant_dialogue", "secret"));
    }

    [Fact]
    public void DialogueHistory_MultipleDialogues_TracksIndependently()
    {
        _provider.RecordNodeVisit("merchant_dialogue", "greeting");
        _provider.RecordNodeVisit("quest_dialogue", "greeting");

        Assert.True(_provider.WasNodeVisited("merchant_dialogue", "greeting"));
        Assert.True(_provider.WasNodeVisited("quest_dialogue", "greeting"));
        Assert.False(_provider.WasNodeVisited("merchant_dialogue", "quest_node"));
        Assert.False(_provider.WasNodeVisited("quest_dialogue", "shop_node"));
    }

    [Fact]
    public void GetBossDefeatedCount_AlwaysReturnsZero()
    {
        // This is a stub implementation that doesn't persist across sessions
        Assert.Equal(0, _provider.GetBossDefeatedCount("dragon_boss"));
    }

    [Fact]
    public void IncrementBossDefeatedCount_DoesNothing()
    {
        // This is a stub implementation
        _provider.IncrementBossDefeatedCount("dragon_boss");
        Assert.Equal(0, _provider.GetBossDefeatedCount("dragon_boss"));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void CompleteQuestScenario_AllOperationsTogether()
    {
        // Scenario: Player completes a quest that requires consumables and gives rewards
        // NOTE: Material-related operations removed - BuildingMaterials now in IGameplayItemProvider

        // Check requirements: Player has quest token and consumables
        _provider.GiveItem("QuestTokens", "quest_active");
        _provider.GiveItem("Consumables", "quest_item", 10);
        _avatar.Stats.Credits = 50;

        Assert.True(_provider.HasItem("QuestTokens", "quest_active"));
        Assert.Equal(10, _provider.GetItemQuantity("Consumables", "quest_item"));

        // Quest completion: Take quest items, give rewards
        _provider.TakeItem("Consumables", "quest_item", 10);
        _provider.TakeItem("QuestTokens", "quest_active");

        _provider.TransferCurrency(200);
        _provider.GiveItem("Consumables", "health_potion", 3);
        _provider.GiveItem("Equipment", "legendary_sword");
        _provider.UnlockAchievement("quest_master");

        // Verify final state
        Assert.False(_provider.HasItem("QuestTokens", "quest_active"));
        Assert.Equal(0, _provider.GetItemQuantity("Consumables", "quest_item"));
        Assert.Equal(250, _provider.GetCredits());
        Assert.Equal(3, _provider.GetItemQuantity("Consumables", "health_potion"));
        Assert.True(_provider.HasItem("Equipment", "legendary_sword"));
        Assert.True(_provider.HasAchievement("quest_master"));
    }

    [Fact]
    public void MerchantTradeScenario_BuyAndSellItems()
    {
        // Start with some money
        _provider.TransferCurrency(500);

        // Buy consumables
        _provider.TransferCurrency(-100);
        _provider.GiveItem("Consumables", "health_potion", 5);

        // Buy equipment
        _provider.TransferCurrency(-200);
        _provider.GiveItem("Equipment", "iron_armor");

        // Sell some consumables (simulating trade)
        // NOTE: Material trading removed - BuildingMaterials now in IGameplayItemProvider
        _provider.GiveItem("Consumables", "wolf_pelt", 10);
        _provider.TakeItem("Consumables", "wolf_pelt", 10);
        _provider.TransferCurrency(50);

        // Verify final state
        Assert.Equal(250, _provider.GetCredits());
        Assert.Equal(5, _provider.GetItemQuantity("Consumables", "health_potion"));
        Assert.True(_provider.HasItem("Equipment", "iron_armor"));
        Assert.Equal(0, _provider.GetItemQuantity("Consumables", "wolf_pelt"));
    }

    #endregion
}
