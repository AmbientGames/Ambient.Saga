using Ambient.Application.Contracts;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI.Services;
using BitMiracle.LibTiff.Classic;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using SharpCompress.Readers;
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
        ILogger<WorldSelectionScreen>? logger = null)
    {
        _worldContentGenerator = worldContentGenerator ?? throw new ArgumentNullException(nameof(worldContentGenerator));
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _fileDialogService = fileDialogService;
        _logger = logger;
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
            default:
                ImGui.Text("(Coming soon...)");
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

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Supported: .tif, .tiff, .tar.gz (containing GeoTIFF)");
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

    private void ValidateTerrainFile()
    {
        _terrainValidated = false;
        _validatedTifPath = "";
        _terrainWidth = 0;
        _terrainHeight = 0;

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

        try
        {
            string tifPath;
            var ext = Path.GetExtension(_selectedTerrainFile).ToLowerInvariant();

            if (ext == ".tif" || ext == ".tiff")
            {
                tifPath = _selectedTerrainFile;
            }
            else if (_selectedTerrainFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                _terrainFileStatus = "Extracting archive...";
                tifPath = ExtractTifFromTarGz(_selectedTerrainFile);
                if (string.IsNullOrEmpty(tifPath))
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

            // Validate the GeoTIFF dimensions
            _terrainFileStatus = "Validating GeoTIFF...";
            var (width, height, error) = ValidateGeoTiffDimensions(tifPath);

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
            _validatedTifPath = tifPath;
            _terrainValidated = true;
            _terrainFileStatus = $"Valid: {width}x{height} ({totalPixels:N0} pixels)";
        }
        catch (Exception ex)
        {
            _terrainFileStatus = $"Error: {ex.Message}";
        }
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
            _wizardStep = 0;
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
                // TODO: Create the world
                _wizardStep = 0;
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
}
