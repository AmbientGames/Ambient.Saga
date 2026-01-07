using Ambient.Application.WorldCreation;

namespace Ambient.Infrastructure.WorldCreation;

/// <summary>
/// Provides available themes by scanning both install and AppData directories.
/// </summary>
public class ThemeProvider : IThemeProvider
{
    private readonly string? _appDataThemesPath;
    private readonly string? _installThemesPath;
    private string[]? _cachedThemes;

    /// <summary>
    /// Create a ThemeProvider with explicit paths.
    /// </summary>
    /// <param name="appDataContentPath">AppData content path (e.g., from IGameSettings.GetAppDataContentPath())</param>
    /// <param name="installDirectory">Install directory (e.g., AppDomain.CurrentDomain.BaseDirectory)</param>
    public ThemeProvider(string? appDataContentPath, string? installDirectory)
    {
        if (!string.IsNullOrEmpty(appDataContentPath))
            _appDataThemesPath = Path.Combine(appDataContentPath, "Generation", "Themes");

        if (!string.IsNullOrEmpty(installDirectory))
            _installThemesPath = Path.Combine(installDirectory, "Content", "Generation", "Themes");
    }

    /// <inheritdoc/>
    public string[] GetAvailableThemes()
    {
        if (_cachedThemes != null)
            return _cachedThemes;

        var themeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check AppData location first (user themes)
        if (!string.IsNullOrEmpty(_appDataThemesPath) && Directory.Exists(_appDataThemesPath))
        {
            foreach (var dir in Directory.GetDirectories(_appDataThemesPath))
            {
                var themeName = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(themeName))
                    themeNames.Add(themeName);
            }
        }

        // Check install location (bundled themes)
        if (!string.IsNullOrEmpty(_installThemesPath) && Directory.Exists(_installThemesPath))
        {
            foreach (var dir in Directory.GetDirectories(_installThemesPath))
            {
                var themeName = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(themeName))
                    themeNames.Add(themeName);
            }
        }

        _cachedThemes = themeNames.OrderBy(t => t).ToArray();

        if (_cachedThemes.Length == 0)
            _cachedThemes = new[] { "Default" };

        return _cachedThemes;
    }

    /// <inheritdoc/>
    public string GetDisplayName(string themeFolderName)
    {
        if (string.IsNullOrEmpty(themeFolderName))
            return "Default";

        // Convert folder name to display name (e.g., "fantasy_medieval" -> "Fantasy Medieval")
        return string.Join(" ", themeFolderName
            .Replace("_", " ")
            .Replace("-", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
    }

    /// <summary>
    /// Clear the cached themes to force a refresh on next call.
    /// </summary>
    public void ClearCache()
    {
        _cachedThemes = null;
    }
}
