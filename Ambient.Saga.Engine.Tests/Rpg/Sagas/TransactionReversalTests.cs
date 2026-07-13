using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Tests.Rpg.Sagas;

/// <summary>
/// Tests for TransactionReversed compensating transactions in the state machine fold.
/// TradeItemHandler emits these when avatar persistence fails after the saga-side
/// commit; replay must undo the referenced transaction's fold effect (restore
/// merchant stock) instead of quarantining the type as unknown.
/// </summary>
public class TransactionReversalTests
{
    private static readonly Guid MerchantInstanceId = Guid.NewGuid();

    private sealed class RecordingSagaMetrics : ISagaMetrics
    {
        public int UnknownTransactionTypeCount { get; private set; }

        public void IncrementSnapshotDeserializationFailure(Guid transactionId, long sequenceNumber) { }
        public void IncrementUnknownTransactionType(int transactionTypeValue) => UnknownTransactionTypeCount++;
        public void IncrementQuarantinedExtension(string extensionTypeName) { }
    }

    private static World CreateWorldWithMerchant()
    {
        var merchant = new Character
        {
            RefName = "Merchant",
            DisplayName = "Village Merchant",
            // Trade stock lives in Interactable.Loot — ApplyCharacterSpawned clones it
            // into the live CurrentInventory per spawn instance
            Interactable = new Interactable
            {
                Loot = new ItemCollection
                {
                    Consumables = new[]
                    {
                        new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 10 }
                    }
                }
            }
        };

        var sagaArc = new SagaArc
        {
            RefName = "VillageMerchant",
            DisplayName = "Village Merchant",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var world = new World();
        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.CharactersLookup[merchant.RefName] = merchant;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger>();
        world.ConsumablesLookup["HealthPotion"] = new Consumable
        {
            RefName = "HealthPotion",
            DisplayName = "Health Potion",
            WholesalePrice = 20
        };
        // ApplyQuestAccepted resolves the template from the world catalog
        world.QuestsLookup["REVERSAL_QUEST"] = new Quest
        {
            RefName = "REVERSAL_QUEST",
            DisplayName = "Reversal Quest",
            Stages = new QuestStages
            {
                StartStage = "STAGE_1",
                Stage = new[]
                {
                    new QuestStage { RefName = "STAGE_1" },
                    new QuestStage { RefName = "STAGE_2" }
                }
            }
        };

        return world;
    }

    private static SagaStateMachine CreateStateMachine(World world, ISagaMetrics? metrics = null)
        => new(world.SagaArcLookup["VillageMerchant"], world.SagaTriggersLookup["VillageMerchant"], world, metrics: metrics);

