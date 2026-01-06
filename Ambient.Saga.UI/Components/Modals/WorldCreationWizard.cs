using Ambient.Application.Contracts;
using Ambient.Application.WorldCreation;
using Ambient.Saga.UI.Models;
using Ambient.Saga.UI.Services;
using BitMiracle.LibTiff.Classic;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using SharpCompress.Readers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Standalone world creation wizard modal for creating new worlds.
/// Can be used from multiple locations (WorldSelectionScreen, TileSelectionScreen, etc.)
/// </summary>
public class WorldCreationWizard
{
    private readonly IGameSettings _gameSettings;
    private readonly IFileDialogService? _fileDialogService;
    private readonly IGeoTiffConverter? _geoTiffConverter;
    private readonly IThemeProvider _themeProvider;
    private readonly IWorldCreationService _worldCreationService;
    private readonly IAIWorldGenerationService? _aiWorldGenerationService;
    private ITextureProvider? _textureProvider;
    private readonly ILogger? _logger;

    // Wizard state
    private int _wizardStep = 0;
    private bool _isRealWorld = true;

    // Step 2: Terrain state
    private string _selectedTerrainFile = "";
    private string _terrainFileStatus = "";
    private bool _terrainValidated = false;
    private string _validatedTifPath = "";
    private string _convertedTifPath = "";
    private int _terrainWidth = 0;
    private int _terrainHeight = 0;
    private const int MaxTerrainPixels = 3600 * 3600;
    private int _selectedProceduralMode = 0;

    // Terrain preview and spawn selection
    private HeightMapImageData? _terrainPreviewImage;
    private nint _terrainTexturePtr;
    private IDisposable[]? _terrainTextureResources;
    private Vector2 _selectedSpawnPixel = Vector2.Zero;
    private bool _spawnSelected = false;

    // GeoTIFF metadata
    private double[]? _geoTransform;
    private ushort[]? _heightData;
    private int _heightDataWidth;
    private int _minElevation;
    private int _maxElevation;

    // Step 3: World details state
    private string _worldName = "";
    private int _selectedLatitude = 0;
    private int _selectedWorldHeight = 0;
    private string[] _availableThemes = Array.Empty<string>();
    private int _selectedTheme = 0;

    // Step 4: Locations state
    private bool _locationsValidated = false;
    private string _locationsStatus = "";

    // AI generation state
    private bool _isGeneratingLocations;
    private string _aiGenerationStatus = "";
    private WorldGenerationResult? _aiGenerationResult;

    // Creation state
    private bool _isCreatingWorld;
    private string _creationStatus = "";
    private bool _isLoadingPreview;

    /// <summary>
    /// Event raised when a world is successfully created.
    /// Subscribers can use this to refresh world lists, etc.
    /// </summary>
    public event Action<string>? WorldCreated;

    // Static data
    private static readonly string[] ProceduralModes = { "Rugged", "Rolling", "Extreme" };
    private static readonly string[] ProceduralModeDescriptions = {
        "Dramatic peaks and deep valleys - challenging terrain",
        "Gentle hills and valleys - balanced exploration",
        "Extreme height variations - for the adventurous"
    };
    private static readonly string[] Latitudes = {
        "Equatorial (0°)", "Sub-Tropical (30°)", "Temperate (45°)", "Sub-Arctic (60°)"
    };
    private static readonly string[] LatitudeDescriptions = {
        "Tropical climate - lush vegetation, warm year-round",
        "Warm climate - distinct wet/dry seasons",
        "Moderate climate - four seasons, mixed forests",
        "Cold climate - long winters, coniferous forests"
    };
    private static readonly string[] WorldHeights = { "256 (Fast)", "512 (Standard)", "1024 (Detailed)" };
    private static readonly int[] WorldHeightValues = { 256, 512, 1024 };
    private static readonly string[] WorldHeightDescriptions = {
        "Quick generation, lower detail",
        "Balanced performance and detail",
        "High detail, slower generation"
    };
    private static readonly int[] LatitudeNumericValues = { 0, 30, 45, 60 };

    public WorldCreationWizard(
        IGameSettings gameSettings,
        IThemeProvider themeProvider,
        IWorldCreationService worldCreationService,
        IFileDialogService? fileDialogService,
        IGeoTiffConverter? geoTiffConverter,
        ITextureProvider? textureProvider,
        IAIWorldGenerationService? aiWorldGenerationService,
        ILogger? logger = null)
    {
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _themeProvider = themeProvider ?? throw new ArgumentNullException(nameof(themeProvider));
        _worldCreationService = worldCreationService ?? throw new ArgumentNullException(nameof(worldCreationService));
        _fileDialogService = fileDialogService;
        _geoTiffConverter = geoTiffConverter;
        _textureProvider = textureProvider;
        _aiWorldGenerationService = aiWorldGenerationService;
        _logger = logger;

        _availableThemes = _themeProvider.GetAvailableThemes();
    }

    /// <summary>
    /// Sets the texture provider for rendering terrain previews.
    /// </summary>
    public void SetTextureProvider(ITextureProvider textureProvider)
    {
        _textureProvider = textureProvider;
    }

    /// <summary>
    /// Renders the wizard modal. Call this each frame while the wizard is open.
    /// </summary>
    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(viewport.Size.X * 0.5f, viewport.Size.Y * 0.5f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(550, 420), ImGuiCond.Appearing);

