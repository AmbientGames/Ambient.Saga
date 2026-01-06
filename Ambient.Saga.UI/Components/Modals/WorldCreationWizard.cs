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
    private readonly ILocationGenerator? _locationGenerator;
    private ITextureProvider? _textureProvider;
    private readonly ILogger<WorldCreationWizard>? _logger;

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

    // Location generation settings
    private double _spawnLatitude;
    private double _spawnLongitude;

    // Step 3: World details state
    private string _worldName = "";
    private int _selectedLatitude = 0;
    private int _selectedWorldHeight = 0;
    private string[] _availableThemes = Array.Empty<string>();
    private int _selectedTheme = 0;

    // Step 4: Locations state
    private string _selectedLocationsFile = "";
    private string _locationsFileStatus = "";
    private bool _locationsValidated = false;
    private int _locationsCount = 0;
    private List<LocationEntry> _importedLocations = new();

    // AI prompt state
    private string _aiPromptText = "";
    private bool _aiPromptCopied = false;

    // AI generation state
    private bool _isGeneratingLocations;
    private string _aiGenerationStatus = "";
    private LocationGenerationResponse? _aiGenerationResult;
    private List<GeneratedLocationEntry> _aiGeneratedLocations = new();

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

    // Location entry from CSV
    private class LocationEntry
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Category { get; set; } = "Default";
        public string Kind { get; set; } = "Default";
    }

    public WorldCreationWizard(
        IGameSettings gameSettings,
        IThemeProvider themeProvider,
        IWorldCreationService worldCreationService,
        IFileDialogService? fileDialogService = null,
        IGeoTiffConverter? geoTiffConverter = null,
        ITextureProvider? textureProvider = null,
        ILocationGenerator? locationGenerator = null,
        ILogger<WorldCreationWizard>? logger = null)
    {
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _themeProvider = themeProvider ?? throw new ArgumentNullException(nameof(themeProvider));
        _worldCreationService = worldCreationService ?? throw new ArgumentNullException(nameof(worldCreationService));
        _fileDialogService = fileDialogService;
        _geoTiffConverter = geoTiffConverter;
        _textureProvider = textureProvider;
        _locationGenerator = locationGenerator;
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
        _selectedLocationsFile = "";
        _locationsFileStatus = "";
        _locationsValidated = false;
        _locationsCount = 0;
        _importedLocations.Clear();
        _isGeneratingLocations = false;
        _aiGenerationStatus = "";
        _aiGenerationResult = null;
        _aiGeneratedLocations.Clear();
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
        ImGui.TextColored(new Vector4(0.8f, 0.9f, 1f, 1), "Source Locations (Optional)");
        ImGui.Spacing();

        ImGui.TextWrapped("Generate or import locations to place points of interest in your world. This step is optional - you can add locations later.");
        ImGui.Spacing();

        if (ImGui.BeginTabBar("LocationsTabs"))
        {
            // Show AI Generate tab first if generator is available
            if (_locationGenerator != null)
            {
                if (ImGui.BeginTabItem("AI Generate"))
                {
                    RenderLocationsAIGenerateTab();
                    ImGui.EndTabItem();
                }
            }
            if (ImGui.BeginTabItem("CSV Import"))
            {
                RenderLocationsTemplateTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("AI Prompt (Manual)"))
            {
                RenderLocationsAIPromptTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 90);
        ImGui.InputText("##LocationsFile", ref _selectedLocationsFile, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse...", new Vector2(80, 0)))
        {
            BrowseForLocationsFile();
        }

        if (!string.IsNullOrEmpty(_locationsFileStatus))
        {
            ImGui.Spacing();
            var statusColor = _locationsFileStatus.StartsWith("Error")
                ? new Vector4(1, 0.4f, 0.4f, 1)
                : new Vector4(0.4f, 1, 0.4f, 1);
            ImGui.TextColored(statusColor, _locationsFileStatus);
        }

        if (_locationsValidated && _importedLocations.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Text($"Preview ({Math.Min(_importedLocations.Count, 5)} of {_importedLocations.Count}):");

            ImGui.BeginChild("LocationsPreview", new Vector2(0, 100), ImGuiChildFlags.Borders);
            for (int i = 0; i < Math.Min(_importedLocations.Count, 5); i++)
            {
                var loc = _importedLocations[i];
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.7f, 1), loc.Name);
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1),
                    $"({loc.Latitude:F4}, {loc.Longitude:F4}) [{loc.Category}/{loc.Kind}]");
            }
            if (_importedLocations.Count > 5)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"... and {_importedLocations.Count - 5} more");
            }
            ImGui.EndChild();
        }

        if (_locationsValidated)
        {
            ImGui.Spacing();
            if (ImGui.Button("Clear Locations"))
            {
                _selectedLocationsFile = "";
                _locationsFileStatus = "";
                _locationsValidated = false;
                _locationsCount = 0;
                _importedLocations.Clear();
            }
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "You can skip this step if you don't have location data.");
    }

    private void RenderLocationsTemplateTab()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1, 0.8f, 0.4f, 1), "Required CSV Headers:");
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1), "name,description,latitude,longitude,category,kind");
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(1, 0.8f, 0.4f, 1), "Example Row:");
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Ise Grand Shrine,Ancient Shinto shrine,34.4550,136.7258,Religious,Shrine");
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(1, 0.8f, 0.4f, 1), "Valid Categories:");
        ImGui.BeginChild("CategoryList", new Vector2(0, 120), ImGuiChildFlags.Borders);

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1), "Religious");
        ImGui.SameLine(120); ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Kinds: Shrine, Temple, Church, Monastery");

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1), "Stronghold");
        ImGui.SameLine(120); ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Kinds: Castle, Fortress, Keep, Watchtower");

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1), "Facility");
        ImGui.SameLine(120); ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Kinds: Market, Inn, Blacksmith, Hospital");

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1), "Landmark");
        ImGui.SameLine(120); ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Kinds: Monument, Statue, Viewpoint, Peak");

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1), "Ruin");
        ImGui.SameLine(120); ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Kinds: AncientRuin, Battlefield, Tomb");

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1), "Infrastructure");
        ImGui.SameLine(120); ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Kinds: Bridge, Port, Well, Road");

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1), "Camp");
        ImGui.SameLine(120); ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Kinds: BaseCamp, Campsite, Outpost");

        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1), "Service");
        ImGui.SameLine(120); ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Kinds: Merchant, Healer, Trainer");

        ImGui.EndChild();
    }

    private void RenderLocationsAIGenerateTab()
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 1f, 0.8f, 1), "Generate Locations with AI");
        ImGui.Spacing();

        ImGui.TextWrapped("Click the button below to generate themed locations, characters, and story assignments using AI. This requires Steam authentication and an internet connection.");
        ImGui.Spacing();

        // Show current settings that will be used
        var themeName = _themeProvider.GetDisplayName(_availableThemes[_selectedTheme]);
        var worldType = _isRealWorld ? "Real World" : "Procedural";

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"World Type: {worldType}");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"Theme: {themeName}");

        if (_isRealWorld && _geoTransform != null && _geoTransform.Length >= 6)
        {
            var minLat = _geoTransform[3] + _terrainHeight * _geoTransform[5];
            var maxLat = _geoTransform[3];
            var minLon = _geoTransform[0];
            var maxLon = _geoTransform[0] + _terrainWidth * _geoTransform[1];
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"Bounds: ({minLat:F2}, {minLon:F2}) to ({maxLat:F2}, {maxLon:F2})");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Generation button
        if (_isGeneratingLocations)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Generating...", new Vector2(-1, 40));
            ImGui.EndDisabled();

            if (!string.IsNullOrEmpty(_aiGenerationStatus))
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1), _aiGenerationStatus);
            }
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.7f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.6f, 0.8f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.9f, 1));
            if (ImGui.Button("Generate Locations with AI", new Vector2(-1, 40)))
            {
                GenerateLocationsWithAI();
            }
            ImGui.PopStyleColor(3);
        }

        // Show results
        if (_aiGeneratedLocations.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var storyLocations = _aiGeneratedLocations.Where(l => l.StoryAssignment != null).ToList();
            var uniqueStories = storyLocations.Select(l => l.StoryAssignment!.Story).Distinct().Count();

            ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1),
                $"Generated {_aiGeneratedLocations.Count} locations ({storyLocations.Count} with story assignments, {uniqueStories} unique stories)");
            ImGui.Spacing();

            ImGui.Text($"Preview (first 5 of {_aiGeneratedLocations.Count}):");
            ImGui.BeginChild("AILocationsPreview", new Vector2(0, 120), ImGuiChildFlags.Borders);

            for (int i = 0; i < Math.Min(_aiGeneratedLocations.Count, 5); i++)
            {
                var loc = _aiGeneratedLocations[i];
                var storyMarker = loc.StoryAssignment != null ? $" [STORY: {loc.StoryAssignment.Story}]" : "";

                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.7f, 1), loc.Name);
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1),
                    $"({loc.Category}/{loc.Kind}){storyMarker}");

                if (loc.Character != null)
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1),
                        $"  Character: {loc.Character.Name} ({loc.Character.Role})");
                }
            }

            if (_aiGeneratedLocations.Count > 5)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"... and {_aiGeneratedLocations.Count - 5} more");
            }
            ImGui.EndChild();

            ImGui.Spacing();
            if (ImGui.Button("Use These Locations"))
            {
                UseAIGeneratedLocations();
            }
            ImGui.SameLine();
            if (ImGui.Button("Regenerate"))
            {
                GenerateLocationsWithAI();
            }
        }

        // Show parse errors if any
        if (_aiGenerationResult?.ParseErrors?.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.4f, 1), $"Parse warnings ({_aiGenerationResult.ParseErrors.Count}):");
            ImGui.BeginChild("ParseErrors", new Vector2(0, 60), ImGuiChildFlags.Borders);
            foreach (var err in _aiGenerationResult.ParseErrors.Take(3))
            {
                ImGui.TextColored(new Vector4(0.8f, 0.5f, 0.3f, 1), $"- {err}");
            }
            ImGui.EndChild();
        }
    }

    private void GenerateLocationsWithAI()
    {
        if (_locationGenerator == null || _isGeneratingLocations) return;

        _isGeneratingLocations = true;
        _aiGenerationStatus = "Preparing request...";
        _aiGeneratedLocations.Clear();
        _aiGenerationResult = null;

        // Build the request from current wizard state
        var regionName = !string.IsNullOrWhiteSpace(_worldName) ? _worldName : "Generated World";
        var themeName = _themeProvider.GetDisplayName(_availableThemes[_selectedTheme]);
        var worldType = _isRealWorld ? "real" : "procedural";

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

        Task.Run(async () =>
        {
            try
            {
                _aiGenerationStatus = "Authenticating with Steam...";
                System.Diagnostics.Debug.WriteLine($"[WorldCreationWizard] Starting AI generation for '{regionName}'...");

                _aiGenerationStatus = "Generating locations (this may take a minute)...";
                var response = await _locationGenerator.GenerateAsync(request);

                _aiGenerationResult = response;
                _aiGeneratedLocations = response.Locations.ToList();

                var storyCount = _aiGeneratedLocations.Count(l => l.StoryAssignment != null);
                _aiGenerationStatus = $"Generated {_aiGeneratedLocations.Count} locations with {storyCount} story assignments";

                System.Diagnostics.Debug.WriteLine($"[WorldCreationWizard] AI generation complete: {_aiGeneratedLocations.Count} locations");
            }
            catch (Exception ex)
            {
                _aiGenerationStatus = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[WorldCreationWizard] AI generation failed: {ex}");
            }
            finally
            {
                _isGeneratingLocations = false;
            }
        });
    }

    private void UseAIGeneratedLocations()
    {
        // Convert AI locations to the wizard's internal format and mark as validated
        _importedLocations.Clear();

        foreach (var aiLoc in _aiGeneratedLocations)
        {
            _importedLocations.Add(new LocationEntry
            {
                Name = aiLoc.Name,
                Description = aiLoc.Description,
                Latitude = aiLoc.Latitude,
                Longitude = aiLoc.Longitude,
                Category = aiLoc.Category,
                Kind = aiLoc.Kind
            });
        }

        _locationsCount = _importedLocations.Count;
        _locationsValidated = true;
        _locationsFileStatus = $"Using {_locationsCount} AI-generated locations";
        _selectedLocationsFile = "(AI Generated)";

        System.Diagnostics.Debug.WriteLine($"[WorldCreationWizard] Using {_locationsCount} AI-generated locations");
    }

    private void RenderLocationsAIPromptTab()
    {
        ImGui.Spacing();
        ImGui.TextWrapped("Copy this prompt to ChatGPT, Claude, or another AI to generate location data for your world:");
        ImGui.Spacing();

        string prompt;

        if (_isRealWorld && _geoTransform != null && _geoTransform.Length >= 6 && _terrainWidth > 0 && _terrainHeight > 0)
        {
            var minLon = _geoTransform[0];
            var maxLon = _geoTransform[0] + _terrainWidth * _geoTransform[1];
            var maxLat = _geoTransform[3];
            var minLat = _geoTransform[3] + _terrainHeight * _geoTransform[5];

            _spawnLongitude = _geoTransform[0] + _selectedSpawnPixel.X * _geoTransform[1] + _selectedSpawnPixel.Y * _geoTransform[2];
            _spawnLatitude = _geoTransform[3] + _selectedSpawnPixel.X * _geoTransform[4] + _selectedSpawnPixel.Y * _geoTransform[5];

            prompt = $@"Generate a CSV file with points of interest for a game set in this real-world area:

Map bounds:
- Latitude: {minLat:F4} to {maxLat:F4}
- Longitude: {minLon:F4} to {maxLon:F4}

Player spawn point: {_spawnLatitude:F4}, {_spawnLongitude:F4}

Include a mix of:
- Historical/cultural sites (shrines, temples, castles, monuments)
- Scenic locations (viewpoints, peaks, waterfalls)
- Points of interest (interesting places to explore)

Locations should form a natural exploration path radiating outward from the spawn point.

Required CSV headers (first row):
name,description,latitude,longitude,category,kind

Valid categories: Religious, Stronghold, Facility, Landmark, Ruin, Infrastructure, Camp, Service, Passage, Waypoint
Example kinds: Shrine, Temple, Castle, Monument, Viewpoint, Peak, Bridge, Cave, Inn, Market

Generate 20-30 locations with accurate real-world GPS coordinates within the map bounds. Place interesting locations near the spawn point for early exploration.

Output only the CSV data, no explanation.";
        }
        else
        {
            var themeName = _themeProvider.GetDisplayName(_availableThemes[_selectedTheme]);
            _spawnLatitude = 35.0;
            _spawnLongitude = 135.0;
            var latRange = 1.0;

            prompt = $@"Generate a CSV file with fictional points of interest for a {themeName}-themed fantasy game world.

Player spawn point: {_spawnLatitude:F4}, {_spawnLongitude:F4}
Area: Within ~{latRange} degree in each direction from spawn

Create {themeName}-themed locations such as:
- Sacred sites appropriate to the culture
- Strongholds and defensive structures
- Natural landmarks and mysterious locations
- Villages, markets, and service facilities

Locations should form a natural exploration path radiating outward from the spawn point, with easier/friendlier locations near spawn and more challenging ones further out.

Required CSV headers (first row):
name,description,latitude,longitude,category,kind

Valid categories: Religious, Stronghold, Facility, Landmark, Ruin, Infrastructure, Camp, Service, Passage, Waypoint
Example kinds: Shrine, Temple, Castle, Monument, Viewpoint, Peak, Bridge, Cave, Inn, Market

Generate 20-30 diverse locations. Give each a creative {themeName}-appropriate name and brief description.

Output only the CSV data, no explanation.";
        }

        _aiPromptText = prompt;

        ImGui.BeginChild("AIPrompt", new Vector2(0, 180), ImGuiChildFlags.Borders);
        ImGui.TextWrapped(prompt);
        ImGui.EndChild();

        ImGui.Spacing();
        if (ImGui.Button("Copy to Clipboard", new Vector2(150, 0)))
        {
            ImGui.SetClipboardText(_aiPromptText);
            _aiPromptCopied = true;
        }
        if (_aiPromptCopied)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1), "Copied!");
        }
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

        if (_locationsValidated && _importedLocations.Count > 0)
        {
            ImGui.Text($"Locations: {_importedLocations.Count} imported");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Locations: None");
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
            _ => true
        };
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

    private void BrowseForLocationsFile()
    {
        if (_fileDialogService == null)
        {
            _locationsFileStatus = "File dialog not available on this platform";
            return;
        }

        _fileDialogService.OpenFile(
            "Select Locations CSV File",
            "CSV files|*.csv|All files|*.*",
            selectedPath =>
            {
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    _selectedLocationsFile = selectedPath;
                    ValidateLocationsFile();
                }
            });
    }

    private void ValidateLocationsFile()
    {
        _locationsValidated = false;
        _locationsCount = 0;
        _importedLocations.Clear();

        if (string.IsNullOrEmpty(_selectedLocationsFile))
        {
            _locationsFileStatus = "";
            return;
        }

        if (!File.Exists(_selectedLocationsFile))
        {
            _locationsFileStatus = "Error: File not found";
            return;
        }

        try
        {
            var lines = File.ReadAllLines(_selectedLocationsFile);
            var startLine = 0;

            if (lines.Length > 0)
            {
                var firstLine = lines[0].ToLowerInvariant();
                if (firstLine.Contains("name") || firstLine.Contains("latitude") || firstLine.Contains("longitude"))
                {
                    startLine = 1;
                }
            }

            var isNewFormat = false;
            if (startLine == 1 && lines.Length > 0)
            {
                var header = lines[0].ToLowerInvariant();
                isNewFormat = header.Contains("description") && header.Contains("category");
            }

            for (int i = startLine; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var parts = line.Split(',');

                if (isNewFormat && parts.Length >= 6)
                {
                    if (double.TryParse(parts[2].Trim(), out var lat) &&
                        double.TryParse(parts[3].Trim(), out var lon))
                    {
                        _importedLocations.Add(new LocationEntry
                        {
                            Name = parts[0].Trim().Trim('"'),
                            Description = parts[1].Trim().Trim('"'),
                            Latitude = lat,
                            Longitude = lon,
                            Category = parts[4].Trim().Trim('"'),
                            Kind = parts[5].Trim().Trim('"')
                        });
                    }
                }
                else if (parts.Length >= 3)
                {
                    if (double.TryParse(parts[1].Trim(), out var lat) &&
                        double.TryParse(parts[2].Trim(), out var lon))
                    {
                        var type = parts.Length > 3 ? parts[3].Trim().Trim('"') : "Default";
                        _importedLocations.Add(new LocationEntry
                        {
                            Name = parts[0].Trim().Trim('"'),
                            Description = "",
                            Latitude = lat,
                            Longitude = lon,
                            Category = type,
                            Kind = "Default"
                        });
                    }
                }
            }

            if (_importedLocations.Count == 0)
            {
                _locationsFileStatus = "Error: No valid locations found in CSV";
                return;
            }

            _locationsCount = _importedLocations.Count;
            _locationsValidated = true;
            _locationsFileStatus = $"Imported {_locationsCount} locations";
        }
        catch (Exception ex)
        {
            _locationsFileStatus = $"Error: {ex.Message}";
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

        string locationGenerationType;
        List<Ambient.Application.WorldCreation.LocationEntry> locations;
        if (_importedLocations.Count > 0)
        {
            locationGenerationType = "trail";
            locations = _importedLocations.Select(l => new Ambient.Application.WorldCreation.LocationEntry
            {
                Name = l.Name,
                Description = l.Description,
                Latitude = l.Latitude,
                Longitude = l.Longitude,
                Category = l.Category,
                Kind = l.Kind
            }).ToList();
        }
        else
        {
            locationGenerationType = "radial";
            locations = GenerateDefaultRadialLocations().Select(l => new Ambient.Application.WorldCreation.LocationEntry
            {
                Name = l.Name,
                Description = l.Description,
                Latitude = l.Latitude,
                Longitude = l.Longitude,
                Category = l.Category,
                Kind = l.Kind
            }).ToList();
        }

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
            Locations = locations,
            LocationGenerationType = locationGenerationType
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

    private List<LocationEntry> GenerateDefaultRadialLocations()
    {
        var locations = new List<LocationEntry>();
        var random = new Random();

        if (_geoTransform != null && _geoTransform.Length >= 6)
        {
            _spawnLongitude = _geoTransform[0] + _selectedSpawnPixel.X * _geoTransform[1] + _selectedSpawnPixel.Y * _geoTransform[2];
            _spawnLatitude = _geoTransform[3] + _selectedSpawnPixel.X * _geoTransform[4] + _selectedSpawnPixel.Y * _geoTransform[5];
        }
        else
        {
            _spawnLatitude = 35.0;
            _spawnLongitude = 135.0;
        }

        var angles = new[] { 0.0, 120.0, 240.0 };
        var categories = new[] { "Landmark", "Religious", "Stronghold" };
        var kinds = new[] { "Peak", "Shrine", "Ruin" };
        var names = new[] { "Distant Peak", "Ancient Shrine", "Forgotten Fortress" };
        var descriptions = new[] {
            "A towering peak visible from afar",
            "A sacred site of forgotten rituals",
            "Crumbling walls of an ancient stronghold"
        };

        for (int i = 0; i < 3; i++)
        {
            var angleRad = angles[i] * Math.PI / 180.0;
            var distance = 0.15 + random.NextDouble() * 0.15;

            var lat = _spawnLatitude + distance * Math.Cos(angleRad);
            var lon = _spawnLongitude + distance * Math.Sin(angleRad);

            locations.Add(new LocationEntry
            {
                Name = names[i],
                Description = descriptions[i],
                Latitude = lat,
                Longitude = lon,
                Category = categories[i],
                Kind = kinds[i]
            });
        }

        return locations;
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

    #endregion
}
