using Ambient.Application.Contracts;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Presentation.UI.ViewModels;
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
/// World selection screen for choosing which world to load.
///
/// USAGE:
/// - Sandbox: Shows at startup before world is loaded
/// - Game: Optional - can be used for "Select World" menu option or multiplayer lobby
///
/// PATTERN:
/// - Follows ModalManager pattern with ref bool isOpen parameter
/// - Closes automatically when world is loaded (via LoadSelectedConfigurationCommand)
/// - Can also be manually closed by user clicking X button
/// </summary>
public class WorldSelectionScreen
{
    private readonly IWorldContentGenerator _worldContentGenerator;
    private readonly IGameSettings _gameSettings;
    private readonly IFileDialogService? _fileDialogService;
    private readonly IGeoTiffConverter? _geoTiffConverter;
    private ITextureProvider? _textureProvider;
    private readonly ILogger<WorldSelectionScreen>? _logger;
    private string? _lastGenerationMessage;
    private bool _showGenerationMessage;
    private Task? _generationTask;
    private bool _isGenerating;
    private bool _showWorldCreationWizard;

    // Wizard state
    private int _wizardStep = 0;
    private bool _isRealWorld = true; // true = Real World, false = Procedural

    // Step 2: Terrain state
    private string _selectedTerrainFile = "";
    private string _terrainFileStatus = "";
    private bool _terrainValidated = false;
    private string _validatedTifPath = ""; // Path to validated .tif (may be extracted from tar.gz)
    private int _terrainWidth = 0;
    private int _terrainHeight = 0;
    private const int MaxTerrainPixels = 3600 * 3600; // 12,960,000 max pixels
    private int _selectedProceduralMode = 0;

    // Terrain preview and spawn selection
    private HeightMapImageData? _terrainPreviewImage;
    private nint _terrainTexturePtr;
    private IDisposable[]? _terrainTextureResources;
    private Vector2 _selectedSpawnPixel = Vector2.Zero; // Pixel coordinates on map
    private bool _spawnSelected = false;

    // GeoTIFF metadata for coordinate/height display
    private double[]? _geoTransform; // [originX, pixelWidth, rotX, originY, rotY, pixelHeight]
    private ushort[]? _heightData; // Raw height values for lookup
    private int _heightDataWidth; // Width of height data array

    // Location generation settings
    private string _locationGenerationType = "trail"; // "trail" or "radial"
    private double _spawnLatitude; // GPS latitude of spawn point
    private double _spawnLongitude; // GPS longitude of spawn point

    private static readonly string[] ProceduralModes = new[]
    {
        "Rugged", "Rolling", "Extreme"
    };
    private static readonly string[] ProceduralModeDescriptions = new[]
    {
        "Dramatic peaks and deep valleys - challenging terrain",
        "Gentle hills and valleys - balanced exploration",
        "Extreme height variations - for the adventurous"
    };

    // Step 3: World details state
    private string _worldName = "";
    private int _selectedLatitude = 0;
    private int _selectedWorldHeight = 0;
    private static readonly string[] Latitudes = new[]
    {
        "Equatorial (0°)", "Sub-Tropical (30°)", "Temperate (45°)", "Sub-Arctic (60°)"
    };
    private static readonly string[] LatitudeValues = new[] { "Lat0", "Lat30", "Lat45", "Lat60" };
    private static readonly string[] LatitudeDescriptions = new[]
    {
        "Tropical climate - lush vegetation, warm year-round",
        "Warm climate - distinct wet/dry seasons",
        "Moderate climate - four seasons, mixed forests",
        "Cold climate - long winters, coniferous forests"
    };
    private static readonly string[] WorldHeights = new[]
    {
        "256 (Fast)", "512 (Standard)", "1024 (Detailed)"
    };
    private static readonly int[] WorldHeightValues = new[] { 256, 512, 1024 };
    private static readonly string[] WorldHeightDescriptions = new[]
    {
        "Quick generation, lower detail",
        "Balanced performance and detail",
        "High detail, slower generation"
    };

    public WorldSelectionScreen(
        IWorldContentGenerator worldContentGenerator,
        IGameSettings gameSettings,
        IFileDialogService? fileDialogService = null,
        IGeoTiffConverter? geoTiffConverter = null,
        ITextureProvider? textureProvider = null,
        ILogger<WorldSelectionScreen>? logger = null)
    {
        _worldContentGenerator = worldContentGenerator ?? throw new ArgumentNullException(nameof(worldContentGenerator));
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _fileDialogService = fileDialogService;
        _geoTiffConverter = geoTiffConverter;
        _textureProvider = textureProvider;
        _logger = logger;
    }

