using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to use a consumable item outside of battle.
/// Applies consumable effects to avatar's stats and decrements quantity.
///
/// Side Effects:
/// - Creates ConsumableUsed transaction
/// - Applies consumable effects (health/stamina/mana restoration, status effects, etc.)
/// - Decrements consumable quantity (or removes if quantity becomes 0)
/// - Persists updated avatar state
/// </summary>
public record UseConsumableCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar using the consumable
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc context (for transaction logging)
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Consumable item reference to use
    /// </summary>
    public required string ConsumableRef { get; init; }

    /// <summary>
    /// Avatar entity for state updates and persistence
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
