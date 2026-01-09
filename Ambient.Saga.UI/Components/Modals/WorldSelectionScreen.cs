using Ambient.Application.Contracts;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
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
    private readonly ILogger<WorldSelectionScreen>? _logger;
    private string? _lastGenerationMessage;
    private bool _showGenerationMessage;
    private Task? _generationTask;
    private bool _isGenerating;

    public WorldSelectionScreen(
        IWorldContentGenerator worldContentGenerator,
        IGameSettings gameSettings,
        ILogger<WorldSelectionScreen>? logger = null)
    {
        _worldContentGenerator = worldContentGenerator ?? throw new ArgumentNullException(nameof(worldContentGenerator));
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _logger = logger;
    }

    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        // Center the selection window (auto-resize with content, capped to screen)
        var viewport = ImGui.GetMainViewport();
        var center = viewport.GetCenter();
        var scale = UIConstants.DpiScale;

        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(600 * scale, 461 * scale),
            new Vector2(600 * scale, viewport.Size.Y * 0.9f));

        var windowFlags =
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.AlwaysAutoResize;

        if (!ImGui.Begin("World Selection", windowFlags))
        {
            ImGui.End();
            return;
        }

        ImGui.PushFont(UIConstants.FontTitle);
        ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Select a World to Load");
        ImGui.PopFont();
        ImGui.Separator();
        ImGui.Spacing();

        // World selection
        ImGui.Text("World:");
        ImGui.SetNextItemWidth(ImGuiSizes.Fill);
        if (ImGui.BeginCombo("##WorldConfig", viewModel.SelectedConfiguration?.DisplayName ?? viewModel.SelectedConfiguration?.RefName ?? "Select a world..."))
        {
            foreach (var config in viewModel.AvailableConfigurations)
            {
                var isSelected = viewModel.SelectedConfiguration?.RefName == config.RefName;
                if (ImGui.Selectable(config.DisplayName ?? config.RefName, isSelected))
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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Display selected world info
        if (viewModel.SelectedConfiguration != null)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), viewModel.SelectedConfiguration.DisplayName ?? viewModel.SelectedConfiguration.RefName);
            ImGui.Indent(10 * UIConstants.DpiScale);

            if (!string.IsNullOrEmpty(viewModel.SelectedConfiguration.Description))
            {
                ImGui.Spacing();
                ImGui.TextWrapped(viewModel.SelectedConfiguration.Description);
            }
            ImGui.Unindent(10 * UIConstants.DpiScale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // World Content Generation section
            ImGui.TextColored(new Vector4(1, 0.647f, 0, 1), "World Content Generation:");
            ImGui.Spacing();

            var generateButtonHeight = ImGui.GetFrameHeight() * 1.2f;

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
                    ImGui.Button("Generating...", new Vector2(ImGuiSizes.Fill, generateButtonHeight));
                    ImGui.EndDisabled();
                }
                else if (ImGui.Button("Generate World Content", new Vector2(ImGuiSizes.Fill, generateButtonHeight)))
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
                ImGui.Button("Generate World Content", new Vector2(ImGuiSizes.Fill, generateButtonHeight));
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

            var loadButtonHeight = ImGui.GetFrameHeight() * 1.4f;
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1));
            if (ImGui.Button("Load World", new Vector2(ImGuiSizes.Fill, loadButtonHeight)))
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
            var quitButtonHeight = ImGui.GetFrameHeight() * 1.2f;
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.2f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.25f, 0.25f, 1));
            if (ImGui.Button("Quit Game", new Vector2(ImGuiSizes.Fill, quitButtonHeight)))
            {
                // Request quit through parent's quit mechanism
                isOpen = false;
                viewModel.RaiseRequestQuit();
            }
            ImGui.PopStyleColor(3);
        }
        else
        {
            // Quit button when no world selected
            var quitButtonHeight = ImGui.GetFrameHeight() * 1.4f;
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.2f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.25f, 0.25f, 1));
            if (ImGui.Button("Quit Game", new Vector2(ImGuiSizes.Fill, quitButtonHeight)))
            {
                isOpen = false;
                viewModel.RaiseRequestQuit();
            }
            ImGui.PopStyleColor(3);
        }

        ImGui.End();
    }
}
