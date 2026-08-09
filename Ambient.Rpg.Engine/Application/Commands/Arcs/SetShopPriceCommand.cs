using Ambient.Rpg.Engine.Application.Results.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Commands.Arcs;

/// <summary>
/// Command for a Market shop's OWNER to list a per-item price on their arc. A listed
/// item sells to visitors at exactly this price instead of catalog x markup — the
/// shopkeeper's per-item knob (bread dear in the mountains, ore cheap). A price of 0
/// clears the listing.
///
/// Side Effects:
/// - Creates a ShopPriceSet transaction on the arc's shared instance
/// </summary>
public record SetShopPriceCommand : IRequest<ArcCommandResult>
{
    /// <summary>
    /// The avatar setting the price — must be the arc's owner.
    /// </summary>
    public required Guid AvatarId { get; init; }

    /// <summary>
    /// The Market arc whose listing is being set.
    /// </summary>
    public required string ArcRef { get; init; }

    /// <summary>
    /// The item being listed (a catalog ref; block refs may carry a variety suffix).
    /// </summary>
    public required string ItemRef { get; init; }

    /// <summary>
    /// The listing price in credits. 0 clears the listing.
    /// </summary>
    public required int PricePerItem { get; init; }
}