    private static SagaTransaction CreateTransaction(SagaTransactionType type, long sequenceNumber, Dictionary<string, string> data)
        => new()
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            AvatarId = Guid.NewGuid().ToString(),
            Status = TransactionStatus.Committed,
            LocalTimestamp = DateTime.UtcNow,
            SequenceNumber = sequenceNumber,
            Data = data
        };

    private static SagaTransaction CreateSpawnTransaction(long sequenceNumber)
        => CreateTransaction(SagaTransactionType.CharacterSpawned, sequenceNumber, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "Merchant",
            [TransactionDataKeys.CharacterInstanceId] = MerchantInstanceId.ToString()
        });

    private static SagaTransaction CreateReversalTransaction(long sequenceNumber, SagaTransaction original)
        => CreateTransaction(SagaTransactionType.TransactionReversed, sequenceNumber, new Dictionary<string, string>
        {
            [TransactionDataKeys.ReversedTransactionId] = original.TransactionId.ToString(),
            [TransactionDataKeys.Reason] = "Avatar persistence failed: test",
            [TransactionDataKeys.OriginalType] = original.Type.ToString()
        });

    [Fact]
    public void Replay_ReversedItemTraded_RestoresMerchantStock()
    {
        var world = CreateWorldWithMerchant();
        var stateMachine = CreateStateMachine(world);

        var trade = CreateTransaction(SagaTransactionType.ItemTraded, 2, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterInstanceId] = MerchantInstanceId.ToString(),
            [TransactionDataKeys.ItemRef] = "HealthPotion",
            [TransactionDataKeys.Quantity] = "3",
            [TransactionDataKeys.IsBuying] = "True", // player buys → merchant stock drops
            [TransactionDataKeys.PricePerItem] = "20",
            [TransactionDataKeys.TotalPrice] = "60"
        });

        // Sanity: without the reversal the trade drains merchant stock
        var stateWithoutReversal = stateMachine.Replay(new List<SagaTransaction>
        {
            CreateSpawnTransaction(1), trade
        });
        Assert.Equal(7, stateWithoutReversal.Characters[MerchantInstanceId.ToString()]
            .CurrentInventory!.Consumables!.Single(c => c.ConsumableRef == "HealthPotion").Quantity);

        // With the compensation the fold effect is undone
        var state = stateMachine.Replay(new List<SagaTransaction>
        {
            CreateSpawnTransaction(1), trade, CreateReversalTransaction(3, trade)
        });
        Assert.Equal(10, state.Characters[MerchantInstanceId.ToString()]
            .CurrentInventory!.Consumables!.Single(c => c.ConsumableRef == "HealthPotion").Quantity);
    }

    [Fact]
    public void Replay_ReversedReputationChanged_RestoresReputation()
    {
        var world = CreateWorldWithMerchant();
        var stateMachine = CreateStateMachine(world);

        var repChange = CreateTransaction(SagaTransactionType.ReputationChanged, 1, new Dictionary<string, string>
        {
            [TransactionDataKeys.FactionRef] = "VillageFaction",
            [TransactionDataKeys.Amount] = "10"
        });

        var state = stateMachine.Replay(new List<SagaTransaction>
        {
            repChange, CreateReversalTransaction(2, repChange)
        });

        Assert.Equal(0, state.FactionReputation["VillageFaction"]);
    }

    [Fact]
    public void Replay_ReversedQuestTokenAwarded_RemovesToken()
    {
        var world = CreateWorldWithMerchant();
        var stateMachine = CreateStateMachine(world);

        var award = CreateTransaction(SagaTransactionType.QuestTokenAwarded, 1, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = "token-reversal-test"
        });

        // Sanity: without the reversal the token is present
        var stateWithoutReversal = stateMachine.Replay(new List<SagaTransaction> { award });
        Assert.Contains("token-reversal-test", stateWithoutReversal.AwardedQuestTokens);

        var state = stateMachine.Replay(new List<SagaTransaction>
        {
            award, CreateReversalTransaction(2, award)
        });

        Assert.DoesNotContain("token-reversal-test", state.AwardedQuestTokens);
    }

    [Fact]
    public void Replay_ReversedQuestAccepted_RemovesFromActiveQuests()
    {
        var world = CreateWorldWithMerchant();
        var stateMachine = CreateStateMachine(world);

        var accept = CreateTransaction(SagaTransactionType.QuestAccepted, 1, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "REVERSAL_QUEST"
        });

        // Sanity: without the reversal the quest is active
        var stateWithoutReversal = stateMachine.Replay(new List<SagaTransaction> { accept });
        Assert.True(stateWithoutReversal.ActiveQuests.ContainsKey("REVERSAL_QUEST"));

        var state = stateMachine.Replay(new List<SagaTransaction>
        {
            accept, CreateReversalTransaction(2, accept)
        });

        Assert.False(state.ActiveQuests.ContainsKey("REVERSAL_QUEST"));
    }

    [Fact]
    public void Replay_ReversedQuestCompleted_RemovesFromCompleted_WithoutReactivating()
    {
        var world = CreateWorldWithMerchant();
        var stateMachine = CreateStateMachine(world);

        var questData = new Dictionary<string, string> { [TransactionDataKeys.QuestRef] = "REVERSAL_QUEST" };
        var accept = CreateTransaction(SagaTransactionType.QuestAccepted, 1, new(questData));
        var complete = CreateTransaction(SagaTransactionType.QuestCompleted, 2, new(questData));

        var state = stateMachine.Replay(new List<SagaTransaction>
        {
            accept, complete, CreateReversalTransaction(3, complete)
        });

        // Cross-arc gates must stop counting the completion...
        Assert.DoesNotContain("REVERSAL_QUEST", state.CompletedQuests);
        // ...and stage progress at completion time is unrecoverable, so the quest
        // does NOT return to ActiveQuests — the avatar re-accepts instead
        Assert.False(state.ActiveQuests.ContainsKey("REVERSAL_QUEST"));

        // The reversal makes the quest re-acceptable (ApplyQuestAccepted refuses
        // quests still present in CompletedQuests)
        var reaccepted = stateMachine.Replay(new List<SagaTransaction>
        {
            accept, complete, CreateReversalTransaction(3, complete),
            CreateTransaction(SagaTransactionType.QuestAccepted, 4, new(questData))
        });
        Assert.True(reaccepted.ActiveQuests.ContainsKey("REVERSAL_QUEST"));
    }

    [Fact]
    public void Replay_ReversedTraitAssigned_RemovesTraitFromTemplateAndLiveInstance()
    {
        var world = CreateWorldWithMerchant();
        var stateMachine = CreateStateMachine(world);

        var assign = CreateTransaction(SagaTransactionType.TraitAssigned, 2, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterRef] = "Merchant",
            [TransactionDataKeys.TraitType] = "Friendly"
        });

        // Sanity: without the reversal the trait is on the template list AND the live instance
        var stateWithoutReversal = stateMachine.Replay(new List<SagaTransaction>
        {
            CreateSpawnTransaction(1), assign
        });
        Assert.Contains("Friendly", stateWithoutReversal.CharacterTraits["Merchant"]);
        Assert.True(stateWithoutReversal.Characters[MerchantInstanceId.ToString()].Traits.ContainsKey("Friendly"));

        var state = stateMachine.Replay(new List<SagaTransaction>
        {
            CreateSpawnTransaction(1), assign, CreateReversalTransaction(3, assign)
        });

        Assert.DoesNotContain("Friendly", state.CharacterTraits["Merchant"]);
        Assert.False(state.Characters[MerchantInstanceId.ToString()].Traits.ContainsKey("Friendly"));
    }

    [Fact]
    public void Replay_ReversedQuestStageAdvanced_HasNoInverseFold_AndSkipsQuietly()
    {
        // Stage progress is unrecoverable from the single transaction, so
        // QuestStageAdvanced is a documented log-and-skip reversal: the fold
        // stands, the reversal stays as an audit record, and nothing quarantines
        var world = CreateWorldWithMerchant();
        var metrics = new RecordingSagaMetrics();
        var stateMachine = CreateStateMachine(world, metrics);

        var questData = new Dictionary<string, string> { [TransactionDataKeys.QuestRef] = "REVERSAL_QUEST" };
        var accept = CreateTransaction(SagaTransactionType.QuestAccepted, 1, new(questData));
        var advance = CreateTransaction(SagaTransactionType.QuestStageAdvanced, 2, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestRef] = "REVERSAL_QUEST",
            [TransactionDataKeys.NextStage] = "STAGE_2"
        });

        var state = stateMachine.Replay(new List<SagaTransaction>
        {
            accept, advance, CreateReversalTransaction(3, advance)
        });

        Assert.Equal("STAGE_2", state.ActiveQuests["REVERSAL_QUEST"].CurrentStage);
        Assert.Equal(0, metrics.UnknownTransactionTypeCount);
    }

    [Fact]
    public void Replay_TransactionReversed_NeverHitsUnknownTypeMetric()
    {
        var world = CreateWorldWithMerchant();
        var metrics = new RecordingSagaMetrics();
        var stateMachine = CreateStateMachine(world, metrics);

        var trade = CreateTransaction(SagaTransactionType.ItemTraded, 2, new Dictionary<string, string>
        {
            [TransactionDataKeys.CharacterInstanceId] = MerchantInstanceId.ToString(),
            [TransactionDataKeys.ItemRef] = "HealthPotion",
            [TransactionDataKeys.Quantity] = "1",
            [TransactionDataKeys.IsBuying] = "True",
            [TransactionDataKeys.PricePerItem] = "20",
            [TransactionDataKeys.TotalPrice] = "20"
        });

        // A reversal referencing a missing transaction must also skip quietly —
        // logged, but never routed through the unknown-type quarantine
        var danglingReversal = CreateTransaction(SagaTransactionType.TransactionReversed, 4, new Dictionary<string, string>
        {
            [TransactionDataKeys.ReversedTransactionId] = Guid.NewGuid().ToString(),
            [TransactionDataKeys.Reason] = "Avatar persistence failed: test",
            [TransactionDataKeys.OriginalType] = SagaTransactionType.ItemTraded.ToString()
        });

        stateMachine.Replay(new List<SagaTransaction>
        {
            CreateSpawnTransaction(1), trade, CreateReversalTransaction(3, trade), danglingReversal
        });

        Assert.Equal(0, metrics.UnknownTransactionTypeCount);
    }
}
