namespace Ambient.Saga.UI.Services;

/// <summary>
/// Provides Steam availability status for the Schema.Sandbox library.
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
