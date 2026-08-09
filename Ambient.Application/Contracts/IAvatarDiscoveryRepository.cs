namespace Ambient.Application.Contracts;

/// <summary>
/// Repository interface for AvatarDiscovery persistence.
/// AvatarDiscovery tracks when avatars discover entities (lore, achievements, arcs, etc.)
/// and their trigger history (cooldowns, interaction counts).
/// </summary>
public interface IAvatarDiscoveryRepository
{
    /// <summary>
    /// Finds a single discovery by composite key.
    /// Returns null if not found.
    /// </summary>
    Task<TDiscovery?> FindOneAsync<TDiscovery>(string avatarId, string entityType, string entityRef)
        where TDiscovery : class;

    /// <summary>
    /// Inserts a new discovery record.
    /// </summary>
    Task InsertAsync<TDiscovery>(TDiscovery discovery) where TDiscovery : class;

    /// <summary>
    /// Updates an existing discovery record.
    /// </summary>
    Task UpdateAsync<TDiscovery>(TDiscovery discovery) where TDiscovery : class;

    /// <summary>
    /// Checks if a discovery record exists.
    /// </summary>
    Task<bool> ExistsAsync(string avatarId, string entityType, string entityRef);

    /// <summary>
    /// Gets all discoveries for a specific avatar.
    /// </summary>
    Task<List<TDiscovery>> GetByAvatarIdAsync<TDiscovery>(string avatarId) where TDiscovery : class;
}
