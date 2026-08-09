using Ambient.Domain;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Domain.Arcs;
using Ambient.Rpg.Engine.Domain.Trade;
using Ambient.Rpg.Engine.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using System.Collections.ObjectModel;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Presentation.WindowsUI.RpgControls.ViewModels;

public partial class MerchantTradeViewModel : ObservableObject
{
    // Events for notifying the host about state changes
    public event EventHandler<string>? StatusMessageChanged;
    public event EventHandler<string>? ActivityMessageGenerated;
    public event Action<string, int>? OwnerRevenueEarned;

    private readonly ArcInteractionContext _context;
    private readonly IMediator _mediator;
    private TradeEngine? _tradeEngine;  // Still used for price calculation

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TradeInventory))]
    private string _selectedTradeCategory = "Equipment";

    // ---- Render-path caches (audit D9) -------------------------------------------------
    // TradeInventory and the category counts are evaluated several times per frame by
    // MerchantTradeModal. They must never hit the mediator (arc replay) on the render
    // path, so the arc-derived state is loaded asynchronously into these fields
    // (RefreshArcStateAsync) and the computed inventory list is cached until something
    // relevant changes (mode/category switch, completed trade, arc refresh).

    /// <summary>Arc-replayed live merchant/cache inventory; null → fall back to the character template's Loot.</summary>
    private ItemCollection? _arcMerchantInventory;

    /// <summary>Arc-replayed character traits (drive merchant pricing); null when unavailable.</summary>
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
    /// True when this VM is driving a BATTLE-LOOT collect: the victor-only remains arc
    /// holding a defeated character's drop. Free like a cache, but TAKE ONLY — remains
    /// accept no deposits — and labeled distinctly so a victory drop never reads as
    /// "Geocache"/"Cache".
    /// </summary>
    public bool IsVictoryLoot { get; }

    /// <summary>
    /// The source arc kind ("Market", "GeoCache", "RemnantLoot", "BattleLoot"; empty for
    /// plain merchants). Death remains (RemnantLoot) are take-only like battle loot but
    /// labeled as remains.
    /// </summary>
    public string ArcKind { get; }

    /// <summary>Death remains: anyone takes for free, nobody deposits.</summary>
    public bool IsRemnant => ArcKind == "RemnantLoot";

    /// <summary>Free-take modes: zero-price trades, no money UI.</summary>
    public bool IsFreeTake => IsCache || IsVictoryLoot;

    public bool ShowBuySellToggle => !IsVictoryLoot && !IsRemnant;
    public bool IsMerchant => !IsFreeTake;

    public string CurrencyName => _context?.CurrencyName ?? "Coin";
    public string PluralCurrencyName => _context?.PluralCurrencyName ?? "Coins";
    public AvatarBase? Avatar => _context?.AvatarEntity;  // Implicit upcast to AvatarBase

    // UI text + visibility that varies by mode
    public bool ShowMoneyBar => !IsFreeTake;
    public bool ShowPrices => !IsFreeTake;
    public string HeaderSubtitle => IsVictoryLoot ? "- Defeated" : IsRemnant ? "- Remains" : IsCache ? "- Cache" : "- Merchant";
    public string BuyModeLabel => IsVictoryLoot ? "Spoils of Victory" : IsRemnant ? "Take from Remains" : IsCache ? "Take from Cache" : "Buy from Merchant";
    public string SellModeLabel => IsCache ? "Deposit Items" : "Sell your Items";
    public string ItemBuyLabel => IsFreeTake ? "Take" : "Buy";
    public string ItemSellLabel => IsCache ? "Deposit" : "Sell";
    public string CloseLabel => IsFreeTake ? "Close" : "Leave Shop";
    public string EmptyBuyText => IsVictoryLoot || IsRemnant
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

    public MerchantTradeViewModel(ArcInteractionContext context, IMediator mediator, bool isCache = false, bool isVictoryLoot = false, string arcKind = "")
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        IsCache = isCache;
        IsVictoryLoot = isVictoryLoot;
        ArcKind = arcKind ?? string.Empty;

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
    /// Live inventory source. The arc-replayed CurrentInventory (same source for every arc
    /// kind — geocache, player shop, remnant Loot — so deposits/withdrawals via ItemTraded
    /// mutations become visible) is loaded asynchronously by <see cref="RefreshArcStateAsync"/>;
    /// until it lands (or when no arc instance exists — authored arcs without a recorded
    /// CharacterSpawned) this falls back to the character template's Interactable.Loot.
    /// Never queries the mediator: this sits on the render path.
    /// </summary>
    private ItemCollection? GetMerchantInventorySource()
    {
        return _arcMerchantInventory ?? _context.ActiveCharacter?.Interactable?.Loot;
    }

    /// <summary>
    /// Replays the arc instance ONCE (off the render path) and caches the character's live
    /// inventory and traits. Call when the trade UI opens and after each completed trade —
    /// never per frame. Raises property changes so the next rendered frame picks it up.
    /// </summary>
    public async Task RefreshArcStateAsync()
    {
        if (_context.CurrentArcRef == null)
            return;

        try
        {
            var query = new Ambient.Rpg.Engine.Application.Queries.Arcs.GetArcStateQuery
            {
                AvatarId = _context.AvatarId,
                ArcRef = _context.CurrentArcRef,
            };
            var arcState = await _mediator.Send(query);

            if (arcState != null && _context.CurrentCharacterInstanceId != null &&
                arcState.Characters.TryGetValue(_context.CurrentCharacterInstanceId.Value.ToString(), out var characterState))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MerchantTradeVM] Arc inventory hit for '{_context.CurrentArcRef}' " +
                    $"(CharacterInstanceId={_context.CurrentCharacterInstanceId.Value})");
                _arcMerchantInventory = characterState.CurrentInventory;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MerchantTradeVM] Arc state had no character for CharacterInstanceId={_context.CurrentCharacterInstanceId?.ToString() ?? "null"} " +
                    $"in '{_context.CurrentArcRef}'. Falling back to template Loot.");
                _arcMerchantInventory = null;
            }

            _characterTraits = arcState != null && _context.ActiveCharacter != null &&
                arcState.CharacterTraits.TryGetValue(_context.ActiveCharacter.RefName, out var traits)
                    ? traits
                    : null;

            // The owner's per-item listings (visitors pay exactly the listed price).
            _shopPrices = arcState?.ShopPrices;

            // Categories may have appeared/disappeared with the live inventory.
            RefreshCategories();
            InvalidateTradeInventory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] Failed to refresh arc state: {ex.Message}");
        }
    }

    private ObservableCollection<TradeItem> GetMerchantInventory()
    {
        var items = new ObservableCollection<TradeItem>();
        if (_tradeEngine == null) return items;

        var source = GetMerchantInventorySource();
        if (source == null) return items;

        // Traits drive merchant pricing; free-take modes (caches, victory loot) don't care.
        // Cached by RefreshArcStateAsync — no sync-over-async on the render path.
        var characterTraits = IsFreeTake ? null : _characterTraits;

        var tradeItems = _tradeEngine.GetAvailableItems(source, SelectedTradeCategory, isBuying: true, characterTraits);
        foreach (var item in tradeItems)
        {
            var price = IsFreeTake ? 0 : item.Price;

            // A shop listing REPLACES the catalog price for visitors — the shopkeeper's
            // per-item knob. (The handler enforces the same price server-side.)
            if (!IsFreeTake && _shopPrices != null
                && _shopPrices.TryGetValue(item.ItemRef ?? item.Item.RefName, out var listed))
            {
                price = listed;
            }

            items.Add(new TradeItem(item.Item, price, item.Quantity, item.Condition, item.ItemRef));
        }

        return items;
    }

    private Dictionary<string, int>? _shopPrices;

    /// <summary>
    /// True when this view is a Market shop seen by its OWNER — the only place per-item
    /// listing prices can be set.
    /// </summary>
    public bool CanSetListingPrices => IsCache && ArcKind == Ambient.Rpg.Engine.Domain.Trade.ArcTradeRules.MarketKind;

    /// <summary>The item's current listing price, or null when unlisted.</summary>
    public int? GetListingPrice(TradeItem item)
        => _shopPrices != null && _shopPrices.TryGetValue(ListingKey(item), out var p) ? p : null;

    private static string ListingKey(TradeItem item) => item.ItemRef ?? item.Item.RefName;

    /// <summary>
    /// Sets (price &gt; 0) or clears (price == 0) the owner's listing for an item and
    /// refreshes the cached state so the next frame shows it.
    /// </summary>
    public async Task SetListingPriceAsync(TradeItem item, int price)
    {
        if (_context.CurrentArcRef == null)
            return;

        var result = await _mediator.Send(new SetShopPriceCommand
        {
            AvatarId = _context.AvatarId,
            ArcRef = _context.CurrentArcRef,
            ItemRef = ListingKey(item),
            PricePerItem = price
        });

        if (result.Successful)
        {
            await RefreshArcStateAsync();
            StatusMessageChanged?.Invoke(this, price > 0 ? $"Listed at {price}." : "Listing cleared.");
        }
        else
        {
            StatusMessageChanged?.Invoke(this, result.ErrorMessage ?? "Could not set the listing");
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
            items.Add(new TradeItem(item.Item, item.Price, item.Quantity, item.Condition, item.ItemRef));
        }

        return items;
    }

    /// <summary>
    /// The display name to show for a trade row. Blocks resolve through the block provider by
    /// their ref (so the provider decides the label); everything else uses the item's own name.
    /// </summary>
    public string GetDisplayName(TradeItem item)
        => _context.World?.GetItemDisplayName(item.ItemRef) ?? item.Item.DisplayName;

    [RelayCommand]
    private async Task BuyItemAsync(TradeItem tradeItem)
    {
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] === BUY CLICKED ===");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] Item: {tradeItem?.Item?.RefName ?? "null"}, Price: {tradeItem?.Price ?? 0}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] AvatarEntity: {(_context.AvatarEntity != null ? "present" : "NULL")}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] CurrentArcRef: {_context.CurrentArcRef ?? "NULL"}");
        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] CurrentCharacterInstanceId: {_context.CurrentCharacterInstanceId?.ToString() ?? "NULL"}");

        if (_context.AvatarEntity == null || _context.CurrentArcRef == null || _context.CurrentCharacterInstanceId == null)
        {
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] BUY ABORTED - missing context data");
            StatusMessageChanged?.Invoke(this, "Cannot complete trade - missing avatar or character data");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[MerchantTradeVM] Avatar credits before: {_context.AvatarEntity.Stats?.Credits ?? 0}");

        try
        {
            // Send CQRS command - Arc Engine handles persistence and returns updated avatar
            var command = new TradeItemCommand
            {
                AvatarId = _context.AvatarId,
                ArcRef = _context.CurrentArcRef,
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

            // Use the updated avatar returned by Arc Engine (self-contained)
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

            // Refresh UI to reflect updated inventory and credits (re-replays the arc
            // state once, off the render path).
            OnPropertyChanged(nameof(Avatar));
            InvalidateTradeInventory();
            await RefreshArcStateAsync();
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

        if (_context.AvatarEntity == null || _context.CurrentArcRef == null || _context.CurrentCharacterInstanceId == null)
        {
            StatusMessageChanged?.Invoke(this, "Cannot complete trade - missing avatar or character data");
            return;
        }

        try
        {
            // Send CQRS command - Arc Engine handles persistence and returns updated avatar
            var command = new TradeItemCommand
            {
                AvatarId = _context.AvatarId,
                ArcRef = _context.CurrentArcRef,
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

            // Use the updated avatar returned by Arc Engine (self-contained)
            if (result.UpdatedAvatar != null)
            {
                _context.AvatarEntity = result.UpdatedAvatar;
            }

            // Refresh UI to reflect updated inventory and credits (re-replays the arc
            // state once, off the render path).
            OnPropertyChanged(nameof(Avatar));
            InvalidateTradeInventory();
            await RefreshArcStateAsync();
        }
        catch (Exception ex)
        {
            StatusMessageChanged?.Invoke(this, $"Trade error: {ex.Message}");
        }
    }

}
