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

    // ---- Render-path caches (audit D9) -------------------------------------------------
    // TradeInventory and the category counts are evaluated several times per frame by
    // MerchantTradeModal. They must never hit the mediator (saga replay) on the render
    // path, so the saga-derived state is loaded asynchronously into these fields
    // (RefreshSagaStateAsync) and the computed inventory list is cached until something
    // relevant changes (mode/category switch, completed trade, saga refresh).

    /// <summary>Saga-replayed live merchant/cache inventory; null → fall back to the character template's Loot.</summary>
    private ItemCollection? _sagaMerchantInventory;

    /// <summary>Saga-replayed character traits (drive merchant pricing); null when unavailable.</summary>
    private List<string>? _characterTraits;

    /// <summary>Cached TradeInventory result; null means "rebuild on next read".</summary>
    private ObservableCollection<TradeItem>? _tradeInventoryCache;

    partial void OnSelectedTradeCategoryChanged(string value) => _tradeInventoryCache = null;

    private void InvalidateTradeInventory()
    {
        _tradeInventoryCache = null;
        OnPropertyChanged(nameof(TradeInventory));
    }

    /// <summary>
    /// True when this VM is driving a cache interaction (geocache / remnant Loot / etc.) rather than
    /// a paid merchant. Hides money/price UI, relabels buttons (Buy→Take, Sell→Deposit), and sends
    /// trade transactions with PricePerItem=0.
    /// </summary>
    public bool IsCache { get; }

    /// <summary>
    /// True when this VM is driving a VICTORY-LOOT collect: the defeated character's remaining
    /// Loot, free-takeable by its victor. Free like a cache, but TAKE ONLY — the engine rejects
    /// zero-price sells to a corpse, so the Sell/Deposit side is hidden entirely. Labeled
    /// distinctly from caches so a defeat drop never reads as "Geocache"/"Remnant Loot".
    /// </summary>
    public bool IsVictoryLoot { get; }

    /// <summary>Free-take modes: zero-price trades, no money UI.</summary>
    public bool IsFreeTake => IsCache || IsVictoryLoot;

    public bool ShowBuySellToggle => !IsVictoryLoot;
    public bool IsMerchant => !IsFreeTake;

    public string CurrencyName => _context?.CurrencyName ?? "Coin";
    public string PluralCurrencyName => _context?.PluralCurrencyName ?? "Coins";
    public AvatarBase? Avatar => _context?.AvatarEntity;  // Implicit upcast to AvatarBase

    // UI text + visibility that varies by mode
    public bool ShowMoneyBar => !IsFreeTake;
    public bool ShowPrices => !IsFreeTake;
    public string HeaderSubtitle => IsVictoryLoot ? "- Defeated" : IsCache ? "- Cache" : "- Merchant";
    public string BuyModeLabel => IsVictoryLoot ? "Spoils of Victory" : IsCache ? "Take from Cache" : "Buy from Merchant";
    public string SellModeLabel => IsCache ? "Deposit Items" : "Sell your Items";
    public string ItemBuyLabel => IsFreeTake ? "Take" : "Buy";
    public string ItemSellLabel => IsCache ? "Deposit" : "Sell";
    public string CloseLabel => IsFreeTake ? "Close" : "Leave Shop";
    public string EmptyBuyText => IsVictoryLoot
        ? "Nothing left to take"
        : IsCache ? "Cache is empty" : "Merchant has no items in this category";
    public string EmptySellText => IsCache ? "You have nothing to deposit in this category" : "You have no items to sell in this category";

    private string _tradeMode = "Buy"; // "Buy" or "Sell"

    public string TradeMode
    {
        get => _tradeMode;
        set
        {
            if (SetProperty(ref _tradeMode, value))
            {
                InvalidateTradeInventory();
                RefreshCategories();
            }
        }
    }

    public ObservableCollection<TradeItem> TradeInventory =>
        _tradeInventoryCache ??= _tradeMode == "Buy" ? GetMerchantInventory() : GetAvatarInventory();

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

    public MerchantTradeViewModel(SagaInteractionContext context, IMediator mediator, bool isCache = false, bool isVictoryLoot = false)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        IsCache = isCache;
        IsVictoryLoot = isVictoryLoot;

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
        OnPropertyChanged(nameof(Avatar));
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
                InvalidateTradeInventory();
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
            ? GetMerchantInventorySource()      // Merchant loot or cache's live stock
            : _context.AvatarEntity?.Capabilities;

        return _tradeEngine.GetCategoryItemCount(inventory, category);
    }

    /// <summary>
    /// Live inventory source. The saga-replayed CurrentInventory (same source for every arc
    /// kind — geocache, player shop, remnant Loot — so deposits/withdrawals via ItemTraded
    /// mutations become visible) is loaded asynchronously by <see cref="RefreshSagaStateAsync"/>;
    /// until it lands (or when no saga instance exists — authored arcs without a recorded
    /// CharacterSpawned) this falls back to the character template's Interactable.Loot.
    /// Never queries the mediator: this sits on the render path.
    /// </summary>
    private ItemCollection? GetMerchantInventorySource()
    {
        return _sagaMerchantInventory ?? _context.ActiveCharacter?.Interactable?.Loot;
    }

    /// <summary>
    /// Replays the saga instance ONCE (off the render path) and caches the character's live
    /// inventory and traits. Call when the trade UI opens and after each completed trade —
    /// never per frame. Raises property changes so the next rendered frame picks it up.
    /// </summary>
    public async Task RefreshSagaStateAsync()
    {
        if (_context.CurrentSagaRef == null)
            return;

        try
        {
            var query = new Ambient.Saga.Engine.Application.Queries.Saga.GetSagaStateQuery
            {
                AvatarId = _context.AvatarId,
                SagaRef = _context.CurrentSagaRef,
            };
            var sagaState = await _mediator.Send(query);

            if (sagaState != null && _context.CurrentCharacterInstanceId != null &&
                sagaState.Characters.TryGetValue(_context.CurrentCharacterInstanceId.Value.ToString(), out var characterState))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MerchantTradeVM] Saga inventory hit for '{_context.CurrentSagaRef}' " +
                    $"(CharacterInstanceId={_context.CurrentCharacterInstanceId.Value})");
                _sagaMerchantInventory = characterState.CurrentInventory;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MerchantTradeVM] Saga state had no character for CharacterInstanceId={_context.CurrentCharacterInstanceId?.ToString() ?? "null"} " +
                    $"in '{_context.CurrentSagaRef}'. Falling back to template Loot.");
                _sagaMerchantInventory = null;
            }

            _characterTraits = sagaState != null && _context.ActiveCharacter != null &&
                sagaState.CharacterTraits.TryGetValue(_context.ActiveCharacter.RefName, out var traits)
                    ? traits
                    : null;

            // Categories may have appeared/disappeared with the live inventory.
            RefreshCategories();
            InvalidateTradeInventory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] Failed to refresh saga state: {ex.Message}");
        }
    }

    private ObservableCollection<TradeItem> GetMerchantInventory()
    {
        var items = new ObservableCollection<TradeItem>();
        if (_tradeEngine == null) return items;

        var source = GetMerchantInventorySource();
        if (source == null) return items;

        // Traits drive merchant pricing; free-take modes (caches, victory loot) don't care.
        // Cached by RefreshSagaStateAsync — no sync-over-async on the render path.
        var characterTraits = IsFreeTake ? null : _characterTraits;

        var tradeItems = _tradeEngine.GetAvailableItems(source, SelectedTradeCategory, isBuying: true, characterTraits);
        foreach (var item in tradeItems)
        {
            var price = IsFreeTake ? 0 : item.Price;
            items.Add(new TradeItem(item.Item, price, item.Quantity, item.Condition, item.ItemRef));
        }

        return items;
    }

    private ObservableCollection<TradeItem> GetAvatarInventory()
    {
        var items = new ObservableCollection<TradeItem>();

        if (_tradeEngine == null || _context.AvatarEntity?.Capabilities == null)
            return items;

        var tradeItems = _tradeEngine.GetAvailableItems(_context.AvatarEntity.Capabilities, SelectedTradeCategory, isBuying: false);
        foreach (var item in tradeItems)
        {
            items.Add(new TradeItem(item.Item, item.Price, item.Quantity, item.Condition, item.ItemRef));
        }

        return items;
    }

    /// <summary>
    /// The display name to show for a trade row. Blocks resolve through the block provider by
    /// their ref (so the provider decides the label); everything else uses the item's own name.
    /// </summary>
    public string GetDisplayName(TradeItem item)
        => _context.World?.BlockProvider?.GetDisplayName(item.ItemRef) ?? item.Item.DisplayName;

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
                ItemRef = tradeItem.ItemRef,
                Quantity = 1,  // Buy one at a time
                IsBuying = true,
                PricePerItem = IsFreeTake ? 0 : tradeItem.Price,
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

            var message = IsVictoryLoot
                ? $"Claimed {tradeItem.Item.DisplayName} as victory loot"
                : IsCache
                    ? $"Took {tradeItem.Item.DisplayName} from cache"
                    : $"Bought {tradeItem.Item.DisplayName} for {tradeItem.Price} {PluralCurrencyName}";
            ActivityMessageGenerated?.Invoke(this, message);
            StatusMessageChanged?.Invoke(this, IsFreeTake ? "Taken." : "Trade successful!");

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

            // Refresh UI to reflect updated inventory and credits (re-replays the saga
            // state once, off the render path).
            OnPropertyChanged(nameof(Avatar));
            InvalidateTradeInventory();
            await RefreshSagaStateAsync();
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
        // Victory loot is take-only (the toggle is hidden; this is a belt-and-braces
        // guard — the engine rejects zero-price sells to a dead character anyway).
        if (IsVictoryLoot)
        {
            StatusMessageChanged?.Invoke(this, "You cannot leave items on a defeated enemy");
            return;
        }

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
                ItemRef = tradeItem.ItemRef,
                Quantity = 1,  // Sell one at a time
                IsBuying = false,
                PricePerItem = IsCache ? 0 : tradeItem.Price,
                Avatar = _context.AvatarEntity
            };

            var result = await _mediator.Send(command);

            if (!result.Successful)
            {
                StatusMessageChanged?.Invoke(this, result.ErrorMessage ?? "Trade failed");
                return;
            }

            var message = IsCache
                ? $"Deposited {tradeItem.Item.DisplayName} in cache"
                : $"Sold {tradeItem.Item.DisplayName} for {tradeItem.Price} {PluralCurrencyName}";
            ActivityMessageGenerated?.Invoke(this, message);
            StatusMessageChanged?.Invoke(this, IsCache ? "Deposited." : "Trade successful!");

            // Use the updated avatar returned by Saga Engine (self-contained)
            if (result.UpdatedAvatar != null)
            {
                _context.AvatarEntity = result.UpdatedAvatar;
            }

            // Refresh UI to reflect updated inventory and credits (re-replays the saga
            // state once, off the render path).
            OnPropertyChanged(nameof(Avatar));
            InvalidateTradeInventory();
            await RefreshSagaStateAsync();
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this, $"Trade error: {ex.Message}");
        }
    }

}
