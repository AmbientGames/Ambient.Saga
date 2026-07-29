namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// Read-only view of the set of <see cref="ArcTransaction.ExtensionTypeName"/> strings
/// that a domain package (e.g. the host game) has declared it knows how to replay.
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

/// <summary>
/// Default registry: accepts any self-identifying extension (non-empty name).
/// Extension transactions are host-layer data the RPG engine deliberately does not
/// fold into ArcState — with nothing registering names, the strict Empty registry
/// quarantine-logged EVERY claim on EVERY replay, permanently saturating the
/// anti-drift signal with false positives. The sync validator already rejects
/// extension transactions without a name; hosts that want strict name checking
/// pass an explicit registry.
/// </summary>
public sealed class PermissiveExtensionTypeRegistry : IExtensionTypeRegistry
{
    public static readonly PermissiveExtensionTypeRegistry Instance = new();
    public bool IsKnown(string extensionTypeName) => !string.IsNullOrEmpty(extensionTypeName);
}
