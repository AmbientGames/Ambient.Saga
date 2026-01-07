namespace Ambient.Application.WorldCreation;

/// <summary>
/// Provides available themes for world creation.
/// Themes are loaded from both the install directory and user's AppData.
/// </summary>
public interface IThemeProvider
{
    /// <summary>
    /// Get all available theme names.
    /// Combines themes from install location and AppData.
    /// </summary>
    string[] GetAvailableThemes();

    /// <summary>
    /// Get the display name for a theme (converts folder name to title case).
    /// </summary>
    string GetDisplayName(string themeFolderName);
}
