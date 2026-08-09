using Ambient.Rpg.Presentation.UI.ViewModels;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Rpg.Engine.Domain.Arcs;
using Ambient.Rpg.Ui.Components.Utilities;
using ImGuiNET;
using System.Numerics;
using Ambient.Rpg.Rendering.DirectX;

namespace Ambient.Rpg.Ui.Components.Modals;

/// <summary>
/// Modal for trading with merchants - uses MerchantTradeViewModel from WPF project
/// </summary>
public class MerchantTradeModal
{
    private MerchantTradeViewModel? _tradeViewModel;
    private Guid _currentCharacterId = Guid.Empty;

    // Per-row listing-price edit buffers (owner's shop only), keyed by the listing ref.
    private readonly Dictionary<string, int> _listingEdits = new();

    public void Render(RpgMainViewModel viewModel, CharacterViewModel character, ref bool isOpen)
    {
        if (!isOpen)
        {
            _tradeViewModel = null;
            _currentCharacterId = Guid.Empty;
            _listingEdits.Clear();
            return;
        }

        // Initialize ViewModel if needed
        if (_tradeViewModel == null || _currentCharacterId != character.CharacterInstanceId)
        {
            InitializeViewModel(viewModel, character);
            _currentCharacterId = character.CharacterInstanceId;
        }

        if (_tradeViewModel == null)
        {
            ImGui.Text("Error: Could not initialize trade");
            return;
        }

        // Center the window using helper
        ImGuiHelpers.SetupModalWindow(800, 650);

        // Style the window (DPI-scaled)
        var scale = UIConstants.DpiScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20 * scale, 20 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f * scale);

        var windowFlags = ImGuiWindowFlags.NoCollapse;

