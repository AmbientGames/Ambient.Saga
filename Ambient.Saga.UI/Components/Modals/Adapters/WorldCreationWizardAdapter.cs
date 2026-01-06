using Ambient.Application.Contracts;
using Ambient.Application.WorldCreation;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI.Services;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for WorldCreationWizard to work with the Modal Registry Pattern.
/// </summary>
public class WorldCreationWizardAdapter : IModal
{
    private readonly WorldCreationWizard _wizard;

    /// <summary>
    /// Event raised when a world is successfully created.
    /// </summary>
    public event Action<string>? WorldCreated;

    public WorldCreationWizardAdapter(
        IGameSettings gameSettings,
        IThemeProvider themeProvider,
        IWorldCreationService worldCreationService,
        IFileDialogService? fileDialogService = null,
        IGeoTiffConverter? geoTiffConverter = null,
        ITextureProvider? textureProvider = null,
        ILocationGenerator? locationGenerator = null)
    {
        _wizard = new WorldCreationWizard(
            gameSettings ?? throw new ArgumentNullException(nameof(gameSettings)),
            themeProvider ?? throw new ArgumentNullException(nameof(themeProvider)),
            worldCreationService ?? throw new ArgumentNullException(nameof(worldCreationService)),
            fileDialogService,
            geoTiffConverter,
            textureProvider,
            locationGenerator);

        // Forward the wizard's WorldCreated event
        _wizard.WorldCreated += path => WorldCreated?.Invoke(path);
    }

    public string Name => "WorldCreationWizard";

    public bool CanOpen(object? context)
    {
        // Can be opened from any context - MainViewModel or null
        return true;
    }

    public void OnOpening(object? context)
    {
        // Reset wizard state when opening
        _wizard.Reset();
        System.Diagnostics.Debug.WriteLine("[WorldCreationWizard] Opening");
    }

    public void Render(object? context, ref bool isOpen)
    {
        _wizard.Render(ref isOpen);
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[WorldCreationWizard] Closed");
    }

    /// <summary>
    /// Sets the texture provider for rendering terrain previews.
    /// </summary>
    public void SetTextureProvider(ITextureProvider textureProvider)
    {
        _wizard.SetTextureProvider(textureProvider);
    }
}
