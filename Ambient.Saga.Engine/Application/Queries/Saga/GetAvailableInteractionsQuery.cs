using Ambient.Domain;
using Ambient.Saga.Engine.Application.Results.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Queries.Saga;

/// <summary>
/// Comprehensive query to get all available interactions at the avatar's current position.
/// This is the primary query for client UI - returns everything the player can do right now.
///
/// Design Philosophy:
/// - Commands are simple (just create transactions)
/// - Queries are rich (tell you what's possible)
/// - This query is the "what can I do?" entry point
///
/// Usage:
/// 1. Player moves → UpdateAvatarPositionCommand (creates transactions)
/// 2. Client queries → GetAvailableInteractionsQuery (reads state)
/// 3. UI shows options → "Press E to talk to Merchant", "Click to loot chest"
/// 4. Player acts → StartDialogueCommand, TradeItemCommand, etc.
/// </summary>
public record GetAvailableInteractionsQuery : IRequest<AvailableInteractionsResult>
{
    /// <summary>
    /// Avatar checking for interactions
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Saga to check
    /// </summary>
    public required string SagaRef { get; init; }

    /// <summary>
    /// Avatar's current position (world coordinates - will be converted to Saga-relative)
    /// </summary>
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }

    /// <summary>
    /// Avatar data (for checking quest token requirements, etc.)
    /// </summary>
    public required AvatarBase Avatar { get; init; }
}
