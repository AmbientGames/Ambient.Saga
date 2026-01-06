using Ambient.Application.Contracts;
using Ambient.Application.WorldCreation;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI.Services;
using Microsoft.Extensions.Logging;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for WorldSelectionScreen to work with the Modal Registry Pattern.
/// </summary>
public class WorldSelectionScreenAdapter : IModal
{
    private readonly WorldSelectionScreen _modal;

    public event Action<string>? WorldCreated;

    public WorldSelectionScreenAdapter(
        IWorldContentGenerator worldContentGenerator,
        IGameSettings gameSettings,
        IThemeProvider themeProvider,
        IWorldCreationService worldCreationService,
        IFileDialogService? fileDialogService,
        IGeoTiffConverter? geoTiffConverter,
        ITextureProvider? textureProvider,
        IAIWorldGenerationService? aiWorldGenerationService,
        ILogger? logger = null)
    {
        _modal = new WorldSelectionScreen(
            worldContentGenerator ?? throw new ArgumentNullException(nameof(worldContentGenerator)),
            gameSettings ?? throw new ArgumentNullException(nameof(gameSettings)),
            themeProvider ?? throw new ArgumentNullException(nameof(themeProvider)),
            worldCreationService ?? throw new ArgumentNullException(nameof(worldCreationService)),
            fileDialogService,
            geoTiffConverter,
            textureProvider, aiWorldGenerationService, logger);

        _modal.WorldCreated += path => WorldCreated?.Invoke(path);
    }

    public string Name => "WorldSelection";

    public bool CanOpen(object? context)
    {
        return context is MainViewModel;
    }

    public void OnOpening(object? context)
    {
        System.Diagnostics.Debug.WriteLine("[WorldSelectionScreen] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        if (context is MainViewModel viewModel)
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

    /// <summary>
    /// Sets the texture provider for rendering terrain previews.
    /// </summary>
    public void SetTextureProvider(ITextureProvider textureProvider)
    {
        _modal.SetTextureProvider(textureProvider);
    }
}
