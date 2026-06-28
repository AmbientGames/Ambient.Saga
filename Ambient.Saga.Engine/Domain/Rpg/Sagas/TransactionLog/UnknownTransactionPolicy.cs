namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Controls how the state machine reacts to a transaction it cannot apply — either an
/// unrecognized <see cref="SagaTransactionType"/> enum value or an <c>Extension</c>
/// transaction whose <see cref="SagaTransaction.ExtensionTypeName"/> is not registered
/// with the <see cref="IExtensionTypeRegistry"/>.
///
/// Default is <see cref="Quarantine"/>: logs, increments a metric, and continues replay.
/// Tests and strict build pipelines can opt into <see cref="Throw"/> to surface drift
/// (e.g. an extension name renamed in one repo but not the other) as a hard failure.
/// </summary>
public enum UnknownTransactionPolicy
{
    Quarantine,
    Throw
}
