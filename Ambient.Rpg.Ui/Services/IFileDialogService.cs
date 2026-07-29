namespace Ambient.Rpg.Ui.Services;

/// <summary>
/// Platform-agnostic file dialog service.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Opens a file selection dialog, defaulting to the user's Downloads folder.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="filters">File filters (e.g., "GeoTIFF|*.tif;*.tiff")</param>
    /// <param name="callback">Called with selected file path, or null if cancelled</param>
    void OpenFile(string title, string filters, Action<string?> callback);
}
