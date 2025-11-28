using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal for trading with merchants - uses MerchantTradeViewModel from WPF project
/// </summary>
public class MerchantTradeModal
{
    private MerchantTradeViewModel? _tradeViewModel;
    private Guid _currentCharacterId = Guid.Empty;

    public void Render(MainViewModel viewModel, CharacterViewModel character, ref bool isOpen)
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

        ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"Trade with {character.DisplayName}", ref isOpen))
        {
            ImGui.TextColored(new Vector4(1, 0.84f, 0, 1), $"Merchant: {character.DisplayName}");
            ImGui.Separator();
            ImGui.Spacing();

            // Buy/Sell mode toggle
            if (_tradeViewModel.ShowBuySellToggle)
            {
                var isBuyMode = _tradeViewModel.TradeMode == "Buy";
                var isSellMode = _tradeViewModel.TradeMode == "Sell";

                if (ImGui.RadioButton("Buy", isBuyMode))
                {
                    _tradeViewModel.TradeMode = "Buy";
                }
                ImGui.SameLine();
                if (ImGui.RadioButton("Sell", isSellMode))
                {
                    _tradeViewModel.TradeMode = "Sell";
                }
                ImGui.Spacing();
            }

            // Category selector (if multiple categories available)
            if (_tradeViewModel.ShowCategorySelector && _tradeViewModel.AvailableCategories.Count > 0)
            {
                var categories = _tradeViewModel.AvailableCategories.ToList();
                var currentIndex = categories.IndexOf(_tradeViewModel.SelectedTradeCategory);
                if (currentIndex < 0) currentIndex = 0;

                if (ImGui.Combo("Category", ref currentIndex, categories.ToArray(), categories.Count))
                {
                    _tradeViewModel.SelectedTradeCategory = categories[currentIndex];
                }
                ImGui.Spacing();
            }

            // Player money display
            if (_tradeViewModel.IsMerchant && _tradeViewModel.PlayerAvatar?.Stats != null)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1),
                    $"Your {_tradeViewModel.PluralCurrencyName}: {_tradeViewModel.PlayerAvatar.Stats.Credits:N0}");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // Trade inventory list
            ImGui.BeginChild("TradeInventory", new Vector2(0, -40), ImGuiChildFlags.Borders);

            if (_tradeViewModel.TradeInventory.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1),
                    _tradeViewModel.TradeMode == "Buy" ? "Merchant has no items in this category" : "You have no items in this category");
            }
            else
            {
                foreach (var tradeItem in _tradeViewModel.TradeInventory)
                {
                    ImGui.PushID(tradeItem.GetHashCode());

                    // Item name and price
                    ImGui.Text(tradeItem.Item.DisplayName);
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"({tradeItem.Price} {_tradeViewModel.CurrencyName})");

                    // Quantity if > 1
                    if (tradeItem.Quantity > 1)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"x{tradeItem.Quantity}");
                    }

                    // Buy/Sell button
                    ImGui.SameLine();
                    if (_tradeViewModel.TradeMode == "Buy")
                    {
                        if (ImGui.Button("Buy"))
                        {
                            _tradeViewModel.BuyItemCommand.Execute(tradeItem);
                        }
                    }
                    else
                    {
                        if (ImGui.Button("Sell"))
                        {
                            _tradeViewModel.SellItemCommand.Execute(tradeItem);
                        }
                    }

                    ImGui.Separator();
                    ImGui.PopID();
                }
            }

            ImGui.EndChild();

            // Close button
            ImGui.Spacing();
            if (ImGui.Button("Close", new Vector2(120, 0)))
            {
                isOpen = false;
            }

            ImGui.End();
        }

        // Clean up when window closes
        if (!isOpen)
        {
            _tradeViewModel = null;
            _currentCharacterId = Guid.Empty;
        }
    }

    private void InitializeViewModel(MainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.CurrentWorld == null || viewModel.PlayerAvatar == null)
            return;

        try
        {
            // Find character template
            var characterTemplate = viewModel.CurrentWorld.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == character.CharacterRef);
            if (characterTemplate == null) return;

            // Create SagaInteractionContext
            var context = new SagaInteractionContext
            {
                World = viewModel.CurrentWorld,
                AvatarEntity = viewModel.PlayerAvatar,
                ActiveCharacter = characterTemplate
            };

            // Create ViewModel
            _tradeViewModel = new MerchantTradeViewModel(context, viewModel.Mediator);
            _tradeViewModel.RefreshCategories();

            // Subscribe to events
            _tradeViewModel.ActivityMessageGenerated += (s, msg) =>
            {
                viewModel.ActivityLog.Insert(0, msg);
            };

            _tradeViewModel.StatusMessageChanged += (s, msg) =>
            {
                viewModel.StatusMessage = msg;
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing trade: {ex.Message}");
        }
    }
}
