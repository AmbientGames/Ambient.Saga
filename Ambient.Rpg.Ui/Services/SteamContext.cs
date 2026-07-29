namespace Ambient.Rpg.Ui.Services;

/// <summary>
/// Provides Steam availability status to the host application.
/// Set by the host application during initialization.
/// </summary>
public static class SteamContext
{
    /// <summary>
    /// Indicates whether Steam API has been initialized and is available.
    /// Set this from the host application after initializing Steam.
    /// </summary>
    public static bool IsSteamInitialized { get; set; } = false;
}
