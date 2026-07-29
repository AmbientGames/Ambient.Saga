using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command to trade items with a merchant character.
///
/// Side Effects:
/// - Creates ItemTraded transaction
/// - Transfers items between avatar and character inventories
/// - Updates currency balances
/// - Persists updated avatar state
/// </summary>
public record TradeItemCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// Avatar performing the trade
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// Arc containing the merchant
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// Merchant character instance
    /// </summary>
    public required Guid CharacterInstanceId { get; init; }

    /// <summary>
    /// Item being traded
    /// </summary>
    public required string ItemRef { get; init; }

    /// <summary>
    /// Quantity being traded
    /// </summary>
    public required int Quantity { get; init; }

    /// <summary>
    /// Trade direction (true = buying from merchant, false = selling to merchant)
    /// </summary>
    public required bool IsBuying { get; init; }

    /// <summary>
    /// Price per item (after discounts/markups)
    /// </summary>
    public required int PricePerItem { get; init; }

    /// <summary>
    /// Avatar entity performing the trade (for state updates and persistence)
    /// </summary>
    public required AvatarEntity Avatar { get; init; }
}
