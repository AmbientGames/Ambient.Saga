using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal for trading with merchants - uses MerchantTradeViewModel from WPF project
/// </summary>
public class MerchantTradeModal
{
    private MerchantTradeViewModel? _tradeViewModel;
    private Guid _currentCharacterId = Guid.Empty;

    public void Render(SagaMainViewModel viewModel, CharacterViewModel character, ref bool isOpen)
    {
        if (!isOpen)
        {
            _tradeViewModel = null;
            _currentCharacterId = Guid.Empty;
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

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);

        var windowFlags = ImGuiWindowFlags.NoCollapse;

        if (ImGui.Begin($"Trade###MerchantTradeModal", ref isOpen, windowFlags))
        {
            // Merchant header
            ImGui.PushFont(UIConstants.FontTitle);
            ImGui.TextColored(new Vector4(1, 0.85f, 0.3f, 1), character.DisplayName);
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "- Merchant");

            ImGui.Separator();
            ImGui.Spacing();

            // Avatar money display at top
            if (_tradeViewModel.IsMerchant && _tradeViewModel.Avatar?.Stats != null)
            {
                var moneyBarHeight = ImGui.GetFrameHeightWithSpacing() * 1.5f;
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.12f, 0.08f, 0.9f));
                ImGui.BeginChild("AvatarMoney", new Vector2(ImGuiSizes.Fill, moneyBarHeight), ImGuiChildFlags.Borders);
                ImGui.Spacing();
                ImGui.Indent(10 * UIConstants.DpiScale);
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1), "Your Funds:");
                ImGui.SameLine();
                ImGui.PushFont(UIConstants.FontTitle);
                ImGui.TextColored(new Vector4(1, 0.85f, 0.2f, 1),
                    $"{_tradeViewModel.Avatar.Stats.Credits:N0} {_tradeViewModel.PluralCurrencyName}");
                ImGui.PopFont();
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
                var buttonWidth = 120f;
                var toggleButtonHeight = ImGui.GetFrameHeight();

                // Buy button
                if (isBuyMode)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.5f, 0.25f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.6f, 0.3f, 1));
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.25f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.35f, 0.35f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1));
                }
                if (ImGui.Button("Buy from Merchant", new Vector2(buttonWidth + 40, toggleButtonHeight)))
                {
                    _tradeViewModel.TradeMode = "Buy";
                }
                ImGui.PopStyleColor(3);

                ImGui.SameLine();

                // Sell button
                if (isSellMode)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.3f, 0.2f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.4f, 0.25f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.5f, 0.3f, 1));
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.25f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.35f, 0.35f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1));
                }
                if (ImGui.Button("Sell your Items", new Vector2(buttonWidth + 30, toggleButtonHeight)))
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

                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1), "Category:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200);
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
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.6f, 1), headerText);
            ImGui.Spacing();

            // Trade inventory list - reserve space for footer
            var tradeFooterHeight = ImGui.GetFrameHeightWithSpacing() * 2;
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.08f, 0.9f));
            ImGui.BeginChild("TradeInventory", new Vector2(ImGuiSizes.Fill, -tradeFooterHeight), ImGuiChildFlags.Borders);

            if (_tradeViewModel.TradeInventory.Count == 0)
            {
                ImGui.Spacing();
                ImGui.Spacing();
                var emptyText = _tradeViewModel.TradeMode == "Buy"
                    ? "Merchant has no items in this category"
                    : "You have no items to sell in this category";
                var textSize = ImGui.CalcTextSize(emptyText);
                ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), emptyText);
            }
            else
            {
                foreach (var tradeItem in _tradeViewModel.TradeInventory)
                {
                    // Use stable ID based on item RefName, not object hash (which changes each frame)
                    ImGui.PushID(tradeItem.Item.RefName);

                    // Item row with styled background
                    ImGui.BeginGroup();

                    // Item name
                    ImGui.TextColored(new Vector4(0.95f, 0.95f, 0.9f, 1), tradeItem.Item.DisplayName);

                    // Quantity if > 1
                    if (tradeItem.Quantity > 1)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"x{tradeItem.Quantity}");
                    }

                    // Price - position using content region for proper scrollbar handling
                    var contentStart = ImGui.GetCursorPosX();
                    var contentWidth = ImGui.GetContentRegionAvail().X;
                    ImGui.SameLine(contentStart + contentWidth - 220);
                    ImGui.TextColored(new Vector4(1, 0.85f, 0.2f, 1), $"{tradeItem.Price:N0}");
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), _tradeViewModel.CurrencyName);

                    // Buy/Sell button
                    var itemButtonHeight = ImGui.GetFrameHeight();
                    ImGui.SameLine(contentStart + contentWidth - 80);
                    if (_tradeViewModel.TradeMode == "Buy")
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.35f, 0.2f, 1));
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.5f, 0.3f, 1));
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.65f, 0.4f, 1));

                        var buttonClicked = ImGui.Button($"Buy##{tradeItem.Item.RefName}", new Vector2(60, itemButtonHeight));
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
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.25f, 0.15f, 1));
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.35f, 0.2f, 1));
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.65f, 0.45f, 0.25f, 1));
                        if (ImGui.Button($"Sell##{tradeItem.Item.RefName}", new Vector2(60, itemButtonHeight)))
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

            // Close button centered
            ImGui.Spacing();
            var closeWidth = 130f;
            var closeButtonHeight = ImGui.GetFrameHeight() * 1.2f;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - closeWidth) * 0.5f);
            if (ImGui.Button("Leave Shop", new Vector2(closeWidth, closeButtonHeight)))
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

    private void InitializeViewModel(SagaMainViewModel viewModel, CharacterViewModel character)
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

            // Create SagaInteractionContext
            var context = new SagaInteractionContext
            {
                World = viewModel.CurrentWorld,
                AvatarEntity = viewModel.Avatar,
                AvatarId = viewModel.Avatar?.AvatarId ?? Guid.Empty,
                ActiveCharacter = characterTemplate,
                CurrentSagaRef = character.SagaRef,
                CurrentCharacterInstanceId = character.CharacterInstanceId
            };

            // Create ViewModel
            _tradeViewModel = new MerchantTradeViewModel(context, viewModel.Mediator);
            _tradeViewModel.RefreshCategories();

            // Subscribe to events
            _tradeViewModel.ActivityMessageGenerated += (s, msg) =>
            {
                viewModel.ActivityLog.Insert(0, msg);
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
