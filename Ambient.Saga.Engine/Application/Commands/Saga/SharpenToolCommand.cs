using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Commands.Saga;

/// <summary>
/// Command to sharpen a tool, restoring its condition to 100%.
/// Deducts currency from the avatar.
///
/// Side Effects:
/// - Creates ToolSharpened transaction
/// - Deducts currency cost from avatar
/// - Restores tool condition to 1.0 (100%)
/// - Persists updated avatar state
/// </summary>
public record SharpenToolCommand : IRequest<SagaCommandResult>
{
    /// <summary>
    /// Avatar whose tool is being sharpened
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga context (for transaction logging)
    /// </summary>
    public required string SagaArcRef { get; init; }

    /// <summary>
    /// Tool reference to sharpen
    /// </summary>
    public required string ToolRef { get; init; }

    /// <summary>
    /// Cost in currency to sharpen the tool
    /// </summary>
    public required int Cost { get; init; }

    /// <summary>
    /// Avatar entity for state updates and persistence
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