        if (!ImGui.Begin("Create New World", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        // Step indicator
        var stepNames = new[] { "World Type", "Terrain", "Details", "Locations", "Create" };
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"Step {_wizardStep + 1} of {stepNames.Length}: {stepNames[_wizardStep]}");
        ImGui.Separator();
        ImGui.Spacing();

        // Render current step
        switch (_wizardStep)
        {
            case 0: RenderWizardStep_WorldType(); break;
            case 1: RenderWizardStep_Terrain(); break;
            case 2: RenderWizardStep_Details(); break;
            case 3: RenderWizardStep_Locations(); break;
            case 4: RenderWizardStep_Create(); break;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        RenderWizardNavigation(ref isOpen, stepNames.Length);

        ImGui.End();
    }

    /// <summary>
    /// Resets all wizard state to defaults.
    /// </summary>
    public void Reset()
    {
        _wizardStep = 0;
        _isRealWorld = true;
        _selectedTerrainFile = "";
        _terrainFileStatus = "";
        _terrainValidated = false;
        _validatedTifPath = "";
        _convertedTifPath = "";
        _terrainWidth = 0;
        _terrainHeight = 0;
        _selectedProceduralMode = 0;
        _terrainPreviewImage = null;
        _terrainTexturePtr = IntPtr.Zero;
        _selectedSpawnPixel = Vector2.Zero;
        _spawnSelected = false;
        _isLoadingPreview = false;
        _worldName = "";
        _selectedLatitude = 0;
        _selectedWorldHeight = 0;
        _selectedTheme = 0;
        _locationsValidated = false;
        _locationsStatus = "";
        _isGeneratingLocations = false;
        _aiGenerationStatus = "";
        _aiGenerationResult = null;
        _isCreatingWorld = false;
        _creationStatus = "";
        _geoTransform = null;
        _heightData = null;
        _heightDataWidth = 0;
        _minElevation = 0;
        _maxElevation = 0;

        if (_terrainTextureResources != null && _textureProvider != null)
        {
            _textureProvider.DisposeTexture(_terrainTextureResources);
            _terrainTextureResources = null;
        }
    }

    #region Step Rendering

    private void RenderWizardStep_WorldType()
    {
        ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "What kind of world do you want to create?");
        ImGui.Spacing();
        ImGui.Spacing();

        // Real World option
        ImGui.PushStyleColor(ImGuiCol.Header, _isRealWorld ? new Vector4(0.2f, 0.4f, 0.6f, 1) : new Vector4(0.2f, 0.2f, 0.2f, 1));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.3f, 0.5f, 0.7f, 1));
        if (ImGui.Selectable("## RealWorld", _isRealWorld, ImGuiSelectableFlags.None, new Vector2(0, 70)))
        {
            _isRealWorld = true;
        }
        ImGui.PopStyleColor(2);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 68);
        ImGui.Indent(10);
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1), "Real World Location");
        ImGui.TextWrapped("Create a world based on real elevation data (DEM). Perfect for recreating actual mountains, valleys, and coastlines.");
        ImGui.Unindent(10);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10);

        ImGui.Spacing();

        // Procedural option
        ImGui.PushStyleColor(ImGuiCol.Header, !_isRealWorld ? new Vector4(0.2f, 0.4f, 0.6f, 1) : new Vector4(0.2f, 0.2f, 0.2f, 1));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.3f, 0.5f, 0.7f, 1));
        if (ImGui.Selectable("## Procedural", !_isRealWorld, ImGuiSelectableFlags.None, new Vector2(0, 70)))
        {
            _isRealWorld = false;
        }
        ImGui.PopStyleColor(2);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 68);
        ImGui.Indent(10);
        ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1), "Procedural Generation");
        ImGui.TextWrapped("Generate terrain algorithmically. Choose from various terrain styles like rolling hills, rugged mountains, or flat plains.");
        ImGui.Unindent(10);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10);
    }

    private void RenderWizardStep_Terrain()
    {
        if (_isRealWorld)
            RenderWizardStep_Terrain_RealWorld();
        else
            RenderWizardStep_Terrain_Procedural();
    }

    private void RenderWizardStep_Terrain_RealWorld()
    {
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1), "Real World Elevation Data");
        ImGui.Spacing();

        ImGui.TextWrapped("You'll need a GeoTIFF (.tif) elevation file. OpenTopography provides free SRTM data covering most of Earth at ~30m resolution.");
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(1, 0.8f, 0.4f, 1), "Step 1: Get elevation data");
        ImGui.Spacing();
        if (ImGui.Button("Open OpenTopography Website", new Vector2(-1, 30)))
        {
            OpenUrl("https://portal.opentopography.org/raster?opentopoID=OTSRTM.082015.4326.1");
        }
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Select your region, download the raster data (.tar.gz or .tif)");

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(1, 0.8f, 0.4f, 1), "Step 2: Select your downloaded file");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 90);
        ImGui.InputText("##TerrainFile", ref _selectedTerrainFile, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse...", new Vector2(80, 0)))
        {
            BrowseForTerrainFile();
        }

        if (!string.IsNullOrEmpty(_terrainFileStatus))
        {
            ImGui.Spacing();
            var statusColor = _terrainFileStatus.StartsWith("Error") || _terrainFileStatus.StartsWith("Not")
                ? new Vector4(1, 0.4f, 0.4f, 1)
                : new Vector4(0.4f, 1, 0.4f, 1);
            ImGui.TextColored(statusColor, _terrainFileStatus);
        }

        CreateTerrainTexture();

        if (_terrainValidated && _terrainTexturePtr != IntPtr.Zero && _terrainPreviewImage != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("Click to set spawn location:");

            var availWidth = ImGui.GetContentRegionAvail().X;
            var aspectRatio = (float)_terrainPreviewImage.Width / _terrainPreviewImage.Height;
            var displayWidth = availWidth;
            var displayHeight = displayWidth / aspectRatio;
            var imageSize = new Vector2(displayWidth, displayHeight);

            var imagePos = ImGui.GetCursorScreenPos();
            ImGui.Image(_terrainTexturePtr, imageSize);

            if (ImGui.IsItemClicked())
            {
                var mousePos = ImGui.GetMousePos();
                var relativePos = mousePos - imagePos;
                _selectedSpawnPixel = new Vector2(
                    relativePos.X / displayWidth * _terrainPreviewImage.Width,
                    relativePos.Y / displayHeight * _terrainPreviewImage.Height);
                _spawnSelected = true;
            }

            if (ImGui.IsItemHovered() && _heightData != null)
            {
                var mousePos = ImGui.GetMousePos();
                var relativePos = mousePos - imagePos;
                var pixelX = Math.Clamp((int)(relativePos.X / displayWidth * _terrainPreviewImage.Width), 0, _terrainPreviewImage.Width - 1);
                var pixelY = Math.Clamp((int)(relativePos.Y / displayHeight * _terrainPreviewImage.Height), 0, _terrainPreviewImage.Height - 1);

                var heightIndex = pixelY * _heightDataWidth + pixelX;
                var heightValue = heightIndex < _heightData.Length ? _heightData[heightIndex] : 0;

                var tooltipText = $"Height: {heightValue}m";
                if (_geoTransform != null && _geoTransform.Length >= 6)
                {
                    var lon = _geoTransform[0] + pixelX * _geoTransform[1] + pixelY * _geoTransform[2];
                    var lat = _geoTransform[3] + pixelX * _geoTransform[4] + pixelY * _geoTransform[5];
                    tooltipText += $"\nLat: {lat:F5}, Lon: {lon:F5}";
                }
                ImGui.SetTooltip(tooltipText);
            }

            if (_spawnSelected)
            {
                var markerScreenPos = imagePos + new Vector2(
                    _selectedSpawnPixel.X / _terrainPreviewImage.Width * displayWidth,
                    _selectedSpawnPixel.Y / _terrainPreviewImage.Height * displayHeight);
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddCircleFilled(markerScreenPos, 6, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1)));
                drawList.AddCircle(markerScreenPos, 6, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), 12, 2);
            }

            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Spawn: ({(int)_selectedSpawnPixel.X}, {(int)_selectedSpawnPixel.Y})");
        }
        else if (!_terrainValidated)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Supported: .tif, .tiff, .tar.gz (containing GeoTIFF)");
        }
    }

    private void RenderWizardStep_Terrain_Procedural()
    {
        ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1), "Procedural Terrain Generation");
        ImGui.Spacing();

        ImGui.TextWrapped("Choose a terrain style. This determines the overall feel of your world's landscape.");
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.Text("Terrain Style:");
        ImGui.Spacing();

        for (int i = 0; i < ProceduralModes.Length; i++)
        {
            var isSelected = _selectedProceduralMode == i;
            ImGui.PushStyleColor(ImGuiCol.Header, isSelected ? new Vector4(0.2f, 0.5f, 0.3f, 1) : new Vector4(0.2f, 0.2f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.3f, 0.6f, 0.4f, 1));

            if (ImGui.Selectable($"## Mode{i}", isSelected, ImGuiSelectableFlags.None, new Vector2(0, 40)))
            {
                _selectedProceduralMode = i;
            }
            ImGui.PopStyleColor(2);

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 38);
            ImGui.Indent(10);
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1), ProceduralModes[i]);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), ProceduralModeDescriptions[i]);
            ImGui.Unindent(10);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
        }
    }

    private void RenderWizardStep_Details()
    {
        ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), "World Details");
        ImGui.Spacing();

        ImGui.Text("World Name:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##WorldName", ref _worldName, 64);
        if (string.IsNullOrWhiteSpace(_worldName))
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Used for file naming and display");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Theme:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##Theme", _themeProvider.GetDisplayName(_availableThemes[_selectedTheme])))
        {
            for (int i = 0; i < _availableThemes.Length; i++)
            {
                var isSelected = _selectedTheme == i;
                if (ImGui.Selectable(_themeProvider.GetDisplayName(_availableThemes[i]), isSelected))
                {
                    _selectedTheme = i;
                }
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Content theme for characters, items, and stories");

        if (!_isRealWorld)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Climate Zone:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##Latitude", Latitudes[_selectedLatitude]))
            {
                for (int i = 0; i < Latitudes.Length; i++)
                {
                    var isSelected = _selectedLatitude == i;
                    if (ImGui.Selectable(Latitudes[i], isSelected))
                    {
                        _selectedLatitude = i;
                    }
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), LatitudeDescriptions[_selectedLatitude]);

            ImGui.Spacing();

            ImGui.Text("World Height:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##WorldHeight", WorldHeights[_selectedWorldHeight]))
            {
                for (int i = 0; i < WorldHeights.Length; i++)
                {
                    var isSelected = _selectedWorldHeight == i;
                    if (ImGui.Selectable(WorldHeights[i], isSelected))
                    {
                        _selectedWorldHeight = i;
                    }
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), WorldHeightDescriptions[_selectedWorldHeight]);
        }
    }

    private void RenderWizardStep_Locations()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.9f, 1f, 1), "Generate Locations");
        ImGui.Spacing();

        // Auto-start AI generation if available and not already started
        if (_aiWorldGenerationService != null &&
            !_locationsValidated &&
            !_isGeneratingLocations &&
            _aiGenerationResult == null)
        {
            GenerateLocationsWithAI();
        }

        // Show generation in progress
        if (_isGeneratingLocations)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1), _aiGenerationStatus);
            ImGui.Spacing();
            ImGui.ProgressBar(-1.0f * (float)ImGui.GetTime() % 1.0f, new Vector2(-1, 0), "Generating...");
            return;
        }

        // Show successful AI result
        if (_aiGenerationResult != null && _aiGenerationResult.IsSuccess)
        {
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1), _aiGenerationResult.Summary);
            ImGui.Spacing();

            // Quality rating based on story count
            var (qualityLabel, qualityColor, qualityStars) = GetGenerationQuality(_aiGenerationResult.StoryCount);
            ImGui.TextColored(qualityColor, $"Quality: {qualityStars} {qualityLabel}");

            if (_aiGenerationResult.StoryCount < 6)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "(Regenerate for more story content)");
            }
            ImGui.Spacing();

            if (!string.IsNullOrEmpty(_aiGenerationResult.ConfigurationPath))
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"Saved to: {Path.GetDirectoryName(_aiGenerationResult.ConfigurationPath)}");
            }
            ImGui.Spacing();

            _locationsValidated = true;
            _locationsStatus = _aiGenerationResult.Summary;

            if (ImGui.Button("Regenerate", new Vector2(150, 30)))
            {
                _aiGenerationResult = null;
                _locationsValidated = false;
                // Will auto-regenerate on next frame
            }
            return;
        }

        // Fallback: No AI service available or AI failed
        if (_aiWorldGenerationService == null)
        {
            ImGui.TextWrapped("AI location generation is not available.");
            ImGui.Spacing();
            RenderManualFallbackOption();
        }
        else if (_aiGenerationResult != null && !_aiGenerationResult.IsSuccess)
        {
            // AI was attempted but failed
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.4f, 1), "Generation failed. You can retry or use manual fallback.");
            if (_aiGenerationResult.ParseErrors?.Count > 0)
            {
                ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1), string.Join("\n", _aiGenerationResult.ParseErrors.Take(3)));
            }
            ImGui.Spacing();

            if (ImGui.Button("Retry AI Generation", new Vector2(200, 30)))
            {
                _aiGenerationResult = null;
                // Will auto-regenerate on next frame
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            RenderManualFallbackOption();
        }
    }

    private void RenderManualFallbackOption()
    {
        ImGui.TextColored(new Vector4(1, 0.9f, 0.6f, 1), "Manual Fallback");
        ImGui.TextWrapped("Copy this prompt to ChatGPT or Claude to generate location XML. Then paste the result into your world's GenerationConfiguration.xml file.");
        ImGui.Spacing();

        var prompt = BuildManualXmlPrompt();

        ImGui.BeginChild("ManualPrompt", new Vector2(0, 150), ImGuiChildFlags.Borders);
        ImGui.TextWrapped(prompt);
        ImGui.EndChild();

        ImGui.Spacing();
        if (ImGui.Button("Copy Prompt to Clipboard", new Vector2(200, 0)))
        {
            ImGui.SetClipboardText(prompt);
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "After copying, click Next to create the world. Paste the AI-generated XML into GenerationConfiguration.xml in your world folder.");
    }

    private string BuildManualXmlPrompt()
    {
        var themeName = _themeProvider.GetDisplayName(_availableThemes[_selectedTheme]);

        // Map "Default" to a classic RPG theme name for better AI generation
        if (themeName.Equals("Default Theme", StringComparison.OrdinalIgnoreCase) ||
            themeName.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            themeName = "Classic Fantasy";
        }

        var worldType = _isRealWorld ? "real" : "procedural";

        double minLat, maxLat, minLon, maxLon, spawnLat, spawnLon;
        if (_isRealWorld && _geoTransform != null && _geoTransform.Length >= 6)
        {
            minLon = _geoTransform[0];
            maxLon = _geoTransform[0] + _terrainWidth * _geoTransform[1];
            maxLat = _geoTransform[3];
            minLat = _geoTransform[3] + _terrainHeight * _geoTransform[5];
            spawnLon = _geoTransform[0] + _selectedSpawnPixel.X * _geoTransform[1];
            spawnLat = _geoTransform[3] + _selectedSpawnPixel.Y * _geoTransform[5];
        }
        else
        {
            minLat = 0; maxLat = 100; minLon = 0; maxLon = 100;
            spawnLat = 50; spawnLon = 50;
        }

        return $@"Generate 40 SourceLocation XML elements for a {themeName} themed {worldType} world.

Bounds: Lat {minLat:F2} to {maxLat:F2}, Lon {minLon:F2} to {maxLon:F2}
Spawn point: ({spawnLat:F2}, {spawnLon:F2})

Each location needs: DisplayName, Description, Category, Kind, Lat, Lon, and a Character with Name, Role, Personality, Greeting.

Valid Categories: Religious, Stronghold, Facility, Landmark, Ruin, Camp, Service, QuestHub, Waypoint, Passage, Infrastructure
Valid Roles: Boss, Merchant, QuestGiver, NPC, Hostile
Valid Personalities: cunning, brave, wise, fierce, gentle, stern, mysterious, cautious, cheerful, gruff

Output format (XML only, no explanation):
<SourceLocation DisplayName=""Example Shrine"" Description=""An ancient shrine"" Category=""Religious"" Kind=""Shrine"" Lat=""{spawnLat:F4}"" Lon=""{spawnLon:F4}"">
  <Character Name=""Priest Tanaka"" Role=""NPC"" Personality=""wise"" Greeting=""Welcome, traveler."" />
</SourceLocation>

Generate 40 diverse locations spread across the map with {themeName} theming.";
    }


    private void GenerateLocationsWithAI()
    {
        if (_aiWorldGenerationService == null || _isGeneratingLocations) return;

        _isGeneratingLocations = true;
        _aiGenerationStatus = "Preparing request...";
        _aiGenerationResult = null;

        // Build the request from current wizard state
        var regionName = !string.IsNullOrWhiteSpace(_worldName) ? _worldName : "Generated World";
        var themeName = _themeProvider.GetDisplayName(_availableThemes[_selectedTheme]);

        // Map "Default" to a classic RPG theme name for better AI generation
        if (themeName.Equals("Default Theme", StringComparison.OrdinalIgnoreCase) ||
            themeName.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            themeName = "Classic Fantasy";
        }

        var worldType = _isRealWorld ? "real" : "procedural";
        var worldRef = GenerateWorldRef(_worldName);

        double minLat, maxLat, minLon, maxLon, spawnLat, spawnLon;

        if (_isRealWorld && _geoTransform != null && _geoTransform.Length >= 6)
        {
            minLon = _geoTransform[0];
            maxLon = _geoTransform[0] + _terrainWidth * _geoTransform[1];
            maxLat = _geoTransform[3];
            minLat = _geoTransform[3] + _terrainHeight * _geoTransform[5];

            spawnLon = _geoTransform[0] + _selectedSpawnPixel.X * _geoTransform[1] + _selectedSpawnPixel.Y * _geoTransform[2];
            spawnLat = _geoTransform[3] + _selectedSpawnPixel.X * _geoTransform[4] + _selectedSpawnPixel.Y * _geoTransform[5];
        }
        else
        {
            // Procedural world - use 0-100 coordinate space
            minLat = 0;
            maxLat = 100;
            minLon = 0;
            maxLon = 100;
            spawnLat = 50;
            spawnLon = 50;
        }

        var request = new LocationGenerationRequest(
            RegionName: regionName,
            Theme: themeName,
            WorldType: worldType,
            MinLatitude: minLat,
            MaxLatitude: maxLat,
            MinLongitude: minLon,
            MaxLongitude: maxLon,
            SpawnLatitude: spawnLat,
            SpawnLongitude: spawnLon,
            LocationCount: 40);

        // Calculate output directory
        var outputDirectory = Path.Combine(
            _gameSettings.GetAppDataContentPath(),
            "worlds",
            worldRef + "_generated",
            "assets", "ambient_games", "xml");

        Task.Run(async () =>
        {
            try
            {
                _aiGenerationStatus = "Authenticating with Steam...";
                System.Diagnostics.Debug.WriteLine($"[WorldCreationWizard] Starting AI generation for '{regionName}'...");

                _aiGenerationStatus = "Generating locations (this may take several minutes)...";
                _aiGenerationResult = await _aiWorldGenerationService.GenerateAndSaveAsync(
                    request,
                    worldRef,
                    regionName,
                    $"AI-generated world based on {themeName}",
                    outputDirectory);

                _aiGenerationStatus = _aiGenerationResult.Summary;
                System.Diagnostics.Debug.WriteLine($"[WorldCreationWizard] AI generation complete: {_aiGenerationResult.Summary}");
            }
            catch (Exception ex)
            {
                _aiGenerationStatus = $"Error: {ex.Message}";
                _aiGenerationResult = new WorldGenerationResult(
                    IsSuccess: false,
                    ConfigurationPath: null,
                    LocationCount: 0,
                    StoryCount: 0,
                    CharacterCount: 0,
                    ParseErrors: new List<string> { ex.Message },
                    Summary: $"Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[WorldCreationWizard] AI generation failed: {ex}");
            }
            finally
            {
                _isGeneratingLocations = false;
            }
        });
    }

    private static string GenerateWorldRef(string worldName)
    {
        if (string.IsNullOrWhiteSpace(worldName))
            return $"world_{DateTime.Now:yyyyMMddHHmmss}";

        // Convert to valid ref name (lowercase, underscores)
        var refName = worldName.ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('-', '_');

        // Remove invalid characters
        refName = new string(refName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        return string.IsNullOrEmpty(refName) ? $"world_{DateTime.Now:yyyyMMddHHmmss}" : refName;
    }

    private void RenderWizardStep_Create()
    {
        ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1), "Ready to Create World");
        ImGui.Spacing();

        ImGui.TextWrapped("Review your world settings below. Click 'Create' to generate the world configuration files.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), "Summary:");
        ImGui.Spacing();

        ImGui.Indent(10);

        ImGui.Text($"World Name: {_worldName}");
        ImGui.Text($"World Type: {(_isRealWorld ? "Real World (DEM)" : "Procedural")}");
        ImGui.Text($"Theme: {_themeProvider.GetDisplayName(_availableThemes[_selectedTheme])}");

        if (_isRealWorld)
        {
            ImGui.Text($"Terrain Size: {_terrainWidth}x{_terrainHeight}");
            ImGui.Text($"Spawn Location: ({(int)_selectedSpawnPixel.X}, {(int)_selectedSpawnPixel.Y})");
        }
        else
        {
            ImGui.Text($"Terrain Style: {ProceduralModes[_selectedProceduralMode]}");
            ImGui.Text($"Climate Zone: {Latitudes[_selectedLatitude]}");
            ImGui.Text($"World Height: {WorldHeightValues[_selectedWorldHeight]}");
        }

        if (_locationsValidated && _aiGenerationResult?.IsSuccess == true)
        {
            ImGui.Text($"Locations: {_aiGenerationResult.Summary}");
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1), "Locations: Manual fallback (paste XML after creation)");
        }

        ImGui.Unindent(10);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var worldRef = SanitizeWorldName(_worldName);
        var generatedWorldRef = worldRef + "_generated";
        var outputPath = Path.Combine(
            _gameSettings.GetAppDataContentPath(),
            "worlds",
            generatedWorldRef);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Files will be created at:");
        ImGui.TextWrapped(outputPath);

        if (_isCreatingWorld && !string.IsNullOrEmpty(_creationStatus))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1), _creationStatus);
        }
    }

    #endregion

    #region Navigation

    private void RenderWizardNavigation(ref bool isOpen, int totalSteps)
    {
        if (ImGui.Button("Cancel", new Vector2(80, 30)))
        {
            Reset();
            isOpen = false;
        }

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X - 180, 0));
        ImGui.SameLine();

        if (_wizardStep > 0)
        {
            if (ImGui.Button("Back", new Vector2(80, 30)))
            {
                _wizardStep--;
            }
            ImGui.SameLine();
        }

        var isLastStep = _wizardStep >= totalSteps - 1;
        var buttonLabel = isLastStep ? "Create" : "Next";
        var canProceed = CanProceedFromCurrentStep();

        if (!canProceed)
        {
            ImGui.BeginDisabled();
        }

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.3f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.6f, 0.4f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.5f, 1));
        if (ImGui.Button(buttonLabel, new Vector2(80, 30)))
        {
            if (isLastStep)
            {
                CreateWorld();
            }
            else
            {
                _wizardStep++;
            }
        }
        ImGui.PopStyleColor(3);

        if (!canProceed)
        {
            ImGui.EndDisabled();
        }
    }

    private bool CanProceedFromCurrentStep()
    {
        if (_isCreatingWorld) return false;

        return _wizardStep switch
        {
            0 => true,
            1 => !_isRealWorld || _terrainValidated,
            2 => !string.IsNullOrWhiteSpace(_worldName),
            3 => CanProceedFromLocationsStep(),
            _ => true
        };
    }

    private bool CanProceedFromLocationsStep()
    {
        // No AI available - can always proceed (manual fallback)
        if (_aiWorldGenerationService == null) return true;

        // AI succeeded
        if (_locationsValidated && _aiGenerationResult?.IsSuccess == true) return true;

        // AI failed - allow proceeding with manual fallback
        if (_aiGenerationResult != null && !_aiGenerationResult.IsSuccess) return true;

        // Still generating or waiting
        return false;
    }

    #endregion

    #region File Handling

    private void BrowseForTerrainFile()
    {
        if (_fileDialogService == null)
        {
            _terrainFileStatus = "File dialog not available on this platform";
            return;
        }

        _fileDialogService.OpenFile(
            "Select Elevation Data File",
            "Elevation files|*.tif;*.tiff;*.tar.gz|GeoTIFF|*.tif;*.tiff|Compressed|*.tar.gz|All files|*.*",
            selectedPath =>
            {
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    _selectedTerrainFile = selectedPath;
                    ValidateTerrainFile();
                }
            });
    }

    private void ValidateTerrainFile()
    {
        _terrainValidated = false;
        _validatedTifPath = "";
        _convertedTifPath = "";
        _terrainWidth = 0;
        _terrainHeight = 0;
        _terrainPreviewImage = null;
        _isLoadingPreview = false;
        _geoTransform = null;
        _heightData = null;
        _heightDataWidth = 0;
        _minElevation = 0;
        _maxElevation = 0;

        if (_terrainTexturePtr != IntPtr.Zero && _terrainTextureResources != null && _textureProvider != null)
        {
            _textureProvider.DisposeTexture(_terrainTextureResources);
            _terrainTextureResources = null;
        }
        _terrainTexturePtr = IntPtr.Zero;

        if (string.IsNullOrEmpty(_selectedTerrainFile))
        {
            _terrainFileStatus = "";
            return;
        }

        if (!File.Exists(_selectedTerrainFile))
        {
            _terrainFileStatus = "Error: File not found";
            return;
        }

        _terrainFileStatus = "Processing terrain file...";
        Task.Run(() => ValidateAndConvertTerrainFileAsync());
    }

    private async Task ValidateAndConvertTerrainFileAsync()
    {
        try
        {
            string sourceTifPath;
            var ext = Path.GetExtension(_selectedTerrainFile).ToLowerInvariant();

            if (ext == ".tif" || ext == ".tiff")
            {
                sourceTifPath = _selectedTerrainFile;
            }
            else if (_selectedTerrainFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                _terrainFileStatus = "Extracting archive...";
                sourceTifPath = ExtractTifFromTarGz(_selectedTerrainFile);
                if (string.IsNullOrEmpty(sourceTifPath))
                {
                    _terrainFileStatus = "Error: No .tif file found in archive";
                    return;
                }
            }
            else
            {
                _terrainFileStatus = "Not a supported file type (.tif, .tiff, .tar.gz)";
                return;
            }

            _terrainFileStatus = "Validating dimensions...";
            var (width, height, error) = ValidateGeoTiffDimensions(sourceTifPath);

            if (!string.IsNullOrEmpty(error))
            {
                _terrainFileStatus = $"Error: {error}";
                return;
            }

            var totalPixels = (long)width * height;
            if (totalPixels > MaxTerrainPixels)
            {
                _terrainFileStatus = $"Error: {width}x{height} = {totalPixels:N0} pixels exceeds max {MaxTerrainPixels:N0}";
                return;
            }

            _terrainWidth = width;
            _terrainHeight = height;

            if (_geoTiffConverter != null && _geoTiffConverter.IsAvailable)
            {
                _terrainFileStatus = "Converting with GDAL...";

                var tempDir = Path.Combine(Path.GetTempPath(), "AmbientTerrainConverted");
                Directory.CreateDirectory(tempDir);
                var convertedPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.tif");

                var success = await _geoTiffConverter.ConvertAsync(
                    sourceTifPath,
                    convertedPath,
                    progress => _terrainFileStatus = $"Converting... {progress * 100:F0}%");

                if (success)
                {
                    _convertedTifPath = convertedPath;
                    _validatedTifPath = convertedPath;
                    _terrainFileStatus = $"Valid: {width}x{height} ({totalPixels:N0} pixels)";

                    var info = _geoTiffConverter.GetInfo(convertedPath);
                    if (info != null)
                    {
                        _geoTransform = info.GeoTransform;
                    }
                }
                else
                {
                    _terrainFileStatus = "Error: GDAL conversion failed";
                    return;
                }
            }
            else
            {
                _validatedTifPath = sourceTifPath;
                _terrainFileStatus = $"Valid: {width}x{height} (no GDAL - using raw file)";
            }

            _terrainValidated = true;
            GenerateTerrainPreview(_validatedTifPath);
        }
        catch (Exception ex)
        {
            _terrainFileStatus = $"Error: {ex.Message}";
        }
    }

    private void GenerateTerrainPreview(string tifPath)
    {
        if (_isLoadingPreview) return;
        _isLoadingPreview = true;
        _terrainFileStatus = "Loading terrain preview...";

        Task.Run(() =>
        {
            try
            {
                using var image = Image.Load<L16>(tifPath);

                _heightDataWidth = image.Width;
                _heightData = new ushort[image.Width * image.Height];
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            _heightData[y * image.Width + x] = row[x].PackedValue;
                        }
                    }
                });

                _minElevation = int.MaxValue;
                _maxElevation = int.MinValue;
                foreach (var height in _heightData)
                {
                    if (height < _minElevation) _minElevation = height;
                    if (height > _maxElevation) _maxElevation = height;
                }

                var processedMap = HeightMapProcessor.ProcessHeightMap(image, minWaterAreaSize: 50, adjustMinWaterAreaSizeByElevation: true, verticalShift: 0);
                var imageData = ConvertProcessedMapToImageData(processedMap);

                _terrainPreviewImage = imageData;
                _selectedSpawnPixel = new Vector2(processedMap.Width / 2f, processedMap.Height / 2f);
                _spawnSelected = true;
                _terrainFileStatus = $"Valid: {_terrainWidth}x{_terrainHeight} - Click map to set spawn";
                _isLoadingPreview = false;
            }
            catch (Exception ex)
            {
                _terrainFileStatus = $"Preview error: {ex.Message}";
                _isLoadingPreview = false;
            }
        });
    }

    private void CreateTerrainTexture()
    {
        if (_terrainPreviewImage != null && _terrainTexturePtr == IntPtr.Zero && _textureProvider != null)
        {
            if (_terrainTextureResources != null)
            {
                _textureProvider.DisposeTexture(_terrainTextureResources);
                _terrainTextureResources = null;
            }

            var (texturePtr, _, _, resources) = _textureProvider.CreateTextureFromImageData(_terrainPreviewImage);
            _terrainTexturePtr = texturePtr;
            _terrainTextureResources = resources;
        }
    }

    private static HeightMapImageData ConvertProcessedMapToImageData(HeightMapProcessor.ProcessedHeightMap processedMap)
    {
        var width = processedMap.Width;
        var height = processedMap.Height;
        var stride = width * 4;
        var pixelData = new byte[height * stride];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var color = HeightMapProcessor.GetElevationColorWithWater(x, y, processedMap);
                var index = y * stride + x * 4;
                pixelData[index] = color.B;
                pixelData[index + 1] = color.G;
                pixelData[index + 2] = color.R;
                pixelData[index + 3] = 255;
            }
        }

        return new HeightMapImageData(pixelData, width, height, stride);
    }

    private string ExtractTifFromTarGz(string tarGzPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AmbientTerrainValidation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        using var stream = File.OpenRead(tarGzPath);
        using var reader = ReaderFactory.Open(stream);

        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;

            var entryName = reader.Entry.Key ?? "";
            var ext = Path.GetExtension(entryName).ToLowerInvariant();

            if (ext == ".tif" || ext == ".tiff")
            {
                var outputPath = Path.Combine(tempDir, Path.GetFileName(entryName));
                reader.WriteEntryToFile(outputPath);
                return outputPath;
            }
        }

        return "";
    }

    private static (int width, int height, string? error) ValidateGeoTiffDimensions(string tifPath)
    {
        try
        {
            using var tiff = Tiff.Open(tifPath, "r");
            if (tiff == null)
                return (0, 0, "Cannot open TIFF file");

            var widthField = tiff.GetField(TiffTag.IMAGEWIDTH);
            var heightField = tiff.GetField(TiffTag.IMAGELENGTH);

            if (widthField == null || heightField == null)
                return (0, 0, "Cannot read image dimensions");

            var width = widthField[0].ToInt();
            var height = heightField[0].ToInt();

            return (width, height, null);
        }
        catch (Exception ex)
        {
            return (0, 0, ex.Message);
        }
    }

    #endregion

    #region World Creation

    private void CreateWorld()
    {
        if (_isCreatingWorld) return;

        _isCreatingWorld = true;
        _creationStatus = "Creating world...";

        // Capture state for async task
        var worldName = _worldName;
        var worldRef = SanitizeWorldName(_worldName);
        var isRealWorld = _isRealWorld;
        var validatedTifPath = _validatedTifPath;
        var theme = _availableThemes[_selectedTheme];
        var proceduralMode = ProceduralModes[_selectedProceduralMode];
        var geoTransform = _geoTransform;
        var selectedSpawnPixel = _selectedSpawnPixel;
        var minElevation = _minElevation;
        var maxElevation = _maxElevation;

        double spawnLat, spawnLon;
        int chunkHeight;
        if (isRealWorld && geoTransform != null && geoTransform.Length >= 6)
        {
            spawnLon = geoTransform[0] + selectedSpawnPixel.X * geoTransform[1] + selectedSpawnPixel.Y * geoTransform[2];
            spawnLat = geoTransform[3] + selectedSpawnPixel.X * geoTransform[4] + selectedSpawnPixel.Y * geoTransform[5];
            chunkHeight = WorldConfigurationBuilder.CalculateChunkHeight(minElevation, maxElevation);
        }
        else
        {
            spawnLat = LatitudeNumericValues[_selectedLatitude];
            spawnLon = 0;
            chunkHeight = WorldHeightValues[_selectedWorldHeight];
        }

        // Locations are saved to GenerationConfiguration.xml by AI service
        // WorldCreationService just creates WorldConfiguration.xml
        var parameters = new WorldCreationParameters
        {
            WorldRef = worldRef,
            DisplayName = worldName,
            Theme = theme,
            IsRealWorld = isRealWorld,
            TerrainFilePath = validatedTifPath,
            MinElevation = minElevation,
            MaxElevation = maxElevation,
            GeoTransform = geoTransform,
            ProceduralMode = proceduralMode,
            SpawnLatitude = spawnLat,
            SpawnLongitude = spawnLon,
            ChunkHeight = chunkHeight,
            Locations = new List<Ambient.Application.WorldCreation.LocationEntry>(),
            LocationGenerationType = _aiGenerationResult?.IsSuccess == true ? "ai" : "none"
        };

        var appDataContentPath = _gameSettings.GetAppDataContentPath();

        Task.Run(async () =>
        {
            var result = await _worldCreationService.CreateWorldAsync(
                parameters,
                appDataContentPath,
                status => _creationStatus = status);

            _isCreatingWorld = false;
            _creationStatus = "";

            if (result.Success)
            {
                // Event handler (in parent) is responsible for closing the dialog
                WorldCreated?.Invoke(result.OutputPath ?? "");
            }
        });
    }

    private static string SanitizeWorldName(string name)
    {
        var sanitized = name.ToLowerInvariant()
            .Replace(" ", "_")
            .Replace("-", "_");

        var chars = sanitized.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray();
        return new string(chars);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening URL
        }
    }

    /// <summary>
    /// Returns quality rating based on story count.
    /// 6+ stories = Excellent, 3-5 = Good, 1-2 = Fair, 0 = Basic
    /// </summary>
    private static (string label, Vector4 color, string stars) GetGenerationQuality(int storyCount)
    {
        return storyCount switch
        {
            >= 6 => ("Excellent", new Vector4(0.4f, 1f, 0.4f, 1), "[***]"),
            >= 3 => ("Good", new Vector4(0.9f, 0.9f, 0.4f, 1), "[** ]"),
            >= 1 => ("Fair", new Vector4(1f, 0.7f, 0.4f, 1), "[*  ]"),
            _ => ("Basic", new Vector4(0.6f, 0.6f, 0.6f, 1), "[   ]")
        };
    }

    #endregion
}
