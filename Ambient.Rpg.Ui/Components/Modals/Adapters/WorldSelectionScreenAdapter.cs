using Ambient.Application.Contracts;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Presentation.UI.ViewModels;
using Microsoft.Extensions.Logging;

namespace Ambient.Rpg.Ui.Components.Modals.Adapters;

/// <summary>
/// Adapter for WorldSelectionScreen to work with the Modal Registry Pattern.
/// </summary>
public class WorldSelectionScreenAdapter : IModal
{
    private readonly WorldSelectionScreen _modal;

    public WorldSelectionScreenAdapter(
        IWorldContentGenerator worldContentGenerator,
        IGameSettings gameSettings,
        ILogger<WorldSelectionScreen>? logger = null)
    {
        _modal = new WorldSelectionScreen(worldContentGenerator, gameSettings, logger);
    }

    public string Name => "WorldSelection";

    public bool CanOpen(object? context)
    {
        return context is RpgMainViewModel;
    }

    public void OnOpening(object? context)
    {
        System.Diagnostics.Debug.WriteLine("[WorldSelectionScreen] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is RpgMainViewModel viewModel)
        {
            _modal.Render(viewModel, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[WorldSelectionScreen] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[WorldSelectionScreen] Closed");
    }
}
