using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for MerchantTradeModal to work with the Modal Registry Pattern.
/// </summary>
public class MerchantTradeModalAdapter : IModal
{
    private readonly MerchantTradeModal _modal = new();

    public string Name => "MerchantTrade";

    public bool CanOpen(object? context)
    {
        // Validate that we have the required character context with a merchant
        return context is CharacterContext { Character.CanTrade: true };
    }

    public void OnOpening(object? context)
    {
        if (context is CharacterContext ctx)
        {
            System.Diagnostics.Debug.WriteLine($"[MerchantTradeModal] Opening for merchant: {ctx.Character.DisplayName}");
        }
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is CharacterContext ctx)
        {
            _modal.Render(ctx.ViewModel, ctx.Character, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[MerchantTradeModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[MerchantTradeModal] Closed");
    }
}
