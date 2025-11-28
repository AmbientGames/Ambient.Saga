using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Domain.Rpg.Dialogue;

namespace Ambient.SagaEngine.Tests.Rpg.Dialogue;

public class DirectDialogueStateProviderTests
{
    private readonly World _world;
    private readonly AvatarBase _avatar;
    private readonly DirectDialogueStateProvider _provider;

    public DirectDialogueStateProviderTests()
    {
        _world = new World();
        _avatar = new AvatarBase
        {
            Capabilities = new ItemCollection
            {
                QuestTokens = Array.Empty<QuestTokenEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                BuildingMaterials = Array.Empty<BuildingMaterialEntry>(),
                Equipment = Array.Empty<EquipmentEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                Spells = Array.Empty<SpellEntry>()
            },
            Achievements = Array.Empty<AchievementEntry>(),
            Stats = new CharacterStats { Credits = 0, Health = 1.0f, Stamina = 1.0f, Mana = 1.0f }
        };
        _provider = new DirectDialogueStateProvider(_world, _avatar);
    }

    #region Quest Tokens

    [Fact]
    public void HasQuestToken_InitiallyEmpty_ReturnsFalse()
    {
        Assert.False(_provider.HasQuestToken("quest_001"));
    }

    [Fact]
    public void AddQuestToken_AddsNewToken()
    {
        _provider.AddQuestToken("quest_001");

        Assert.True(_provider.HasQuestToken("quest_001"));
        Assert.Single(_avatar.Capabilities.QuestTokens);
        Assert.Equal("quest_001", _avatar.Capabilities.QuestTokens[0].QuestTokenRef);
    }

    [Fact]
    public void AddQuestToken_DoesNotAddDuplicates()
    {
        _provider.AddQuestToken("quest_001");
        _provider.AddQuestToken("quest_001");

        Assert.Single(_avatar.Capabilities.QuestTokens);
    }

    [Fact]
    public void RemoveQuestToken_RemovesExistingToken()
    {
        _provider.AddQuestToken("quest_001");
        _provider.RemoveQuestToken("quest_001");

        Assert.False(_provider.HasQuestToken("quest_001"));
        Assert.Empty(_avatar.Capabilities.QuestTokens);
    }

    [Fact]
    public void RemoveQuestToken_NonExistent_DoesNothing()
    {
        _provider.RemoveQuestToken("quest_999");
        Assert.Empty(_avatar.Capabilities.QuestTokens);
    }

    [Fact]
    public void QuestTokens_MultipleTokens_WorksCorrectly()
    {
        _provider.AddQuestToken("quest_001");
        _provider.AddQuestToken("quest_002");
        _provider.AddQuestToken("quest_003");

        Assert.True(_provider.HasQuestToken("quest_001"));
        Assert.True(_provider.HasQuestToken("quest_002"));
        Assert.True(_provider.HasQuestToken("quest_003"));
        Assert.Equal(3, _avatar.Capabilities.QuestTokens.Length);

        _provider.RemoveQuestToken("quest_002");

        Assert.True(_provider.HasQuestToken("quest_001"));
        Assert.False(_provider.HasQuestToken("quest_002"));
        Assert.True(_provider.HasQuestToken("quest_003"));
        Assert.Equal(2, _avatar.Capabilities.QuestTokens.Length);
    }

    #endregion

    #region Consumables (Stackable)

    [Fact]
    public void GetConsumableQuantity_InitiallyEmpty_ReturnsZero()
    {
        Assert.Equal(0, _provider.GetConsumableQuantity("health_potion"));
    }

