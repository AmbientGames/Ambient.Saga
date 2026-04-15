namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Observability hook for the event sourcing layer. Counters fire on conditions that
/// were previously silent (snapshot deserialization failures, unknown transaction
/// types, quarantined extension transactions) so operators can see drift instead
/// of discovering it as a performance or correctness regression later.
/// </summary>
public interface ISagaMetrics
{
    void IncrementSnapshotDeserializationFailure(Guid transactionId, long sequenceNumber);

    void IncrementUnknownTransactionType(int transactionTypeValue);

    void IncrementQuarantinedExtension(string extensionTypeName);
}

public sealed class NullSagaMetrics : ISagaMetrics
{
    public static readonly NullSagaMetrics Instance = new();

    public void IncrementSnapshotDeserializationFailure(Guid transactionId, long sequenceNumber) { }
    public void IncrementUnknownTransactionType(int transactionTypeValue) { }
    public void IncrementQuarantinedExtension(string extensionTypeName) { }
}
