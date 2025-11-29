using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to loot a defeated character's inventory.
///
/// Side Effects:
/// - Creates LootAwarded transaction
/// - Clears character's inventory
/// - Marks character as looted
/// - Awards items to avatar inventory
/// - Persists updated avatar state
/// </summary>
public record LootCharacterCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar performing the looting
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga containing the character
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Character instance being looted
    /// </summary>
    public required Guid CharacterInstanceId { get; init; }

    /// <summary>
    /// Avatar entity performing the looting (for state updates and persistence)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