        // Victory loot (defeat drop) is visually distinct from the cache kinds:
        // "Remnant Loot" = a player's death drop, "Victory Loot" = an enemy you beat.
        var titleText = character.IsVictoryLoot
            ? "Victory Loot"
            : character.ArcKind switch
            {
                "GeoCache" => "Geocache",
                "RemnantLoot" => "Remnant Loot",
                "Market" => "Shop",
                _ => "Trade"
            };
        if (ImGui.Begin($"{titleText}###MerchantTradeModal", ref isOpen, windowFlags))
        {
            // Header (merchant or cache). FontHeading for both halves so the subtitle
            // baselines with the name; the modal's window titlebar already carries the
            // bigger "Shop"/"Geocache"/"Remnant Loot" label.
            ImGui.PushFont(UIConstants.FontHeading);
            ImGui.TextColored(UIColors.Gold, character.DisplayName);
            ImGui.SameLine();
            ImGui.TextColored(UIColors.TextMuted, _tradeViewModel.HeaderSubtitle);
            ImGui.PopFont();

            ImGui.Separator();
            ImGui.Spacing();

            // Avatar money display at top (merchant mode only)
            if (_tradeViewModel.ShowMoneyBar && _tradeViewModel.Avatar?.Stats != null)
            {
                var moneyBarHeight = ImGui.GetFrameHeightWithSpacing() * 1.5f;
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.12f, 0.08f, 0.9f));
                ImGui.BeginChild("AvatarMoney", new Vector2(ImGuiSizes.Fill, moneyBarHeight), ImGuiChildFlags.Borders);
                ImGui.Spacing();
                ImGui.Indent(10 * UIConstants.DpiScale);
                ImGui.TextColored(UIColors.TextMuted, "Your Funds:");
                ImGui.SameLine();
                ImGui.TextColored(UIColors.Gold,
                    $"{_tradeViewModel.Avatar.Stats.Credits:N0} {_tradeViewModel.PluralCurrencyName}");
                ImGui.Unindent(10 * UIConstants.DpiScale);
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            // Buy/Sell mode toggle as styled buttons
            if (_tradeViewModel.ShowBuySellToggle)
            {
                var isBuyMode = _tradeViewModel.TradeMode == "Buy";
                var isSellMode = _tradeViewModel.TradeMode == "Sell";
                var modeButtonWidth = 160f;
                var toggleButtonHeight = ImGui.GetFrameHeight();

                // Buy button
                if (isBuyMode)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonAccept);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonAcceptHovered);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonAcceptActive);
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonNeutral);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonNeutralHovered);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonNeutralActive);
                }
                if (ImGui.Button(_tradeViewModel.BuyModeLabel, new Vector2(modeButtonWidth, toggleButtonHeight)))
                {
                    _tradeViewModel.TradeMode = "Buy";
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();

                // Sell button
                if (isSellMode)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonWarning);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonWarningHovered);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonWarningActive);
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonNeutral);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonNeutralHovered);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonNeutralActive);
                }
                if (ImGui.Button(_tradeViewModel.SellModeLabel, new Vector2(modeButtonWidth, toggleButtonHeight)))
                {
                    _tradeViewModel.TradeMode = "Sell";
                }
                ImGui.PopStyleColor(3);

                ImGui.Spacing();
            }

            // Category selector (if multiple categories available)
            if (_tradeViewModel.ShowCategorySelector && _tradeViewModel.AvailableCategories.Count > 0)
            {
                var categories = _tradeViewModel.AvailableCategories.ToList();
                var currentIndex = categories.IndexOf(_tradeViewModel.SelectedTradeCategory);
                if (currentIndex < 0) currentIndex = 0;

                ImGui.TextColored(UIColors.TextMuted, "Category:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200 * UIConstants.DpiScale);
                if (ImGui.Combo("##Category", ref currentIndex, categories.ToArray(), categories.Count))
                {
                    _tradeViewModel.SelectedTradeCategory = categories[currentIndex];
                }
                ImGui.Spacing();
            }

            ImGui.Separator();
            ImGui.Spacing();

            // Trade inventory list header
            var headerText = _tradeViewModel.TradeMode == "Buy" ? "Available Items" : "Your Items";
            ImGui.TextColored(new Vector4(0.95f, 0.92f, 0.8f, 1), headerText);
            ImGui.Spacing();

            // Trade inventory list - reserve space for footer
            var tradeFooterHeight = ImGui.GetFrameHeightWithSpacing() * 2;
            ImGui.PushStyleColor(ImGuiCol.ChildBg, UIColors.PanelBgDark);
            ImGui.BeginChild("TradeInventory", new Vector2(ImGuiSizes.Fill, -tradeFooterHeight), ImGuiChildFlags.Borders);

            if (_tradeViewModel.TradeInventory.Count == 0)
            {
                ImGui.Spacing();
                var emptyText = _tradeViewModel.TradeMode == "Buy"
                    ? _tradeViewModel.EmptyBuyText
                    : _tradeViewModel.EmptySellText;
                var textSize = ImGui.CalcTextSize(emptyText);
                ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
                ImGui.TextColored(UIColors.TextDisabled, emptyText);
            }
            else
            {
                // Right-edge column anchors (DPI-scaled). Action button hugs the right edge;
                // price + currency sits to its left with a fixed gap.
                var s = UIConstants.DpiScale;
                var actionButtonWidth = 70f * s;
                var priceColumnWidth = 140f * s;
                var rightEdgeMargin = 8f * s;

                foreach (var tradeItem in _tradeViewModel.TradeInventory)
                {
                    // Use stable ID based on the trade identity (the exact ref), not object hash
                    // (which changes each frame)
                    ImGui.PushID(tradeItem.ItemRef);

                    // Item row with styled background
                    ImGui.BeginGroup();

                    // Item name (the provider resolves block refs to their label)
                    ImGui.TextColored(new Vector4(0.95f, 0.95f, 0.9f, 1), _tradeViewModel.GetDisplayName(tradeItem));

                    // Quantity if > 1
                    if (tradeItem.Quantity > 1)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(UIColors.TextMuted, $"x{tradeItem.Quantity}");
                    }

                    var rowWidth = ImGui.GetContentRegionAvail().X;
                    var actionButtonX = ImGui.GetCursorPosX() + rowWidth - actionButtonWidth - rightEdgeMargin;
                    var priceColumnX = actionButtonX - priceColumnWidth;

                    // Price column (merchant only)
                    if (_tradeViewModel.ShowPrices)
                    {
                        ImGui.SameLine(priceColumnX);
                        ImGui.TextColored(UIColors.Gold, $"{tradeItem.Price:N0}");
                        ImGui.SameLine();
                        ImGui.TextColored(UIColors.TextMuted, _tradeViewModel.CurrencyName);
                    }

                    // Buy/Sell (or Take/Deposit) button
                    var itemButtonHeight = ImGui.GetFrameHeight();
                    ImGui.SameLine(actionButtonX);
                    if (_tradeViewModel.TradeMode == "Buy")
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonAccept);
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonAcceptHovered);
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonAcceptActive);

                        var buttonClicked = ImGui.Button(_tradeViewModel.ItemBuyLabel, new Vector2(actionButtonWidth, itemButtonHeight));
                        var isHovered = ImGui.IsItemHovered();
                        var isActive = ImGui.IsItemActive();

                        // Log when mouse is released over button
                        if (isHovered && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] Mouse released over Buy button for {tradeItem.Item.RefName}, buttonClicked={buttonClicked}");
                        }

                        if (buttonClicked)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] Buy button clicked for {tradeItem.Item.RefName}");
                            _tradeViewModel.BuyItemCommand.Execute(tradeItem);
                        }
                        ImGui.PopStyleColor(3);

                        if (_tradeViewModel.CanSetListingPrices)
                        {
                            // Owner-only listing editor: visitors pay exactly the listed
                            // price; 0 clears the listing (back to catalog x markup).
                            var listingKey = tradeItem.ItemRef ?? tradeItem.Item.RefName;
                            if (!_listingEdits.TryGetValue(listingKey, out var editPrice))
                            {
                                editPrice = _tradeViewModel.GetListingPrice(tradeItem) ?? 0;
                            }

                            ImGui.SetNextItemWidth(priceColumnWidth);
                            if (ImGui.InputInt("##listing", ref editPrice))
                            {
                                _listingEdits[listingKey] = Math.Max(0, editPrice);
                            }

                            ImGui.SameLine();
                            if (ImGui.Button("Set price"))
                            {
                                _ = _tradeViewModel.SetListingPriceAsync(tradeItem, Math.Max(0, editPrice));
                                _listingEdits.Remove(listingKey);
                            }

                            ImGui.SameLine();
                            var currentListing = _tradeViewModel.GetListingPrice(tradeItem);
                            ImGui.TextColored(UIColors.TextMuted,
                                currentListing is { } listed ? $"listed at {listed}" : "unlisted");
                        }
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.25f, 0.15f, 1));
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.35f, 0.2f, 1));
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.65f, 0.45f, 0.25f, 1));
                        if (ImGui.Button(_tradeViewModel.ItemSellLabel, new Vector2(actionButtonWidth, itemButtonHeight)))
                        {
                            System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] Sell button clicked for {tradeItem.Item.RefName}");
                            _tradeViewModel.SellItemCommand.Execute(tradeItem);
                        }
                        ImGui.PopStyleColor(3);
                    }

                    ImGui.EndGroup();
                    ImGui.Separator();
                    ImGui.PopID();
                }
            }

            ImGui.EndChild();
            ImGui.PopStyleColor();

            // Caller-provided extra section (e.g. cache logbook) — renders between inventory and close.
            if (character.ExtraContentRenderer != null)
            {
                ImGui.Spacing();
                character.ExtraContentRenderer.Invoke();
            }

            // Close button centered
            ImGui.Spacing();
            var closeWidth = 130f;
            var closeButtonHeight = ImGui.GetFrameHeight() * 1.2f;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - closeWidth) * 0.5f);
            if (ImGui.Button(_tradeViewModel.CloseLabel, new Vector2(closeWidth, closeButtonHeight)))
            {
                isOpen = false;
            }
        }
        ImGui.End();

        ImGui.PopStyleVar(2);

        // Clean up when window closes
        if (!isOpen)
        {
            _tradeViewModel = null;
            _currentCharacterId = Guid.Empty;
        }
    }

    private void InitializeViewModel(RpgMainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.CurrentWorld == null || viewModel.Avatar == null)
            return;

        try
        {
            // Find character template
            var characterTemplate = viewModel.CurrentWorld.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == character.CharacterRef);
            if (characterTemplate == null)
            {
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] Character template not found for ref: {character.CharacterRef}");
                return;
            }

            // Debug: Log trading state initialization
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] === TRADE STATE DEBUG ===");
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] Character: {characterTemplate.DisplayName} ({characterTemplate.RefName})");
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] Has Interactable: {characterTemplate.Interactable != null}");
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] Has Loot: {characterTemplate.Interactable?.Loot != null}");

            if (characterTemplate.Interactable?.Loot != null)
            {
                var loot = characterTemplate.Interactable.Loot;
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] Loot Contents:");
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal]   Equipment: {loot.Equipment?.Length ?? 0} items");
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal]   Consumables: {loot.Consumables?.Length ?? 0} items");
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal]   Blocks: {loot.Blocks?.Length ?? 0} items");
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal]   Tools: {loot.Tools?.Length ?? 0} items");
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal]   Spells: {loot.Spells?.Length ?? 0} items");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] WARNING: No loot defined for merchant!");
            }
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] === END DEBUG ===");

            // Create ArcInteractionContext
            var context = new ArcInteractionContext
            {
                World = viewModel.CurrentWorld,
                AvatarEntity = viewModel.Avatar,
                AvatarId = viewModel.Avatar?.AvatarId ?? Guid.Empty,
                ActiveCharacter = characterTemplate,
                CurrentArcRef = character.ArcRef,
                CurrentCharacterInstanceId = character.CharacterInstanceId
            };

            // Create ViewModel — character.IsCache flips the VM into cache mode (hides money/prices,
            // relabels Buy→Take / Sell→Deposit, zero-price trades); character.IsVictoryLoot flips it
            // into battle-loot mode and ArcKind==RemnantLoot into death-remains mode (both free
            // TAKE ONLY — remains accept no deposits).
            _tradeViewModel = new MerchantTradeViewModel(context, viewModel.Mediator,
                isCache: character.IsCache, isVictoryLoot: character.IsVictoryLoot, arcKind: character.ArcKind);
            _tradeViewModel.RefreshCategories();

            // Load the arc-replayed live inventory asynchronously (audit D9: the replay
            // used to run sync-over-async inside the TradeInventory getter, several times
            // per rendered frame). Until it lands the modal shows the template Loot
            // fallback; the cache invalidation makes the next frame pick up the result.
            _ = _tradeViewModel.RefreshArcStateAsync();

            // Subscribe to events
            _tradeViewModel.ActivityMessageGenerated += (s, msg) =>
            {
                viewModel.ActivityLog.Insert(0, msg);
            };

            // Status messages carry the trade failure text ("Not enough money", ...);
            // without this subscription failures were completely silent.
            _tradeViewModel.StatusMessageChanged += (s, msg) =>
            {
                viewModel.ActivityLog.Insert(0, msg);
                viewModel.AddToastMessage(msg);
            };

            _tradeViewModel.OwnerRevenueEarned += (ownerAvatarId, revenue) =>
            {
                viewModel.RaiseOwnerRevenueEarned(ownerAvatarId, revenue);
            };

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing trade: {ex.Message}");
        }
    }
}
