using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to equip or unequip an item outside of battle.
/// Equipment changes persist to avatar's CombatProfile.
///
/// Side Effects:
/// - Creates EquipmentChanged transaction
/// - Updates avatar's CombatProfile (slot -> equipment mapping)
/// - Persists updated avatar state
/// </summary>
public record EquipItemOutsideBattleCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar changing equipment
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga context (for transaction logging)
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Equipment item reference to equip (null or empty to unequip slot)
    /// </summary>
    public string? EquipmentRef { get; init; }

    /// <summary>
    /// Slot to equip into (e.g., "Head", "Chest", "MainHand")
    /// </summary>
    public required string SlotRef { get; init; }

    /// <summary>
    /// Avatar entity for state updates and persistence
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
