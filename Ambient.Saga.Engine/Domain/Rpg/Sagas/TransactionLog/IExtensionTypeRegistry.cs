namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Read-only view of the set of <see cref="SagaTransaction.ExtensionTypeName"/> strings
/// that a domain package (e.g. Ambient.Core / Archimedea) has declared it knows how to replay.
///
/// Implementations typically wrap the same registry used to dispatch extension transactions
/// to their appliers — that way a rename in one place fails the registry check in the other,
/// instead of drifting into a silent replay skip.
/// </summary>
public interface IExtensionTypeRegistry
{
    bool IsKnown(string extensionTypeName);
}

/// <summary>
/// Registry used when no extension package is loaded — treats every extension name as unknown.
/// Combined with the default <see cref="UnknownTransactionPolicy.Quarantine"/> this matches
/// legacy behavior (continue replay) while making unknown extensions observable.
/// </summary>
public sealed class EmptyExtensionTypeRegistry : IExtensionTypeRegistry
{
    public static readonly EmptyExtensionTypeRegistry Instance = new();
    public bool IsKnown(string extensionTypeName) => false;
}