    /// <summary>
    /// Sets the texture provider for rendering terrain previews.
    /// Call this after the graphics device is available.
    /// </summary>
    public void SetTextureProvider(ITextureProvider textureProvider)
    {
        _textureProvider = textureProvider;
    }

    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        // Center the selection window
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(viewport.Size.X * 0.5f, viewport.Size.Y * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(600, 461), ImGuiCond.Always);

        // NoTitleBar removes the close box - world selection is mandatory in sandbox
        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar;
        
        if (!ImGui.Begin("World Selection", windowFlags))
        {
            ImGui.End();
            return;
        }

        ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Select a World to Load");
        ImGui.TextColored(new Vector4(1, 0.7f, 0.3f, 1), "? You must select and load a world to continue");
        ImGui.Separator();
        ImGui.Spacing();

        // World configuration selection
        ImGui.Text("Configuration:");
        if (ImGui.BeginCombo("##WorldConfig", viewModel.SelectedConfiguration?.RefName ?? "Select world..."))
        {
            foreach (var config in viewModel.AvailableConfigurations)
            {
                var isSelected = viewModel.SelectedConfiguration?.RefName == config.RefName;
                if (ImGui.Selectable(config.RefName, isSelected))
                {
                    viewModel.SelectedConfiguration = config;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.3f, 0.5f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.4f, 0.6f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.5f, 0.7f, 1));
        if (ImGui.Button("+ New", new Vector2(60, 0)))
        {
            _showWorldCreationWizard = true;
        }
        ImGui.PopStyleColor(3);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Display selected configuration info
        if (viewModel.SelectedConfiguration != null)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Selected World:");
            ImGui.Indent(10);
            ImGui.Text($"Name: {viewModel.SelectedConfiguration.RefName}");
            ImGui.Text($"Display Name: {viewModel.SelectedConfiguration.DisplayName ?? "N/A"}");

            if (!string.IsNullOrEmpty(viewModel.SelectedConfiguration.Description))
            {
                ImGui.Spacing();
                ImGui.TextWrapped(viewModel.SelectedConfiguration.Description);
            }
            ImGui.Unindent(10);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // World Content Generation section
            ImGui.TextColored(new Vector4(1, 0.647f, 0, 1), "World Content Generation:");
            ImGui.Spacing();

            if (_worldContentGenerator.IsAvailable)
            {
                // Check if generation task completed
                if (_isGenerating && _generationTask != null && _generationTask.IsCompleted)
                {
                    _isGenerating = false;
                    _generationTask = null;
                }

                // Show generating indicator or button
                if (_isGenerating)
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Generating...", new Vector2(-1, 30));
                    ImGui.EndDisabled();
                }
                else if (ImGui.Button("Generate World Content", new Vector2(-1, 30)))
                {
                    _logger?.LogInformation("Generate button clicked for: {ConfigRefName}", viewModel.SelectedConfiguration.RefName);

                    var selectedConfig = viewModel.SelectedConfiguration;
                    _isGenerating = true;
                    var gameSettings = _gameSettings;
                    var logger = _logger;
                    _generationTask = Task.Run(async () =>
                    {
                        try
                        {
                            var generatedWorldRef = selectedConfig.RefName.ToLowerInvariant() + "_generated";
                            var outputDirectory = Path.Combine(
                                gameSettings.GetAppDataContentPath(),
                                "worlds", generatedWorldRef,
                                "assets", "ambient_games", "xml");

                            // Ensure directory exists
                            Directory.CreateDirectory(outputDirectory);

                            logger?.LogInformation("Generating world content to: {OutputDirectory}", outputDirectory);
                            var generatedFiles = await _worldContentGenerator.GenerateWorldContentAsync(selectedConfig, outputDirectory);

                            logger?.LogInformation("Generated {FileCount} files", generatedFiles.Count);
                            foreach (var file in generatedFiles)
                            {
                                logger?.LogInformation("  Generated: {File}", file);
                            }

                            _lastGenerationMessage = $"Generated {generatedFiles.Count} files successfully!";
                            _showGenerationMessage = true;
                        }
                        catch (Exception ex)
                        {
                            _lastGenerationMessage = $"Error: {ex.Message}";
                            _showGenerationMessage = true;
                            logger?.LogError(ex, "Error generating world content");
                        }
                    });
                }
            }
            else
            {
                ImGui.BeginDisabled();
                ImGui.Button("Generate World Content", new Vector2(-1, 30));
                ImGui.EndDisabled();

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), _worldContentGenerator.StatusMessage);
            }

            // Show generation result message
            if (_showGenerationMessage && !string.IsNullOrEmpty(_lastGenerationMessage))
            {
                ImGui.Spacing();
                var color = _lastGenerationMessage.StartsWith("Error")
                    ? new Vector4(1, 0.3f, 0.3f, 1)
                    : new Vector4(0.3f, 1, 0.3f, 1);
                ImGui.TextColored(color, _lastGenerationMessage);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Load button
            var canLoad = viewModel.LoadSelectedConfigurationCommand?.CanExecute(null) == true;
            if (!canLoad)
            {
                ImGui.BeginDisabled();
            }

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1));
            if (ImGui.Button("Load World", new Vector2(-1, 40)))
            {
                if (viewModel.LoadSelectedConfigurationCommand.CanExecute(null))
                {
                    viewModel.LoadSelectedConfigurationCommand.Execute(null);
                }
            }
            ImGui.PopStyleColor(3);

            if (!canLoad)
            {
                ImGui.EndDisabled();
            }
            
            ImGui.Spacing();
            
            // Quit button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.2f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.25f, 0.25f, 1));
            if (ImGui.Button("Quit Game", new Vector2(-1, 30)))
            {
                // Request quit through parent's quit mechanism
                isOpen = false;
                viewModel.RaiseRequestQuit();
            }
            ImGui.PopStyleColor(3);
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Please select a world configuration to continue.");
            
            ImGui.Spacing();
            ImGui.Spacing();
            
            // Quit button when no world selected
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.2f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.25f, 0.25f, 1));
            if (ImGui.Button("Quit Game", new Vector2(-1, 40)))
            {
                isOpen = false;
                viewModel.RaiseRequestQuit();
            }
            ImGui.PopStyleColor(3);
        }

        ImGui.End();

        // Render World Creation Wizard if open
        if (_showWorldCreationWizard)
        {
            RenderWorldCreationWizard(ref _showWorldCreationWizard);
        }
    }

    private void RenderWorldCreationWizard(ref bool isOpen)
    {
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
            case 0:
                RenderWizardStep_WorldType();
                break;
            case 1:
                RenderWizardStep_Terrain();
                break;
            case 2:
                RenderWizardStep_Details();
                break;
            case 3:
                RenderWizardStep_Locations();
                break;
            case 4:
                RenderWizardStep_Create();
                break;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Navigation buttons
        RenderWizardNavigation(ref isOpen, stepNames.Length);

        ImGui.End();
    }

    private void RenderWizardStep_WorldType()
    {
        ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "What kind of world do you want to create?");
        ImGui.Spacing();
        ImGui.Spacing();

        // Real World option
        var realWorldSelected = _isRealWorld;
        ImGui.PushStyleColor(ImGuiCol.Header, realWorldSelected ? new Vector4(0.2f, 0.4f, 0.6f, 1) : new Vector4(0.2f, 0.2f, 0.2f, 1));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.3f, 0.5f, 0.7f, 1));
        if (ImGui.Selectable("## RealWorld", realWorldSelected, ImGuiSelectableFlags.None, new Vector2(0, 70)))
        {
            _isRealWorld = true;
        }
        ImGui.PopStyleColor(2);

        // Draw Real World content over the selectable
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 68);
        ImGui.Indent(10);
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1), "Real World Location");
        ImGui.TextWrapped("Create a world based on real elevation data (DEM). Perfect for recreating actual mountains, valleys, and coastlines.");
        ImGui.Unindent(10);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10);

        ImGui.Spacing();

        // Procedural option
        var proceduralSelected = !_isRealWorld;
        ImGui.PushStyleColor(ImGuiCol.Header, proceduralSelected ? new Vector4(0.2f, 0.4f, 0.6f, 1) : new Vector4(0.2f, 0.2f, 0.2f, 1));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.3f, 0.5f, 0.7f, 1));
        if (ImGui.Selectable("## Procedural", proceduralSelected, ImGuiSelectableFlags.None, new Vector2(0, 70)))
        {
            _isRealWorld = false;
        }
        ImGui.PopStyleColor(2);

        // Draw Procedural content over the selectable
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
        {
            RenderWizardStep_Terrain_RealWorld();
        }
        else
        {
            RenderWizardStep_Terrain_Procedural();
        }
    }

    private void RenderWizardStep_Terrain_RealWorld()
    {
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1), "Real World Elevation Data");
        ImGui.Spacing();

        ImGui.TextWrapped("You'll need a GeoTIFF (.tif) elevation file. OpenTopography provides free SRTM data covering most of Earth at ~30m resolution.");
        ImGui.Spacing();
        ImGui.Spacing();

        // OpenTopography button
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

        // File selection
        ImGui.TextColored(new Vector4(1, 0.8f, 0.4f, 1), "Step 2: Select your downloaded file");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 90);
        ImGui.InputText("##TerrainFile", ref _selectedTerrainFile, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse...", new Vector2(80, 0)))
        {
            BrowseForTerrainFile();
        }

        // Show file status
        if (!string.IsNullOrEmpty(_terrainFileStatus))
        {
            ImGui.Spacing();
            var statusColor = _terrainFileStatus.StartsWith("Error") || _terrainFileStatus.StartsWith("Not")
                ? new Vector4(1, 0.4f, 0.4f, 1)
                : new Vector4(0.4f, 1, 0.4f, 1);
            ImGui.TextColored(statusColor, _terrainFileStatus);
        }

        // Create texture on main thread if image data is ready
        CreateTerrainTexture();

        // Show terrain preview with spawn selection when validated
        if (_terrainValidated && _terrainTexturePtr != IntPtr.Zero && _terrainPreviewImage != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("Click to set spawn location:");

            // Calculate display size (fill available width, maintain aspect ratio)
            var availWidth = ImGui.GetContentRegionAvail().X;
            var aspectRatio = (float)_terrainPreviewImage.Width / _terrainPreviewImage.Height;
            var displayWidth = availWidth;
            var displayHeight = displayWidth / aspectRatio;
            var imageSize = new Vector2(displayWidth, displayHeight);

            // Get cursor position before image
            var imagePos = ImGui.GetCursorScreenPos();

            // Draw the map
            ImGui.Image(_terrainTexturePtr, imageSize);

            // Handle click to select spawn
            if (ImGui.IsItemClicked())
            {
                var mousePos = ImGui.GetMousePos();
                var relativePos = mousePos - imagePos;
                // Convert to pixel coordinates
                _selectedSpawnPixel = new Vector2(
                    relativePos.X / displayWidth * _terrainPreviewImage.Width,
                    relativePos.Y / displayHeight * _terrainPreviewImage.Height);
                _spawnSelected = true;
            }

            // Show height and GPS coordinates on hover
            if (ImGui.IsItemHovered() && _heightData != null)
            {
                var mousePos = ImGui.GetMousePos();
                var relativePos = mousePos - imagePos;

                // Convert to pixel coordinates
                var pixelX = (int)(relativePos.X / displayWidth * _terrainPreviewImage.Width);
                var pixelY = (int)(relativePos.Y / displayHeight * _terrainPreviewImage.Height);

                // Clamp to valid range
                pixelX = Math.Clamp(pixelX, 0, _terrainPreviewImage.Width - 1);
                pixelY = Math.Clamp(pixelY, 0, _terrainPreviewImage.Height - 1);

                // Get height value
                var heightIndex = pixelY * _heightDataWidth + pixelX;
                var heightValue = heightIndex < _heightData.Length ? _heightData[heightIndex] : 0;

                // Build tooltip text
                var tooltipText = $"Height: {heightValue}m";

                // Add GPS coordinates if geotransform is available
                if (_geoTransform != null && _geoTransform.Length >= 6)
                {
                    // GeoTransform: [originX, pixelWidth, rotX, originY, rotY, pixelHeight]
                    var lon = _geoTransform[0] + pixelX * _geoTransform[1] + pixelY * _geoTransform[2];
                    var lat = _geoTransform[3] + pixelX * _geoTransform[4] + pixelY * _geoTransform[5];
                    tooltipText += $"\nLat: {lat:F5}, Lon: {lon:F5}";
                }

                ImGui.SetTooltip(tooltipText);
            }

            // Draw spawn marker
            if (_spawnSelected)
            {
                var markerScreenPos = imagePos + new Vector2(
                    _selectedSpawnPixel.X / _terrainPreviewImage.Width * displayWidth,
                    _selectedSpawnPixel.Y / _terrainPreviewImage.Height * displayHeight);
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddCircleFilled(markerScreenPos, 6, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1)));
                drawList.AddCircle(markerScreenPos, 6, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), 12, 2);
            }

            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1),
                $"Spawn: ({(int)_selectedSpawnPixel.X}, {(int)_selectedSpawnPixel.Y})");
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

        // Terrain style selection as selectable list
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

            // Draw content over selectable
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

        // World name input
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

        // Theme selection (applies to all worlds)
        ImGui.Text("Theme:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##Theme", GetThemeDisplayName(AvailableThemes[_selectedTheme])))
        {
            for (int i = 0; i < AvailableThemes.Length; i++)
            {
                var isSelected = _selectedTheme == i;
                if (ImGui.Selectable(GetThemeDisplayName(AvailableThemes[i]), isSelected))
                {
                    _selectedTheme = i;
                }
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Content theme for characters, items, and stories");

        // Latitude and Height only for procedural worlds
        if (!_isRealWorld)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Latitude selection
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

            // World height selection
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

    // Available themes (folder names from Content/packs/themes/)
    private static readonly string[] AvailableThemes = new[] { "feudal_japan" };
    private int _selectedTheme = 0;

    private void RenderWizardStep_Locations()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.9f, 1f, 1), "Source Locations (Optional)");
        ImGui.Spacing();

        ImGui.TextWrapped("Import a CSV file with location data to place points of interest in your world. This step is optional - you can add locations later.");
        ImGui.Spacing();

        // Tabs for Template vs AI Prompt
        if (ImGui.BeginTabBar("LocationsTabs"))
        {
            if (ImGui.BeginTabItem("CSV Template"))
            {
                RenderLocationsTemplateTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("AI Prompt"))
            {
                RenderLocationsAIPromptTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // File selection
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 90);
        ImGui.InputText("##LocationsFile", ref _selectedLocationsFile, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse...", new Vector2(80, 0)))
        {
            BrowseForLocationsFile();
        }

        // Show file status
        if (!string.IsNullOrEmpty(_locationsFileStatus))
        {
            ImGui.Spacing();
            var statusColor = _locationsFileStatus.StartsWith("Error")
                ? new Vector4(1, 0.4f, 0.4f, 1)
                : new Vector4(0.4f, 1, 0.4f, 1);
            ImGui.TextColored(statusColor, _locationsFileStatus);
        }

        // Show imported locations preview
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

        // Clear button
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

        // Category reference
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

    private string _aiPromptText = "";
    private bool _aiPromptCopied = false;

    private void RenderLocationsAIPromptTab()
    {
        ImGui.Spacing();
        ImGui.TextWrapped("Copy this prompt to ChatGPT, Claude, or another AI to generate location data for your world:");
        ImGui.Spacing();

        string prompt;

        if (_isRealWorld && _geoTransform != null && _geoTransform.Length >= 6 && _terrainWidth > 0 && _terrainHeight > 0)
        {
            // Real World - use actual GPS bounds and spawn location
            var minLon = _geoTransform[0];
            var maxLon = _geoTransform[0] + _terrainWidth * _geoTransform[1];
            var maxLat = _geoTransform[3];
            var minLat = _geoTransform[3] + _terrainHeight * _geoTransform[5];

            // Calculate spawn GPS from pixel coordinates
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
            // Procedural World - fictional theme-based locations within 1 degree of spawn
            var themeName = GetThemeDisplayName(AvailableThemes[_selectedTheme]);

            // For procedural, use a default spawn area (can be adjusted)
            // Center around a thematic location (e.g., 35, 135 for Japan theme)
            _spawnLatitude = 35.0;
            _spawnLongitude = 135.0;
            var latRange = 1.0; // 1 degree in each direction

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

        // Store for clipboard
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

        // Summary
        ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), "Summary:");
        ImGui.Spacing();

        ImGui.Indent(10);

        ImGui.Text($"World Name: {_worldName}");
        ImGui.Text($"World Type: {(_isRealWorld ? "Real World (DEM)" : "Procedural")}");
        ImGui.Text($"Theme: {GetThemeDisplayName(AvailableThemes[_selectedTheme])}");

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

        // Output location info
        var worldRef = SanitizeWorldName(_worldName);
        var outputPath = GetWorldOutputPath(worldRef);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Files will be created at:");
        ImGui.TextWrapped(outputPath);

        // Show creation status if in progress
        if (_isCreatingWorld && !string.IsNullOrEmpty(_creationStatus))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1), _creationStatus);
        }
    }

    // Step 4: Locations state
    private string _selectedLocationsFile = "";
    private string _locationsFileStatus = "";
    private bool _locationsValidated = false;
    private int _locationsCount = 0;
    private List<LocationEntry> _importedLocations = new();

    // Location entry from CSV (maps to SagaArc)
    private class LocationEntry
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Category { get; set; } = "Default";
        public string Kind { get; set; } = "Default";
    }

    private static string GetThemeDisplayName(string folderName)
    {
        // Convert "feudal_japan" to "Feudal Japan"
        return string.Join(" ", folderName.Split('_')
            .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
    }

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

    private string _convertedTifPath = ""; // Path to GDAL-converted file (what we actually use)

    private void ValidateTerrainFile()
    {
        _terrainValidated = false;
        _validatedTifPath = "";
        _convertedTifPath = "";
        _terrainWidth = 0;
        _terrainHeight = 0;

        // Reset preview state
        _terrainPreviewImage = null;
        _isLoadingPreview = false;
        _geoTransform = null;
        _heightData = null;
        _heightDataWidth = 0;
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

        // Run validation and conversion asynchronously
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

            // Validate the source GeoTIFF dimensions first
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

            // Convert with GDAL to standardized format
            if (_geoTiffConverter != null && _geoTiffConverter.IsAvailable)
            {
                // Debug breakpoint for terrain conversion
                System.Diagnostics.Debugger.Break();

                _terrainFileStatus = "Converting with GDAL...";

                // Create temp path for converted file
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
                    _validatedTifPath = convertedPath; // Use converted file for everything
                    _terrainFileStatus = $"Valid: {width}x{height} ({totalPixels:N0} pixels)";
                    System.Diagnostics.Debug.WriteLine($"[Terrain] GDAL conversion succeeded: {convertedPath}");

                    // Capture geotransform for GPS coordinate display
                    var info = _geoTiffConverter.GetInfo(convertedPath);
                    if (info != null)
                    {
                        _geoTransform = info.GeoTransform;
                        System.Diagnostics.Debug.WriteLine($"[Terrain] GeoTransform: [{string.Join(", ", _geoTransform)}]");
                    }
                }
                else
                {
                    _terrainFileStatus = "Error: GDAL conversion failed";
                    System.Diagnostics.Debug.WriteLine("[Terrain] GDAL conversion FAILED");
                    return;
                }
            }
            else
            {
                // No GDAL - use source file directly (may not work correctly)
                _validatedTifPath = sourceTifPath;
                _terrainFileStatus = $"Valid: {width}x{height} (no GDAL - using raw file)";
                System.Diagnostics.Debug.WriteLine($"[Terrain] No GDAL available, using raw file: {sourceTifPath}");
            }

            _terrainValidated = true;
            System.Diagnostics.Debug.WriteLine($"[Terrain] Validated, calling GenerateTerrainPreview with: {_validatedTifPath}");

            // Generate terrain preview for spawn selection
            GenerateTerrainPreview(_validatedTifPath);
        }
        catch (Exception ex)
        {
            _terrainFileStatus = $"Error: {ex.Message}";
        }
    }

    private bool _isLoadingPreview;

    private void GenerateTerrainPreview(string tifPath)
    {
        System.Diagnostics.Debug.WriteLine($"[Terrain] GenerateTerrainPreview called with: {tifPath}");
        if (_isLoadingPreview)
        {
            System.Diagnostics.Debug.WriteLine("[Terrain] Already loading preview, skipping");
            return;
        }
        _isLoadingPreview = true;
        _terrainFileStatus = "Loading terrain preview...";

        // Run on background thread to avoid blocking UI
        Task.Run(() =>
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[Terrain] Loading image from: {tifPath}");
                // Load and process the height map
                using var image = Image.Load<L16>(tifPath);
                System.Diagnostics.Debug.WriteLine($"[Terrain] Image loaded: {image.Width}x{image.Height}");

                // Extract raw height data for hover display
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
                System.Diagnostics.Debug.WriteLine($"[Terrain] Height data extracted: {_heightData.Length} values");

                var processedMap = HeightMapProcessor.ProcessHeightMap(image, minWaterAreaSize: 50, adjustMinWaterAreaSizeByElevation: true, verticalShift: 0);
                System.Diagnostics.Debug.WriteLine($"[Terrain] HeightMap processed: {processedMap.Width}x{processedMap.Height}");

                // Convert to BGRA image data
                var imageData = ConvertProcessedMapToImageData(processedMap);
                System.Diagnostics.Debug.WriteLine($"[Terrain] Image data created: {imageData.Width}x{imageData.Height}, {imageData.PixelData.Length} bytes");

                // Update UI state (will be picked up on next render)
                _terrainPreviewImage = imageData;
                _selectedSpawnPixel = new Vector2(processedMap.Width / 2f, processedMap.Height / 2f);
                _spawnSelected = true;
                _terrainFileStatus = $"Valid: {_terrainWidth}x{_terrainHeight} - Click map to set spawn";
                _isLoadingPreview = false;
                System.Diagnostics.Debug.WriteLine("[Terrain] Preview generation complete, _terrainPreviewImage set");
            }
            catch (Exception ex)
            {
                _terrainFileStatus = $"Preview error: {ex.Message}";
                _isLoadingPreview = false;
                System.Diagnostics.Debug.WriteLine($"[Terrain] Preview error: {ex.Message}\n{ex.StackTrace}");
            }
        });
    }

    private void CreateTerrainTexture()
    {
        // Create texture on main thread when image data is ready
        if (_terrainPreviewImage != null && _terrainTexturePtr == IntPtr.Zero && _textureProvider != null)
        {
            System.Diagnostics.Debug.WriteLine($"[Terrain] CreateTerrainTexture: Creating texture from image {_terrainPreviewImage.Width}x{_terrainPreviewImage.Height}");

            // Dispose previous texture if any
            if (_terrainTextureResources != null)
            {
                _textureProvider.DisposeTexture(_terrainTextureResources);
                _terrainTextureResources = null;
            }

            var (texturePtr, _, _, resources) = _textureProvider.CreateTextureFromImageData(_terrainPreviewImage);
            _terrainTexturePtr = texturePtr;
            _terrainTextureResources = resources;
            System.Diagnostics.Debug.WriteLine($"[Terrain] Texture created, ptr: {texturePtr}");
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

    private void RenderWizardNavigation(ref bool isOpen, int totalSteps)
    {
        // Cancel button (left side)
        if (ImGui.Button("Cancel", new Vector2(80, 30)))
        {
            ResetWizardState();
            isOpen = false;
        }

        ImGui.SameLine();

        // Spacer to push nav buttons to the right
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X - 180, 0));
        ImGui.SameLine();

        // Back button
        if (_wizardStep > 0)
        {
            if (ImGui.Button("Back", new Vector2(80, 30)))
            {
                _wizardStep--;
            }
            ImGui.SameLine();
        }

        // Next/Create button
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
                // Create the world files
                CreateWorld();
                ResetWizardState();
                isOpen = false;
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
        // Block all navigation while creating world
        if (_isCreatingWorld) return false;

        return _wizardStep switch
        {
            0 => true, // World Type - always can proceed
            1 => !_isRealWorld || _terrainValidated, // Terrain - procedural always ok, real world needs validation
            2 => !string.IsNullOrWhiteSpace(_worldName), // Details - need a world name
            _ => true
        };
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

    private void ResetWizardState()
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
        _isCreatingWorld = false;
        _creationStatus = "";

        // Dispose terrain texture if any
        if (_terrainTextureResources != null && _textureProvider != null)
        {
            _textureProvider.DisposeTexture(_terrainTextureResources);
            _terrainTextureResources = null;
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

            // Check if first line is header
            if (lines.Length > 0)
            {
                var firstLine = lines[0].ToLowerInvariant();
                if (firstLine.Contains("name") || firstLine.Contains("latitude") || firstLine.Contains("longitude"))
                {
                    startLine = 1; // Skip header
                }
            }

            // Detect format from header
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
                    // New format: name,description,latitude,longitude,category,kind
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
                    // Legacy format: name,latitude,longitude[,type]
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

    private static string SanitizeWorldName(string name)
    {
        // Convert to lowercase, replace spaces and invalid chars with underscores
        var sanitized = name.ToLowerInvariant()
            .Replace(" ", "_")
            .Replace("-", "_");

        // Remove any characters that aren't alphanumeric or underscore
        var chars = sanitized.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray();
        return new string(chars);
    }

    private string GetWorldOutputPath(string worldRef)
    {
        return Path.Combine(
            _gameSettings.GetAppDataContentPath(),
            "worlds",
            worldRef);
    }

    private bool _isCreatingWorld;
    private string _creationStatus = "";

    private void CreateWorld()
    {
        if (_isCreatingWorld) return;

        _isCreatingWorld = true;
        _creationStatus = "Creating world...";

        // Run world creation asynchronously
        Task.Run(async () =>
        {
            try
            {
                var worldRef = SanitizeWorldName(_worldName);
                var outputPath = GetWorldOutputPath(worldRef);

                // Create directory structure
                Directory.CreateDirectory(outputPath);

                // Create generation.xml
                _creationStatus = "Writing generation.xml...";
                var generationXml = GenerateGenerationXml(worldRef);
                File.WriteAllText(Path.Combine(outputPath, "generation.xml"), generationXml);

                // Create worldconfiguration.xml
                _creationStatus = "Writing worldconfiguration.xml...";
                var configXml = GenerateWorldConfigurationXml(worldRef);
                File.WriteAllText(Path.Combine(outputPath, "worldconfiguration.xml"), configXml);

                // Copy terrain file if real world (already converted during validation)
                if (_isRealWorld && !string.IsNullOrEmpty(_validatedTifPath))
                {
                    _creationStatus = "Copying terrain file...";
                    var terrainDest = Path.Combine(outputPath, "terrain.tif");
                    File.Copy(_validatedTifPath, terrainDest, overwrite: true);
                }

                // Create locations.csv - either imported or default radial locations
                _creationStatus = "Writing locations.csv...";
                var locationsPath = Path.Combine(outputPath, "locations.csv");
                var csvLines = new List<string> { "name,description,latitude,longitude,category,kind" };

                if (_importedLocations.Count > 0)
                {
                    // User provided locations - use trail generation
                    _locationGenerationType = "trail";
                    csvLines.AddRange(_importedLocations.Select(l =>
                        $"\"{l.Name}\",\"{l.Description}\",{l.Latitude},{l.Longitude},{l.Category},{l.Kind}"));
                }
                else
                {
                    // No locations provided - generate 3 default radial locations
                    _locationGenerationType = "radial";
                    var defaultLocations = GenerateDefaultRadialLocations();
                    csvLines.AddRange(defaultLocations.Select(l =>
                        $"\"{l.Name}\",\"{l.Description}\",{l.Latitude},{l.Longitude},{l.Category},{l.Kind}"));
                }

                File.WriteAllLines(locationsPath, csvLines);

                _lastGenerationMessage = $"World '{_worldName}' created successfully at:\n{outputPath}";
                _showGenerationMessage = true;
                _isCreatingWorld = false;
                _creationStatus = "";
            }
            catch (Exception ex)
            {
                _lastGenerationMessage = $"Error creating world: {ex.Message}";
                _showGenerationMessage = true;
                _isCreatingWorld = false;
                _creationStatus = "";
            }
        });
    }

    /// <summary>
    /// Generates 3 default radial seed locations around the spawn point.
    /// These serve as starting points for the radial generation algorithm
    /// which will add intermediate waypoints connecting them.
    /// </summary>
    private List<LocationEntry> GenerateDefaultRadialLocations()
    {
        var locations = new List<LocationEntry>();
        var random = new Random();

        // Calculate spawn GPS if we have geotransform
        if (_geoTransform != null && _geoTransform.Length >= 6)
        {
            _spawnLongitude = _geoTransform[0] + _selectedSpawnPixel.X * _geoTransform[1] + _selectedSpawnPixel.Y * _geoTransform[2];
            _spawnLatitude = _geoTransform[3] + _selectedSpawnPixel.X * _geoTransform[4] + _selectedSpawnPixel.Y * _geoTransform[5];
        }
        else
        {
            // Default for procedural worlds
            _spawnLatitude = 35.0;
            _spawnLongitude = 135.0;
        }

        // Generate 3 seed locations at roughly 120-degree angles from spawn
        // Distance varies between 0.1 and 0.3 degrees (~10-30km)
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
            var distance = 0.15 + random.NextDouble() * 0.15; // 0.15 to 0.3 degrees

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

    private string GenerateGenerationXml(string worldRef)
    {
        var mode = _isRealWorld ? "RealWorld" : ProceduralModes[_selectedProceduralMode];
        var latitude = LatitudeValues[_selectedLatitude];
        var height = _isRealWorld ? 512 : WorldHeightValues[_selectedWorldHeight];

        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Generation xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <WorldRef>{worldRef}</WorldRef>
  <DisplayName>{_worldName}</DisplayName>
  <Mode>{mode}</Mode>
  <Latitude>{latitude}</Latitude>
  <WorldHeight>{height}</WorldHeight>
  <Theme>{AvailableThemes[_selectedTheme]}</Theme>
  <SpawnX>{(int)_selectedSpawnPixel.X}</SpawnX>
  <SpawnY>{(int)_selectedSpawnPixel.Y}</SpawnY>
  <LocationGenerationType>{_locationGenerationType}</LocationGenerationType>
  {(_isRealWorld ? $"<TerrainFile>terrain.tif</TerrainFile>" : "")}
</Generation>";
    }

    private string GenerateWorldConfigurationXml(string worldRef)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<WorldConfiguration xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <RefName>{worldRef}</RefName>
  <DisplayName>{_worldName}</DisplayName>
  <Description>Created with World Creation Wizard</Description>
  <Theme>{AvailableThemes[_selectedTheme]}</Theme>
</WorldConfiguration>";
    }
}
