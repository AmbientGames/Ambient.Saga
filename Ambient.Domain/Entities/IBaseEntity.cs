namespace Ambient.Domain.Entities;

/// <summary>
/// Defines the contract for a base entity.
/// </summary>
public interface IBaseEntity
{
    /// <summary>
    /// The unique identifier for the entity.
    /// </summary>
    Guid Id { get; set; }
}