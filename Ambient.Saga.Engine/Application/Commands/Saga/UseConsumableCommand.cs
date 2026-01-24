using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

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
public record UseConsumableCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar using the consumable
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga context (for transaction logging)
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Consumable item reference to use
    /// </summary>
    public required string ConsumableRef { get; init; }

    /// <summary>
    /// Avatar entity for state updates and persistence
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