    [Fact]
    public void AddConsumable_CreatesNewEntry()
    {
        _provider.AddConsumable("health_potion", 5);

        Assert.Equal(5, _provider.GetConsumableQuantity("health_potion"));
        Assert.Single(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void AddConsumable_ExistingItem_StacksQuantity()
    {
        _provider.AddConsumable("health_potion", 5);
        _provider.AddConsumable("health_potion", 3);

        Assert.Equal(8, _provider.GetConsumableQuantity("health_potion"));
        Assert.Single(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void AddConsumable_ZeroOrNegative_DoesNothing()
    {
        _provider.AddConsumable("health_potion", 0);
        _provider.AddConsumable("mana_potion", -5);

        Assert.Equal(0, _provider.GetConsumableQuantity("health_potion"));
        Assert.Equal(0, _provider.GetConsumableQuantity("mana_potion"));
        Assert.Empty(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void RemoveConsumable_ReducesQuantity()
    {
        _provider.AddConsumable("health_potion", 10);
        _provider.RemoveConsumable("health_potion", 3);

        Assert.Equal(7, _provider.GetConsumableQuantity("health_potion"));
        Assert.Single(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void RemoveConsumable_ReducesToZero_RemovesEntry()
    {
        _provider.AddConsumable("health_potion", 5);
        _provider.RemoveConsumable("health_potion", 5);

        Assert.Equal(0, _provider.GetConsumableQuantity("health_potion"));
        Assert.Empty(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void RemoveConsumable_MoreThanAvailable_ClampsToZeroAndRemoves()
    {
        _provider.AddConsumable("health_potion", 3);
        _provider.RemoveConsumable("health_potion", 10);

        Assert.Equal(0, _provider.GetConsumableQuantity("health_potion"));
        Assert.Empty(_avatar.Capabilities.Consumables);
    }

    [Fact]
    public void RemoveConsumable_NonExistent_DoesNothing()
    {
        _provider.RemoveConsumable("health_potion", 5);
        Assert.Empty(_avatar.Capabilities.Consumables);
    }

    #endregion

    #region Materials (Stackable)

    [Fact]
    public void GetMaterialQuantity_InitiallyEmpty_ReturnsZero()
    {
        Assert.Equal(0, _provider.GetMaterialQuantity("iron_ore"));
    }

    [Fact]
    public void AddMaterial_CreatesNewEntry()
    {
        _provider.AddMaterial("iron_ore", 10);

        Assert.Equal(10, _provider.GetMaterialQuantity("iron_ore"));
        Assert.Single(_avatar.Capabilities.BuildingMaterials);
    }

    [Fact]
    public void AddMaterial_ExistingItem_StacksQuantity()
    {
        _provider.AddMaterial("iron_ore", 10);
        _provider.AddMaterial("iron_ore", 5);

        Assert.Equal(15, _provider.GetMaterialQuantity("iron_ore"));
        Assert.Single(_avatar.Capabilities.BuildingMaterials);
    }

    [Fact]
    public void RemoveMaterial_ReducesQuantity()
    {
        _provider.AddMaterial("iron_ore", 20);
        _provider.RemoveMaterial("iron_ore", 7);

        Assert.Equal(13, _provider.GetMaterialQuantity("iron_ore"));
        Assert.Single(_avatar.Capabilities.BuildingMaterials);
    }

    [Fact]
    public void RemoveMaterial_ReducesToZero_RemovesEntry()
    {
        _provider.AddMaterial("iron_ore", 10);
        _provider.RemoveMaterial("iron_ore", 10);

        Assert.Equal(0, _provider.GetMaterialQuantity("iron_ore"));
        Assert.Empty(_avatar.Capabilities.BuildingMaterials);
    }

    [Fact]
    public void Materials_MultipleTypes_WorkIndependently()
    {
        _provider.AddMaterial("iron_ore", 10);
        _provider.AddMaterial("gold_ore", 5);
        _provider.AddMaterial("copper_ore", 15);

        Assert.Equal(10, _provider.GetMaterialQuantity("iron_ore"));
        Assert.Equal(5, _provider.GetMaterialQuantity("gold_ore"));
        Assert.Equal(15, _provider.GetMaterialQuantity("copper_ore"));
        Assert.Equal(3, _avatar.Capabilities.BuildingMaterials.Length);
    }

    #endregion

    #region Equipment (Degradable)

    [Fact]
    public void HasEquipment_InitiallyEmpty_ReturnsFalse()
    {
        Assert.False(_provider.HasEquipment("iron_sword"));
    }

    [Fact]
    public void AddEquipment_AddsNewItem_WithFullCondition()
    {
        _provider.AddEquipment("iron_sword");

        Assert.True(_provider.HasEquipment("iron_sword"));
        Assert.Single(_avatar.Capabilities.Equipment);
        Assert.Equal(1.0f, _avatar.Capabilities.Equipment[0].Condition);
    }

    [Fact]
    public void AddEquipment_DoesNotAddDuplicates()
    {
        _provider.AddEquipment("iron_sword");
        _provider.AddEquipment("iron_sword");

        Assert.Single(_avatar.Capabilities.Equipment);
    }

    [Fact]
    public void RemoveEquipment_RemovesExistingItem()
    {
        _provider.AddEquipment("iron_sword");
        _provider.RemoveEquipment("iron_sword");

        Assert.False(_provider.HasEquipment("iron_sword"));
        Assert.Empty(_avatar.Capabilities.Equipment);
    }

    [Fact]
    public void RemoveEquipment_NonExistent_DoesNothing()
    {
        _provider.RemoveEquipment("iron_sword");
        Assert.Empty(_avatar.Capabilities.Equipment);
    }

    [Fact]
    public void Equipment_MultipleItems_WorksCorrectly()
    {
        _provider.AddEquipment("iron_sword");
        _provider.AddEquipment("steel_armor");
        _provider.AddEquipment("leather_boots");

        Assert.True(_provider.HasEquipment("iron_sword"));
        Assert.True(_provider.HasEquipment("steel_armor"));
        Assert.True(_provider.HasEquipment("leather_boots"));
        Assert.Equal(3, _avatar.Capabilities.Equipment.Length);

        _provider.RemoveEquipment("steel_armor");

        Assert.True(_provider.HasEquipment("iron_sword"));
        Assert.False(_provider.HasEquipment("steel_armor"));
        Assert.True(_provider.HasEquipment("leather_boots"));
        Assert.Equal(2, _avatar.Capabilities.Equipment.Length);
    }

    #endregion

    #region Tools (Degradable)

    [Fact]
    public void HasTool_InitiallyEmpty_ReturnsFalse()
    {
        Assert.False(_provider.HasTool("pickaxe"));
    }

    [Fact]
    public void AddTool_AddsNewItem_WithFullCondition()
    {
        _provider.AddTool("pickaxe");

        Assert.True(_provider.HasTool("pickaxe"));
        Assert.Single(_avatar.Capabilities.Tools);
        Assert.Equal(1.0f, _avatar.Capabilities.Tools[0].Condition);
    }

    [Fact]
    public void AddTool_DoesNotAddDuplicates()
    {
        _provider.AddTool("pickaxe");
        _provider.AddTool("pickaxe");

        Assert.Single(_avatar.Capabilities.Tools);
    }

    [Fact]
    public void RemoveTool_RemovesExistingItem()
    {
        _provider.AddTool("pickaxe");
        _provider.RemoveTool("pickaxe");

        Assert.False(_provider.HasTool("pickaxe"));
        Assert.Empty(_avatar.Capabilities.Tools);
    }

    #endregion

    #region Spells (Degradable)

    [Fact]
    public void HasSpell_InitiallyEmpty_ReturnsFalse()
    {
        Assert.False(_provider.HasSpell("fireball"));
    }

    [Fact]
    public void AddSpell_AddsNewSpell_WithFullCondition()
    {
        _provider.AddSpell("fireball");

        Assert.True(_provider.HasSpell("fireball"));
        Assert.Single(_avatar.Capabilities.Spells);
        Assert.Equal(1.0f, _avatar.Capabilities.Spells[0].Condition);
    }

    [Fact]
    public void AddSpell_DoesNotAddDuplicates()
    {
        _provider.AddSpell("fireball");
        _provider.AddSpell("fireball");

        Assert.Single(_avatar.Capabilities.Spells);
    }

    [Fact]
    public void RemoveSpell_RemovesExistingSpell()
    {
        _provider.AddSpell("fireball");
        _provider.RemoveSpell("fireball");

        Assert.False(_provider.HasSpell("fireball"));
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
        // Scenario: Player completes a quest that requires materials and rewards them

        // Check requirements: Player has quest token and materials
        _provider.AddQuestToken("quest_active");
        _provider.AddMaterial("iron_ore", 10);
        _provider.AddMaterial("gold_ore", 5);
        _avatar.Stats.Credits = 50;

        Assert.True(_provider.HasQuestToken("quest_active"));
        Assert.Equal(10, _provider.GetMaterialQuantity("iron_ore"));
        Assert.Equal(5, _provider.GetMaterialQuantity("gold_ore"));

        // Quest completion: Take materials, give rewards
        _provider.RemoveMaterial("iron_ore", 10);
        _provider.RemoveMaterial("gold_ore", 5);
        _provider.RemoveQuestToken("quest_active");

        _provider.TransferCurrency(200);
        _provider.AddConsumable("health_potion", 3);
        _provider.AddEquipment("legendary_sword");
        _provider.UnlockAchievement("quest_master");

        // Verify final state
        Assert.False(_provider.HasQuestToken("quest_active"));
        Assert.Equal(0, _provider.GetMaterialQuantity("iron_ore"));
        Assert.Equal(0, _provider.GetMaterialQuantity("gold_ore"));
        Assert.Equal(250, _provider.GetCredits());
        Assert.Equal(3, _provider.GetConsumableQuantity("health_potion"));
        Assert.True(_provider.HasEquipment("legendary_sword"));
        Assert.True(_provider.HasAchievement("quest_master"));
    }

    [Fact]
    public void MerchantTradeScenario_BuyAndSellItems()
    {
        // Start with some money
        _provider.TransferCurrency(500);

        // Buy consumables
        _provider.TransferCurrency(-100);
        _provider.AddConsumable("health_potion", 5);

        // Buy equipment
        _provider.TransferCurrency(-200);
        _provider.AddEquipment("iron_armor");

        // Sell some materials
        _provider.AddMaterial("wolf_pelt", 10);
        _provider.RemoveMaterial("wolf_pelt", 10);
        _provider.TransferCurrency(50);

        // Verify final state
        Assert.Equal(250, _provider.GetCredits());
        Assert.Equal(5, _provider.GetConsumableQuantity("health_potion"));
        Assert.True(_provider.HasEquipment("iron_armor"));
        Assert.Equal(0, _provider.GetMaterialQuantity("wolf_pelt"));
    }

    #endregion
}
