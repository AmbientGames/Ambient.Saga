using ImGuiNET;
using System;
using System.IO;

namespace Ambient.Rpg.Rendering.DirectX;

/// <summary>
/// Segoe UI fonts + DPI scaling for ImGui.NET (no unsafe/pointers).
/// Use with: UIConstants.LoadFonts(GetDpiScaleForWindow(hwnd));
/// Call AFTER you apply your theme, and BEFORE you upload the fonts texture.
/// If you reload (e.g. WM_DPICHANGED), call LoadFonts again and re-upload the fonts texture.
/// </summary>
public static class UIConstants
{
    // Base sizes at 100% DPI (96 DPI)
    // Hierarchy: Title (30) > Heading (24) > Body (20) > Small (16)
    private const float FontSizeBody = 20f;
    private const float FontSizeTitle = 30f;
    private const float FontSizeHeading = 24f;
    private const float FontSizeSmall = 16f;

    public static ImFontPtr FontBody { get; private set; }
    public static ImFontPtr FontTitle { get; private set; }
    public static ImFontPtr FontHeading { get; private set; }
    public static ImFontPtr FontSmall { get; private set; }

    /// <summary>
    /// Current DPI scale factor. Use to scale window/modal sizes.
    /// </summary>
    public static float DpiScale => _lastScale;

    private static float _lastScale = 1.0f;

    /// <summary>
    /// Loads Segoe UI fonts at (size * dpiScale) and scales ImGui style sizes once per scale change.
    /// IMPORTANT: Apply your theme BEFORE calling this. This method does NOT reset colors/style.
    /// </summary>
    public static void LoadFonts(float dpiScale)
    {
        var io = ImGui.GetIO();

        if (dpiScale <= 0f) dpiScale = 1f;

        // Prevent ScaleAllSizes from compounding if you call this again (WM_DPICHANGED etc.)
        float relativeScale = dpiScale / (_lastScale <= 0f ? 1f : _lastScale);
        if (Math.Abs(relativeScale - 1f) > 0.0001f)
            ImGui.GetStyle().ScaleAllSizes(relativeScale);

        _lastScale = dpiScale;

        // Rebuild fonts
        io.Fonts.Clear();
        io.FontGlobalScale = 1.0f;

        string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string segoeUI = Path.Combine(fontsDir, "segoeui.ttf");
        string segoeUIBold = Path.Combine(fontsDir, "segoeuib.ttf");      // Bold (preferred for titles)
        string segoeUISemibold = Path.Combine(fontsDir, "seguisb.ttf");   // fallback

        if (!File.Exists(segoeUI))
        {
            FontBody = io.Fonts.AddFontDefault();
            FontTitle = FontBody;
            FontHeading = FontBody;
            FontSmall = FontBody;
            return;
        }

        FontBody = io.Fonts.AddFontFromFileTTF(segoeUI, FontSizeBody * dpiScale);

        // Title prefers Bold, then Semibold, then Regular
        string titlePath =
            File.Exists(segoeUIBold) ? segoeUIBold :
            File.Exists(segoeUISemibold) ? segoeUISemibold :
            segoeUI;

        FontTitle = io.Fonts.AddFontFromFileTTF(titlePath, FontSizeTitle * dpiScale);

        // Heading prefers Semibold for visual distinction from body
        string headingPath =
            File.Exists(segoeUISemibold) ? segoeUISemibold :
            File.Exists(segoeUIBold) ? segoeUIBold :
            segoeUI;

        FontHeading = io.Fonts.AddFontFromFileTTF(headingPath, FontSizeHeading * dpiScale);

        FontSmall = io.Fonts.AddFontFromFileTTF(segoeUI, FontSizeSmall * dpiScale);
    }
}
