using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Tests.Rpg.Arcs;

/// <summary>
/// Anti-drift quarantine for transaction types the state machine does not
/// recognize: every new transaction type must either get a fold in
/// ApplyTransaction or be listed in StatelessTransactionTypes — anything else
/// trips the unknown-type metric (and throws under the strict policy). These
/// tests pin both sides: unknown values ARE counted without derailing the
/// replay, and stateless/retired types (LootAwarded's value stays reserved)
/// are exempt.
/// </summary>
public class ArcStateMachineQuarantineTests
{
    private sealed class RecordingArcMetrics : IArcMetrics
    {
        public List<int> UnknownTypeValues { get; } = new();

        public void IncrementSnapshotDeserializationFailure(Guid transactionId, long sequenceNumber) { }
        public void IncrementUnknownTransactionType(int transactionTypeValue) => UnknownTypeValues.Add(transactionTypeValue);
        public void IncrementQuarantinedExtension(string extensionTypeName) { }
    }

    private static ArcStateMachine CreateStateMachine(
        IArcMetrics? metrics = null,
        UnknownTransactionPolicy policy = UnknownTransactionPolicy.Quarantine)
    {
        var arc = new Arc
        {
            RefName = "QuarantineArc",
            DisplayName = "Quarantine Arc",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var world = new World();
        world.ArcLookup[arc.RefName] = arc;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger>();

        return new ArcStateMachine(
            arc,
            world.ArcTriggersLookup[arc.RefName],
            world,
            metrics: metrics,
            unknownPolicy: policy);
    }

    private static ArcTransaction CreateTransaction(ArcTransactionType type, long sequenceNumber, Dictionary<string, string>? data = null)
        => new()
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            AvatarId = Guid.NewGuid().ToString(),
            Status = TransactionStatus.Committed,
            LocalTimestamp = DateTime.UtcNow,
            SequenceNumber = sequenceNumber,
            Data = data ?? new Dictionary<string, string>()
        };

    [Fact]
    public void Replay_UnknownTransactionTypeValue_IsCounted_AndReplayContinues()
    {
        var metrics = new RecordingArcMetrics();
        var stateMachine = CreateStateMachine(metrics);

        var unknown = CreateTransaction((ArcTransactionType)9999, 1);
        var token = CreateTransaction(ArcTransactionType.QuestTokenAwarded, 2, new Dictionary<string, string>
        {
            [TransactionDataKeys.QuestTokenRef] = "token-after-unknown"
        });

        // Default policy quarantines: the unknown row is counted and skipped,
        // and everything after it still folds normally
        var state = stateMachine.Replay(new List<ArcTransaction> { unknown, token });

        Assert.Equal(new[] { 9999 }, metrics.UnknownTypeValues);
        Assert.Contains("token-after-unknown", state.AwardedQuestTokens);
    }

    [Fact]
    public void Replay_UnknownTransactionType_UnderThrowPolicy_Throws()
    {
        var stateMachine = CreateStateMachine(policy: UnknownTransactionPolicy.Throw);

        var unknown = CreateTransaction((ArcTransactionType)9999, 1);

        var ex = Assert.Throws<InvalidOperationException>(
            () => stateMachine.Replay(new List<ArcTransaction> { unknown }));
        Assert.Contains("9999", ex.Message);
    }

    [Fact]
    public void Replay_StatelessAndRetiredTypes_AreNeverQuarantined()
    {
        var metrics = new RecordingArcMetrics();
        var stateMachine = CreateStateMachine(metrics);

        var transactions = new List<ArcTransaction>
        {
            CreateTransaction(ArcTransactionType.BattleTurnExecuted, 1),
            CreateTransaction(ArcTransactionType.CurrencyChanged, 2),
            // Retired 2026-07-04 (corpse looting removed) — the enum value is
            // reserved so historical rows replay without tripping the metric
            CreateTransaction(ArcTransactionType.LootAwarded, 3)
        };

        stateMachine.Replay(transactions);

        Assert.Empty(metrics.UnknownTypeValues);
    }
}
