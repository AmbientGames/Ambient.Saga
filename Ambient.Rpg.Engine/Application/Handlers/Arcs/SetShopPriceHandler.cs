using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Items;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Domain.Trade;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for SetShopPriceCommand: the Market owner's per-item listing. Owner-only,
/// Market-only, bounded by the same 10x ceiling that bounds shop trades — the same
/// rules ArcTransactionValidator.ValidateShopPriceSet enforces again at sync.
/// </summary>
internal sealed class SetShopPriceHandler : IRequestHandler<SetShopPriceCommand, ArcCommandResult>
{
    // Shares the trade ceiling's rationale: a listing beyond 10x the standard merchant
    // price is a mint attempt through the owner-revenue path, not a price.
    private const int MaxShopMarkup = 10;

    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public SetShopPriceHandler(IArcInstanceRepository instanceRepository, IWorld world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(SetShopPriceCommand command, CancellationToken ct)
    {
        if (!_world.ArcLookup.TryGetValue(command.ArcRef, out var arcTemplate))
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Arc '{command.ArcRef}' not found");
        }

        if (arcTemplate.Kind != ArcTradeRules.MarketKind)
        {
            return ArcCommandResult.Failure(Guid.Empty, "Listing prices exist only on Market arcs");
        }

        if (string.IsNullOrEmpty(arcTemplate.OwnerAvatarId)
            || command.AvatarId.ToString() != arcTemplate.OwnerAvatarId)
        {
            return ArcCommandResult.Failure(Guid.Empty, "Only the shop's owner may set listing prices");
        }

        if (string.IsNullOrEmpty(command.ItemRef))
        {
            return ArcCommandResult.Failure(Guid.Empty, "A listing needs an ItemRef");
        }

        if (command.PricePerItem < 0)
        {
            return ArcCommandResult.Failure(Guid.Empty, "A listing price cannot be negative (0 clears the listing)");
        }

        if (command.PricePerItem > 0)
        {
            var catalogItem = _world.TryGetTradeableByRefName(command.ItemRef);
            if (catalogItem != null && catalogItem.BaseValue != int.MaxValue)
            {
                var variant = ItemRefManager.VariantOf(command.ItemRef);
                var ceiling = new TradeEngine(_world).CalculateBuyPrice(catalogItem, isMerchant: true, variant: variant) * MaxShopMarkup;
                if (command.PricePerItem > ceiling)
                {
                    return ArcCommandResult.Failure(Guid.Empty,
                        $"Listing {command.PricePerItem} for '{command.ItemRef}' exceeds the {MaxShopMarkup}x markup ceiling {ceiling}");
                }
            }
        }

        var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

        var transaction = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.ShopPriceSet,
            AvatarId = command.AvatarId.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.ItemRef] = command.ItemRef,
                [TransactionDataKeys.PricePerItem] = command.PricePerItem.ToString()
            }
        };

        var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
            instance.InstanceId, new List<ArcTransaction> { transaction }, ct);

        return committed
            ? ArcCommandResult.Success(instance.InstanceId, new List<Guid> { transaction.TransactionId }, sequenceNumbers.LastOrDefault())
            : ArcCommandResult.Failure(instance.InstanceId, "Failed to commit the listing");
    }
}
