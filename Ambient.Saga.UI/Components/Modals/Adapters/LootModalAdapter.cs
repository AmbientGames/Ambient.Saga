using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for LootModal to work with the Modal Registry Pattern.
/// </summary>
public class LootModalAdapter : IModal
{
    private readonly LootModal _modal = new();

    public string Name => "Loot";

    public bool CanOpen(object? context)
    {
        // Validate that we have the required character context
        return context is CharacterContext { Character.CanLoot: true };
    }

    public void OnOpening(object? context)
    {
        if (context is CharacterContext ctx)
        {
            System.Diagnostics.Debug.WriteLine($"[LootModal] Opening for character: {ctx.Character.DisplayName}");
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
            System.Diagnostics.Debug.WriteLine("[LootModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[LootModal] Closed");
    }
}
