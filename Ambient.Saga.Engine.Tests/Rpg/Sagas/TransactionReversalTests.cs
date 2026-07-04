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
            Capabilities = new ItemCollection
            {
                Consumables = new[]
                {
                    new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 10 }
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
