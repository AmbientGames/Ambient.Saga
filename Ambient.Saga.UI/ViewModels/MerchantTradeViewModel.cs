using Ambient.Domain;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Domain.Rpg.Trade;
using Ambient.Saga.Engine.Domain.Rpg.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using System.Collections.ObjectModel;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Presentation.WindowsUI.RpgControls.ViewModels;

public partial class MerchantTradeViewModel : ObservableObject
{
    // Events for notifying the host about state changes
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler<string>? ActivityMessageGenerated;
    public event Action<string, int>? OwnerRevenueEarned;

    private readonly SagaInteractionContext _context;
    private readonly IMediator _mediator;
    private TradeEngine? _tradeEngine;  // Still used for price calculation

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TradeInventory))]
    private string _selectedTradeCategory = "Equipment";

    // MerchantTradeViewModel is only instantiated when OpenMerchantTrade dialogue action fires,
    // so we can assume it's always a merchant interaction
    public bool ShowBuySellToggle => true;
    public bool IsMerchant => true;

    public string CurrencyName => _context?.CurrencyName ?? "Coin";
    public string PluralCurrencyName => _context?.PluralCurrencyName ?? "Coins";
    public AvatarBase? PlayerAvatar => _context?.AvatarEntity;  // Implicit upcast to AvatarBase

    private string _tradeMode = "Buy"; // "Buy" or "Sell"

    public string TradeMode
    {
        get => _tradeMode;
        set
        {
            if (SetProperty(ref _tradeMode, value))
            {
                OnPropertyChanged(nameof(TradeInventory));
                RefreshCategories();
            }
        }
    }

    public ObservableCollection<TradeItem> TradeInventory => _tradeMode == "Buy" ? GetMerchantInventory() : GetAvatarInventory();

    // Category availability properties
    public bool HasEquipment => GetCategoryItemCount("Equipment") > 0;
    public bool HasConsumables => GetCategoryItemCount("Consumables") > 0;
    public bool HasBlocks => GetCategoryItemCount("Blocks") > 0;
    public bool HasTools => GetCategoryItemCount("Tools") > 0;
    public bool HasSpells => GetCategoryItemCount("Spells") > 0;
    public bool HasPotentialLoot => HasEquipment || HasConsumables || HasBlocks || HasTools || HasSpells;

    // Get list of available categories
    public ObservableCollection<string> AvailableCategories
    {
        get
        {
            var categories = new ObservableCollection<string>();
            if (HasEquipment) categories.Add("Equipment");
            if (HasConsumables) categories.Add("Consumables");
            if (HasBlocks) categories.Add("Blocks");
            if (HasTools) categories.Add("Tools");
            if (HasSpells) categories.Add("Spells");
            return categories;
        }
    }

    // Should we show the category selector? (only if more than one category has items)
    public bool ShowCategorySelector => AvailableCategories.Count > 1;

    public MerchantTradeViewModel(SagaInteractionContext context, IMediator mediator)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

        if (_context.World != null)
        {
            _tradeEngine = new TradeEngine(_context.World);
        }
    }

    // Call this when merchant changes or trade mode changes to auto-select category
    public void RefreshCategories()
    {
        // Recreate trade engine if world changed
        if (_context.World != null && _tradeEngine == null)
        {
            _tradeEngine = new TradeEngine(_context.World);
        }

        // Debug: Log category availability
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] RefreshCategories - Mode: {_tradeMode}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM]   HasEquipment: {HasEquipment}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM]   HasConsumables: {HasConsumables}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM]   HasBlocks: {HasBlocks}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM]   HasTools: {HasTools}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM]   HasSpells: {HasSpells}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM]   HasPotentialLoot: {HasPotentialLoot}");

        OnPropertyChanged(nameof(HasEquipment));
        OnPropertyChanged(nameof(HasConsumables));
        OnPropertyChanged(nameof(HasBlocks));
        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(HasSpells));
        OnPropertyChanged(nameof(AvailableCategories));
        OnPropertyChanged(nameof(ShowCategorySelector));
        OnPropertyChanged(nameof(PlayerAvatar));
        OnPropertyChanged(nameof(CurrencyName));
        OnPropertyChanged(nameof(PluralCurrencyName));

        // Auto-select the first available category if current selection is invalid
        var available = AvailableCategories;
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM]   Available categories: {string.Join(", ", available)}");

        if (available.Count > 0)
        {
            if (!available.Contains(SelectedTradeCategory))
            {
                SelectedTradeCategory = available[0];
            }
            else
            {
                // Force refresh of inventory even if category didn't change
                OnPropertyChanged(nameof(TradeInventory));
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM]   WARNING: No tradeable categories available!");
        }
    }

    private int GetCategoryItemCount(string category)
    {
        if (_tradeEngine == null) return 0;

        var inventory = _tradeMode == "Buy"
            ? _context.ActiveCharacter?.Interactable?.Loot  // Merchant inventory for buying
            : _context.AvatarEntity?.Capabilities;  // Avatar inventory for selling

        return _tradeEngine.GetCategoryItemCount(inventory, category);
    }

    private ObservableCollection<TradeItem> GetMerchantInventory()
    {
        var items = new ObservableCollection<TradeItem>();

        if (_tradeEngine == null || _context.ActiveCharacter?.Interactable?.Loot == null)
            return items;

        // Get character traits for pricing (asynchronously, but we'll use cached value if available)
        var characterTraits = GetCharacterTraitsAsync().Result;  // Sync over async for UI binding

        var tradeItems = _tradeEngine.GetAvailableItems(_context.ActiveCharacter.Interactable.Loot, SelectedTradeCategory, isBuying: true, characterTraits);
        foreach (var item in tradeItems)
        {
            items.Add(new TradeItem(item.Item, item.Price, item.Quantity, item.Condition));
        }

        return items;
    }

    private async Task<List<string>?> GetCharacterTraitsAsync()
    {
        try
        {
            if (_context.CurrentSagaRef == null || _context.ActiveCharacter == null)
                return null;

            // Query SagaState for character traits
            var query = new Ambient.Saga.Engine.Application.Queries.Saga.GetSagaStateQuery
            {
                AvatarId = _context.AvatarId,
                SagaRef = _context.CurrentSagaRef
            };

            var sagaState = await _mediator.Send(query);

            if (sagaState != null && sagaState.CharacterTraits.TryGetValue(_context.ActiveCharacter.RefName, out var traits))
            {
                return traits;
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MerchantTrade] Error fetching character traits: {ex.Message}");
            return null;
        }
    }

    private ObservableCollection<TradeItem> GetAvatarInventory()
    {
        var items = new ObservableCollection<TradeItem>();

        if (_tradeEngine == null || _context.AvatarEntity?.Capabilities == null)
            return items;

        var tradeItems = _tradeEngine.GetAvailableItems(_context.AvatarEntity.Capabilities, SelectedTradeCategory, isBuying: false);
        foreach (var item in tradeItems)
        {
            items.Add(new TradeItem(item.Item, item.Price, item.Quantity, item.Condition));
        }

        return items;
    }

    [RelayCommand]
    private async Task BuyItemAsync(TradeItem tradeItem)
    {
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] === BUY CLICKED ===");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] Item: {tradeItem?.Item?.RefName ?? "null"}, Price: {tradeItem?.Price ?? 0}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] AvatarEntity: {(_context.AvatarEntity != null ? "present" : "NULL")}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] CurrentSagaRef: {_context.CurrentSagaRef ?? "NULL"}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] CurrentCharacterInstanceId: {_context.CurrentCharacterInstanceId?.ToString() ?? "NULL"}");

        if (_context.AvatarEntity == null || _context.CurrentSagaRef == null || _context.CurrentCharacterInstanceId == null)
        {
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] BUY ABORTED - missing context data");
            StatusMessageChanged?.Invoke(this, "Cannot complete trade - missing avatar or character data");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] Avatar credits before: {_context.AvatarEntity.Stats?.Credits ?? 0}");

        try
        {
            // Send CQRS command - Saga Engine handles persistence and returns updated avatar
            var command = new TradeItemCommand
            {
                AvatarId = _context.AvatarId,
                SagaArcRef = _context.CurrentSagaRef,
                CharacterInstanceId = _context.CurrentCharacterInstanceId.Value,
                ItemRef = tradeItem.Item.RefName,
                Quantity = 1,  // Buy one at a time
                IsBuying = true,
                PricePerItem = tradeItem.Price,
                Avatar = _context.AvatarEntity
            };

            System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] Sending TradeItemCommand...");
            var result = await _mediator.Send(command);
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] Result: Successful={result.Successful}, Error={result.ErrorMessage}");

            if (!result.Successful)
            {
                StatusMessageChanged?.Invoke(this, result.ErrorMessage ?? "Trade failed");
                return;
            }

            var message = $"Bought {tradeItem.Item.DisplayName} for {tradeItem.Price} {PluralCurrencyName}";
            ActivityMessageGenerated?.Invoke(this, message);
            StatusMessageChanged?.Invoke(this, "Trade successful!");

            // Signal owner revenue if this was a purchase from an avatar-owned merchant
            if (result.Data.TryGetValue(TransactionDataKeys.OwnerAvatarId, out var ownerIdObj) && ownerIdObj is string ownerId
                && result.Data.TryGetValue(TransactionDataKeys.OwnerRevenue, out var revenueObj) && revenueObj is int revenue)
            {
                OwnerRevenueEarned?.Invoke(ownerId, revenue);
            }

            // Use the updated avatar returned by Saga Engine (self-contained)
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] UpdatedAvatar: {(result.UpdatedAvatar != null ? "present" : "NULL")}");
            if (result.UpdatedAvatar != null)
            {
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] Avatar credits after: {result.UpdatedAvatar.Stats?.Credits ?? 0}");
                _context.AvatarEntity = result.UpdatedAvatar;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] WARNING: No updated avatar returned!");
            }

            // Refresh UI to reflect updated inventory and credits
            OnPropertyChanged(nameof(PlayerAvatar));
            OnPropertyChanged(nameof(TradeInventory));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] EXCEPTION: {ex.Message}");
            StatusMessageChanged?.Invoke(this, $"Trade error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SellItemAsync(TradeItem tradeItem)
    {
        if (_context.AvatarEntity == null || _context.CurrentSagaRef == null || _context.CurrentCharacterInstanceId == null)
        {
            StatusMessageChanged?.Invoke(this, "Cannot complete trade - missing avatar or character data");
            return;
        }

        try
        {
            // Send CQRS command - Saga Engine handles persistence and returns updated avatar
            var command = new TradeItemCommand
            {
                AvatarId = _context.AvatarId,
                SagaArcRef = _context.CurrentSagaRef,
                CharacterInstanceId = _context.CurrentCharacterInstanceId.Value,
                ItemRef = tradeItem.Item.RefName,
                Quantity = 1,  // Sell one at a time
                IsBuying = false,
                PricePerItem = tradeItem.Price,
                Avatar = _context.AvatarEntity
            };

            var result = await _mediator.Send(command);

            if (!result.Successful)
            {
                StatusMessageChanged?.Invoke(this, result.ErrorMessage ?? "Trade failed");
                return;
            }

            var message = $"Sold {tradeItem.Item.DisplayName} for {tradeItem.Price} {PluralCurrencyName}";
            ActivityMessageGenerated?.Invoke(this, message);
            StatusMessageChanged?.Invoke(this, "Trade successful!");

            // Use the updated avatar returned by Saga Engine (self-contained)
            if (result.UpdatedAvatar != null)
            {
                _context.AvatarEntity = result.UpdatedAvatar;
            }

            // Refresh UI to reflect updated inventory and credits
            OnPropertyChanged(nameof(PlayerAvatar));
            OnPropertyChanged(nameof(TradeInventory));
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this, $"Trade error: {ex.Message}");
        }
    }

}
