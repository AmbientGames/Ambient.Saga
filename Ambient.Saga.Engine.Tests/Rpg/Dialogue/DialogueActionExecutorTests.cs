using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue.Events;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue.Execution;

namespace Ambient.Saga.Engine.Tests.Rpg.Dialogue;

public class DialogueActionExecutorTests
{
    private readonly MockDialogueStateProvider _state;
    private readonly DialogueActionExecutor _executor;

    public DialogueActionExecutorTests()
    {
        _state = new MockDialogueStateProvider();
        _executor = new DialogueActionExecutor(_state);
    }

    #region Quest Token Actions

    [Fact]
    public void GiveQuestToken_AddsTokenToPlayer()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GiveQuestToken,
            RefName = "dragon_quest"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasQuestToken("dragon_quest"));
    }

    [Fact]
    public void TakeQuestToken_RemovesTokenFromPlayer()
    {
        _state.AddQuestToken("dragon_quest");

        var action = new DialogueAction
        {
            Type = DialogueActionType.TakeQuestToken,
            RefName = "dragon_quest"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.False(_state.HasQuestToken("dragon_quest"));
    }

    #endregion

    #region Stackable Item Actions

    [Fact]
    public void GiveConsumable_AddsQuantityToPlayer()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GiveConsumable,
            RefName = "health_potion",
            Amount = 5
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(5, _state.GetConsumableQuantity("health_potion"));
    }

    [Fact]
    public void TakeConsumable_RemovesQuantityFromPlayer()
    {
        _state.AddConsumable("health_potion", 10);

        var action = new DialogueAction
        {
            Type = DialogueActionType.TakeConsumable,
            RefName = "health_potion",
            Amount = 3
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(7, _state.GetConsumableQuantity("health_potion"));
    }

    [Fact]
    public void GiveMaterial_AddsQuantityToPlayer()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GiveMaterial,
            RefName = "iron_ore",
            Amount = 20
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(20, _state.GetMaterialQuantity("iron_ore"));
    }

    [Fact]
    public void TakeMaterial_RemovesQuantityFromPlayer()
    {
        _state.AddMaterial("iron_ore", 50);

        var action = new DialogueAction
        {
            Type = DialogueActionType.TakeMaterial,
            RefName = "iron_ore",
            Amount = 15
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(35, _state.GetMaterialQuantity("iron_ore"));
    }

    [Fact]
    public void GiveBlock_AddsBlockToPlayer()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GiveBlock,
            RefName = "stone_block",
            Amount = 64
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(64, _state.GetBlockQuantity("stone_block"));
    }

    [Fact]
    public void TakeBlock_RemovesBlockFromPlayer()
    {
        _state.AddBlock("stone_block", 100);

        var action = new DialogueAction
        {
            Type = DialogueActionType.TakeBlock,
            RefName = "stone_block",
            Amount = 25
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(75, _state.GetBlockQuantity("stone_block"));
    }

    #endregion

    #region Degradable Item Actions

    [Fact]
    public void GiveEquipment_AddsItemToPlayer()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GiveEquipment,
            RefName = "iron_sword"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasEquipment("iron_sword"));
    }

    [Fact]
    public void TakeEquipment_RemovesItemFromPlayer()
    {
        _state.AddEquipment("iron_sword");

        var action = new DialogueAction
        {
            Type = DialogueActionType.TakeEquipment,
            RefName = "iron_sword"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.False(_state.HasEquipment("iron_sword"));
    }

    [Fact]
    public void GiveTool_AddsToolToPlayer()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GiveTool,
            RefName = "pickaxe"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasTool("pickaxe"));
    }

    [Fact]
    public void GiveSpell_AddsSpellToPlayer()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GiveSpell,
            RefName = "fireball"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasSpell("fireball"));
    }

    [Fact]
    public void TakeTool_RemovesToolFromPlayer()
    {
        _state.AddTool("pickaxe");

        var action = new DialogueAction
        {
            Type = DialogueActionType.TakeTool,
            RefName = "pickaxe"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.False(_state.HasTool("pickaxe"));
    }

    [Fact]
    public void TakeSpell_RemovesSpellFromPlayer()
    {
        _state.AddSpell("fireball");

        var action = new DialogueAction
        {
            Type = DialogueActionType.TakeSpell,
            RefName = "fireball"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.False(_state.HasSpell("fireball"));
    }

    #endregion

    #region Currency Actions

    [Fact]
    public void TransferCurrency_Positive_AddsCreditsToPlayer()
    {
        _state.Credits = 100;

        var action = new DialogueAction
        {
            Type = DialogueActionType.TransferCurrency,
            Amount = 50
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(150, _state.GetCredits());
    }

    [Fact]
    public void TransferCurrency_Negative_RemovesCreditsFromPlayer()
    {
        _state.Credits = 100;

        var action = new DialogueAction
        {
            Type = DialogueActionType.TransferCurrency,
            Amount = -30
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(70, _state.GetCredits());
    }

    #endregion

    #region Achievement Actions

    [Fact]
    public void UnlockAchievement_UnlocksAchievementForPlayer()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.UnlockAchievement,
            RefName = "dragon_slayer"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasAchievement("dragon_slayer"));
    }

    #endregion

    #region System Transition Events

    [Fact]
    public void OpenMerchantTrade_RaisesEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.OpenMerchantTrade,
            CharacterRef = "merchant_npc_01"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as OpenMerchantTradeEvent;
        Assert.NotNull(evt);
        Assert.Equal("merchant_npc_01", evt.CharacterRef);
        Assert.Equal("test_tree", evt.DialogueTreeRef);
        Assert.Equal("node1", evt.NodeId);
    }

    [Fact]
    public void StartBossBattle_RaisesEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.StartBossBattle,
            CharacterRef = "dragon_boss"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as StartBossBattleEvent;
        Assert.NotNull(evt);
        Assert.Equal("dragon_boss", evt.CharacterRef);
    }

    [Fact]
    public void StartCombat_RaisesEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.StartCombat,
            CharacterRef = "bandit_01"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as StartCombatEvent;
        Assert.NotNull(evt);
        Assert.Equal("bandit_01", evt.CharacterRef);
    }

    [Fact]
    public void SpawnCharacters_RaisesEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.SpawnCharacters,
            CharacterArchetypeRef = "bandit",
            Amount = 3
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as SpawnCharactersEvent;
        Assert.NotNull(evt);
        Assert.Equal("bandit", evt.CharacterArchetypeRef);
        Assert.Equal(3, evt.Amount);
    }

    [Fact]
    public void ClearEvents_RemovesAllRaisedEvents()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.OpenMerchantTrade,
            CharacterRef = "merchant_npc_01"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);
        Assert.Single(_executor.RaisedEvents);

        _executor.ClearEvents();
        Assert.Empty(_executor.RaisedEvents);
    }

    #endregion

    #region Multiple Actions

    [Fact]
    public void ExecuteAll_ExecutesAllActionsInSequence()
    {
        var actions = new[]
        {
            new DialogueAction
            {
                Type = DialogueActionType.GiveQuestToken,
                RefName = "quest1"
            },
            new DialogueAction
            {
                Type = DialogueActionType.TransferCurrency,
                Amount = 100
            },
            new DialogueAction
            {
                Type = DialogueActionType.GiveConsumable,
                RefName = "health_potion",
                Amount = 5
            }
        };

        _executor.ExecuteAll(actions, "test_tree", "node1", "test_character");

        Assert.True(_state.HasQuestToken("quest1"));
        Assert.Equal(100, _state.GetCredits());
        Assert.Equal(5, _state.GetConsumableQuantity("health_potion"));
    }

    [Fact]
    public void ExecuteAll_WithEmptyArray_DoesNothing()
    {
        _executor.ExecuteAll(Array.Empty<DialogueAction>(), "test_tree", "node1", "test_character");
        Assert.Empty(_executor.RaisedEvents);
    }

    #endregion

    #region Character Trait Actions

    [Fact]
    public void AssignTrait_BooleanTrait_AssignsWithoutValue()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.AssignTrait,
            Trait = CharacterTraitType.Hostile,
            TraitSpecified = true
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasTrait("Hostile"));
        Assert.Null(_state.GetTraitValue("Hostile"));
    }

    [Fact]
    public void AssignTrait_NumericTrait_AssignsWithValue()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.AssignTrait,
            Trait = CharacterTraitType.Aggression,
            TraitSpecified = true,
            TraitValue = 75,
            TraitValueSpecified = true
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasTrait("Aggression"));
        Assert.Equal(75, _state.GetTraitValue("Aggression"));
    }

    [Fact]
    public void AssignTrait_OverwritesExistingTrait()
    {
        _state.AssignTrait("Morale", 50);

        var action = new DialogueAction
        {
            Type = DialogueActionType.AssignTrait,
            Trait = CharacterTraitType.Morale,
            TraitSpecified = true,
            TraitValue = 90,
            TraitValueSpecified = true
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(90, _state.GetTraitValue("Morale"));
    }

    [Fact]
    public void AssignTrait_WithoutTraitSpecified_ThrowsException()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.AssignTrait,
            // TraitSpecified defaults to false
            TraitValue = 50,
            TraitValueSpecified = true
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _executor.Execute(action, "test_tree", "node1", "test_character", true));
        Assert.Contains("AssignTrait action requires Trait attribute", ex.Message);
    }

    [Fact]
    public void RemoveTrait_RemovesExistingTrait()
    {
        _state.AssignTrait("Hostile", null);

        var action = new DialogueAction
        {
            Type = DialogueActionType.RemoveTrait,
            Trait = CharacterTraitType.Hostile,
            TraitSpecified = true
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.False(_state.HasTrait("Hostile"));
    }

    [Fact]
    public void RemoveTrait_OnNonExistentTrait_DoesNotThrow()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.RemoveTrait,
            Trait = CharacterTraitType.Friendly,
            TraitSpecified = true
        };

        // Should not throw
        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.False(_state.HasTrait("Friendly"));
    }

    [Fact]
    public void RemoveTrait_WithoutTraitSpecified_ThrowsException()
    {
        _state.AssignTrait("Hostile", null);

        var action = new DialogueAction
        {
            Type = DialogueActionType.RemoveTrait
            // TraitSpecified defaults to false
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _executor.Execute(action, "test_tree", "node1", "test_character", true));
        Assert.Contains("RemoveTrait action requires Trait attribute", ex.Message);
    }

    [Fact]
    public void SetCharacterState_SetsStateAsTrait()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.SetCharacterState,
            RefName = "Friendly"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasTrait("Friendly"));
        Assert.Null(_state.GetTraitValue("Friendly"));
    }

    [Fact]
    public void ExecuteAll_WithMultipleTraitActions_ExecutesInOrder()
    {
        var actions = new[]
        {
            new DialogueAction
            {
                Type = DialogueActionType.AssignTrait,
                Trait = CharacterTraitType.Hostile,
                TraitSpecified = true
            },
            new DialogueAction
            {
                Type = DialogueActionType.AssignTrait,
                Trait = CharacterTraitType.Aggression,
                TraitSpecified = true,
                TraitValue = 80,
                TraitValueSpecified = true
            },
            new DialogueAction
            {
                Type = DialogueActionType.AssignTrait,
                Trait = CharacterTraitType.WillTrade,
                TraitSpecified = true
            }
        };

        _executor.ExecuteAll(actions, "test_tree", "node1", "test_character");

        Assert.True(_state.HasTrait("Hostile"));
        Assert.True(_state.HasTrait("Aggression"));
        Assert.Equal(80, _state.GetTraitValue("Aggression"));
        Assert.True(_state.HasTrait("WillTrade"));
    }

    #endregion

    #region Quest Actions

    [Fact]
    public void AcceptQuest_RaisesAcceptQuestEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.AcceptQuest,
            RefName = "SAVE_THE_VILLAGE"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as AcceptQuestEvent;
        Assert.NotNull(evt);
        Assert.Equal("SAVE_THE_VILLAGE", evt.QuestRef);
    }

    [Fact]
    public void CompleteQuest_RaisesCompleteQuestEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.CompleteQuest,
            RefName = "DRAGON_SLAYER"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as CompleteQuestEvent;
        Assert.NotNull(evt);
        Assert.Equal("DRAGON_SLAYER", evt.QuestRef);
    }

    [Fact]
    public void AbandonQuest_RaisesAbandonQuestEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.AbandonQuest,
            RefName = "FAILED_ESCORT"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as AbandonQuestEvent;
        Assert.NotNull(evt);
        Assert.Equal("FAILED_ESCORT", evt.QuestRef);
    }

    #endregion

    #region Party Actions

    [Fact]
    public void JoinParty_AddsCharacterToParty()
    {
        _state.MaxPartySlots = 2;

        var action = new DialogueAction
        {
            Type = DialogueActionType.JoinParty,
            CharacterRef = "LYRA_THE_HEALER"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.IsInParty("LYRA_THE_HEALER"));
        Assert.Equal(1, _state.GetPartySize());
    }

    [Fact]
    public void JoinParty_WhenPartyFull_DoesNotAdd()
    {
        _state.MaxPartySlots = 1;
        _state.AddPartyMember("COMPANION_A");

        var action = new DialogueAction
        {
            Type = DialogueActionType.JoinParty,
            CharacterRef = "LYRA_THE_HEALER"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.False(_state.IsInParty("LYRA_THE_HEALER"));
        Assert.Equal(1, _state.GetPartySize());
    }

    [Fact]
    public void JoinParty_WhenAlreadyInParty_DoesNotDuplicate()
    {
        _state.MaxPartySlots = 2;
        _state.AddPartyMember("LYRA_THE_HEALER");

        var action = new DialogueAction
        {
            Type = DialogueActionType.JoinParty,
            CharacterRef = "LYRA_THE_HEALER"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.IsInParty("LYRA_THE_HEALER"));
        Assert.Equal(1, _state.GetPartySize());
    }

    [Fact]
    public void JoinParty_RaisesPartyMemberJoinedEvent()
    {
        _state.MaxPartySlots = 2;

        var action = new DialogueAction
        {
            Type = DialogueActionType.JoinParty,
            CharacterRef = "LYRA_THE_HEALER"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as PartyMemberJoinedEvent;
        Assert.NotNull(evt);
        Assert.Equal("LYRA_THE_HEALER", evt.CharacterRef);
    }

    [Fact]
    public void LeaveParty_RemovesCharacterFromParty()
    {
        _state.MaxPartySlots = 2;
        _state.AddPartyMember("LYRA_THE_HEALER");

        var action = new DialogueAction
        {
            Type = DialogueActionType.LeaveParty,
            CharacterRef = "LYRA_THE_HEALER"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.False(_state.IsInParty("LYRA_THE_HEALER"));
        Assert.Equal(0, _state.GetPartySize());
    }

    [Fact]
    public void LeaveParty_WhenNotInParty_DoesNothing()
    {
        _state.MaxPartySlots = 2;
        _state.AddPartyMember("COMPANION_A");

        var action = new DialogueAction
        {
            Type = DialogueActionType.LeaveParty,
            CharacterRef = "LYRA_THE_HEALER"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.IsInParty("COMPANION_A"));
        Assert.Equal(1, _state.GetPartySize());
    }

    [Fact]
    public void LeaveParty_RaisesPartyMemberLeftEvent()
    {
        _state.MaxPartySlots = 2;
        _state.AddPartyMember("LYRA_THE_HEALER");

        var action = new DialogueAction
        {
            Type = DialogueActionType.LeaveParty,
            CharacterRef = "LYRA_THE_HEALER"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as PartyMemberLeftEvent;
        Assert.NotNull(evt);
        Assert.Equal("LYRA_THE_HEALER", evt.CharacterRef);
    }

    [Fact]
    public void PartyActions_CanBeCombinedWithOtherActions()
    {
        _state.MaxPartySlots = 2;

        var actions = new[]
        {
            new DialogueAction
            {
                Type = DialogueActionType.GiveQuestToken,
                RefName = "companion_recruited"
            },
            new DialogueAction
            {
                Type = DialogueActionType.JoinParty,
                CharacterRef = "LYRA_THE_HEALER"
            },
            new DialogueAction
            {
                Type = DialogueActionType.TransferCurrency,
                Amount = -50  // Pay a recruitment fee
            }
        };

        _executor.ExecuteAll(actions, "test_tree", "node1", "test_character");

        Assert.True(_state.HasQuestToken("companion_recruited"));
        Assert.True(_state.IsInParty("LYRA_THE_HEALER"));
        Assert.Equal(-50, _state.GetCredits());
    }

    #endregion

    #region Reputation Actions

    [Fact]
    public void ChangeReputation_IncreasesReputation()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.ChangeReputation,
            FactionRef = "MERCHANTS_GUILD",
            Amount = 500
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(500, _state.GetFactionReputation("MERCHANTS_GUILD"));
    }

    [Fact]
    public void ChangeReputation_DecreasesReputation()
    {
        // Start with some reputation
        _state.ChangeReputation("THIEVES_GUILD", 1000);

        var action = new DialogueAction
        {
            Type = DialogueActionType.ChangeReputation,
            FactionRef = "THIEVES_GUILD",
            Amount = -300
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Equal(700, _state.GetFactionReputation("THIEVES_GUILD"));
    }

    [Fact]
    public void ChangeReputation_RaisesReputationChangedEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.ChangeReputation,
            FactionRef = "MERCHANTS_GUILD",
            Amount = 500
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as ReputationChangedEvent;
        Assert.NotNull(evt);
        Assert.Equal("MERCHANTS_GUILD", evt.FactionRef);
        Assert.Equal(500, evt.Amount);
    }

    [Fact]
    public void ChangeReputation_IsIdempotent_OnlyChangesOnFirstVisit()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.ChangeReputation,
            FactionRef = "MERCHANTS_GUILD",
            Amount = 500
        };

        // First execution - should change reputation
        _executor.Execute(action, "test_tree", "node1", "test_character", true);
        Assert.Equal(500, _state.GetFactionReputation("MERCHANTS_GUILD"));

        // Second execution with shouldAwardRewards = false - should NOT change
        _executor.Execute(action, "test_tree", "node1", "test_character", false);
        Assert.Equal(500, _state.GetFactionReputation("MERCHANTS_GUILD"));
    }

    #endregion

    #region Battle Actions

    [Fact]
    public void ChangeStance_RaisesChangeStanceEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.ChangeStance,
            RefName = "AGGRESSIVE_STANCE"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as ChangeStanceEvent;
        Assert.NotNull(evt);
        Assert.Equal("AGGRESSIVE_STANCE", evt.StanceRef);
        Assert.Equal("test_character", evt.CharacterRef);
    }

    [Fact]
    public void ChangeStance_UsesActionCharacterRef_WhenProvided()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.ChangeStance,
            RefName = "DEFENSIVE_STANCE",
            CharacterRef = "BOSS_CHARACTER"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        var evt = _executor.RaisedEvents[0] as ChangeStanceEvent;
        Assert.Equal("BOSS_CHARACTER", evt!.CharacterRef);
    }

    [Fact]
    public void ChangeAffinity_RaisesChangeAffinityEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.ChangeAffinity,
            RefName = "FIRE_AFFINITY"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as ChangeAffinityEvent;
        Assert.NotNull(evt);
        Assert.Equal("FIRE_AFFINITY", evt.AffinityRef);
        Assert.Equal("test_character", evt.CharacterRef);
    }

    [Fact]
    public void CastSpell_RaisesCastSpellEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.CastSpell,
            RefName = "FIREBALL"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as CastSpellEvent;
        Assert.NotNull(evt);
        Assert.Equal("FIREBALL", evt.SpellRef);
        Assert.Equal("test_character", evt.CharacterRef);
    }

    [Fact]
    public void SummonAlly_RaisesSummonAllyEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.SummonAlly,
            CharacterRef = "GUARDIAN_SPIRIT",
            CharacterArchetypeRef = "SUMMON_ARCHETYPE"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as SummonAllyEvent;
        Assert.NotNull(evt);
        Assert.Equal("GUARDIAN_SPIRIT", evt.CharacterRef);
        Assert.Equal("SUMMON_ARCHETYPE", evt.CharacterArchetypeRef);
    }

    [Fact]
    public void EndBattle_RaisesEndBattleEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.EndBattle,
            RefName = "Victory"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as EndBattleEvent;
        Assert.NotNull(evt);
        Assert.Equal("Victory", evt.Result);
    }

    [Fact]
    public void HealSelf_RaisesHealSelfEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.HealSelf,
            Amount = 50
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as HealSelfEvent;
        Assert.NotNull(evt);
        Assert.Equal("test_character", evt.CharacterRef);
        Assert.Equal(50, evt.Amount);
    }

    [Fact]
    public void ApplyStatusEffect_RaisesApplyStatusEffectEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.ApplyStatusEffect,
            RefName = "POISON",
            CharacterRef = "TARGET_ENEMY"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as ApplyStatusEffectEvent;
        Assert.NotNull(evt);
        Assert.Equal("POISON", evt.StatusEffectRef);
        Assert.Equal("TARGET_ENEMY", evt.TargetCharacterRef);
    }

    [Fact]
    public void BattleActions_AreNotIdempotent_AlwaysRaiseEvents()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.ChangeStance,
            RefName = "AGGRESSIVE_STANCE"
        };

        // Execute twice with shouldAwardRewards = false
        // Battle actions should still raise events (they're NOT idempotent)
        _executor.Execute(action, "test_tree", "node1", "test_character", false);
        _executor.Execute(action, "test_tree", "node1", "test_character", false);

        Assert.Equal(2, _executor.RaisedEvents.Count);
    }

    #endregion

    #region Affinity Actions

    [Fact]
    public void GrantAffinity_AddsAffinityToAvatar()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GrantAffinity,
            RefName = "Fire",
            CharacterRef = "fire_elemental"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasAffinity("Fire"));
        Assert.Equal("fire_elemental", _state.GetAffinitySource("Fire"));
    }

    [Fact]
    public void GrantAffinity_UsesCurrentCharacterRef_WhenCharacterRefNotSpecified()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GrantAffinity,
            RefName = "Lightning"
            // CharacterRef not specified - should fall back to current dialogue character
        };

        _executor.Execute(action, "test_tree", "node1", "thunder_mage", true);

        Assert.True(_state.HasAffinity("Lightning"));
        Assert.Equal("thunder_mage", _state.GetAffinitySource("Lightning"));
    }

    [Fact]
    public void GrantAffinity_RaisesAffinityGrantedEvent()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GrantAffinity,
            RefName = "Ice",
            CharacterRef = "frost_wyrm"
        };

        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.Single(_executor.RaisedEvents);
        var evt = _executor.RaisedEvents[0] as AffinityGrantedEvent;
        Assert.NotNull(evt);
        Assert.Equal("Ice", evt.AffinityRef);
        Assert.Equal("frost_wyrm", evt.CapturedFromCharacterRef);
    }

    [Fact]
    public void GrantAffinity_IsIdempotent_DoesNotAwardOnSecondVisit()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GrantAffinity,
            RefName = "Arcane",
            CharacterRef = "archmage"
        };

        // First execution - should award
        _executor.Execute(action, "test_tree", "node1", "test_character", true);

        Assert.True(_state.HasAffinity("Arcane"));
        Assert.Single(_executor.RaisedEvents);

        // Clear events
        _executor.ClearEvents();

        // Second execution with shouldAwardRewards = false - should NOT award again
        _executor.Execute(action, "test_tree", "node1", "test_character", false);

        // Affinity still exists (from first call), but no new event was raised
        Assert.True(_state.HasAffinity("Arcane"));
        Assert.Empty(_executor.RaisedEvents);
    }

    [Fact]
    public void GrantAffinity_WithExplicitCharacterRef_OverridesCurrentCharacter()
    {
        var action = new DialogueAction
        {
            Type = DialogueActionType.GrantAffinity,
            RefName = "Holy",
            CharacterRef = "high_priest" // Explicitly set
        };

        _executor.Execute(action, "test_tree", "node1", "different_character", true);

        Assert.True(_state.HasAffinity("Holy"));
        Assert.Equal("high_priest", _state.GetAffinitySource("Holy"));
    }

    #endregion
}
