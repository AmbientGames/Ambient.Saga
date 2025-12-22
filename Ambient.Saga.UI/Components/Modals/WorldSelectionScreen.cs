using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
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
    private string? _lastGenerationMessage;
    private bool _showGenerationMessage;
    private Task? _generationTask;
    private bool _isGenerating;

    public WorldSelectionScreen(IWorldContentGenerator worldContentGenerator)
    {
        _worldContentGenerator = worldContentGenerator ?? throw new ArgumentNullException(nameof(worldContentGenerator));
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
                    Debug.WriteLine($"Generate button clicked for: {viewModel.SelectedConfiguration.RefName}");

                    var selectedConfig = viewModel.SelectedConfiguration;
                    _isGenerating = true;
                    _generationTask = Task.Run(async () =>
                    {
                        try
                        {
                            var solutionDir = FindSolutionRootFrom(AppContext.BaseDirectory);
                            if (solutionDir == null)
                                throw new DirectoryNotFoundException($"Could not locate solution root from: {AppContext.BaseDirectory}");

                            var outputDirectory = Path.Combine(solutionDir, "Content", "Worlds");

                            Debug.WriteLine($"Generating world content to: {outputDirectory}");
                            var generatedFiles = await _worldContentGenerator.GenerateWorldContentAsync(selectedConfig, outputDirectory);

                            Debug.WriteLine($"Generated {generatedFiles.Count} files:");
                            foreach (var file in generatedFiles)
                            {
                                Debug.WriteLine($"  - {file}");
                            }

                            // Copy generated files to exe directory for game loading
                            var exeDirectory = AppContext.BaseDirectory;
                            var exeWorldsDirectory = Path.Combine(exeDirectory, "Content", "Worlds");

                            Debug.WriteLine($"Copying generated files to exe directory: {exeWorldsDirectory}");
                            CopyGeneratedFilesToExeDirectory(outputDirectory, exeWorldsDirectory, generatedFiles);

                            _lastGenerationMessage = $"Generated {generatedFiles.Count} files successfully!";
                            _showGenerationMessage = true;
                            Debug.WriteLine("World content generation and deployment complete!");
                        }
                        catch (Exception ex)
                        {
                            _lastGenerationMessage = $"Error: {ex.Message}";
                            _showGenerationMessage = true;
                            Debug.WriteLine($"Error generating world content: {ex.Message}");
                            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
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
    }

    static string? FindSolutionRootFrom(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);

        while (dir != null)
        {
            // Any .sln in this directory?
            if (dir.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any())
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Copies generated story files from source directory to exe directory so the game can load them
    /// </summary>
    private void CopyGeneratedFilesToExeDirectory(string sourceBaseDir, string targetBaseDir, List<string> generatedFiles)
    {
        // Ensure target directory exists
        Directory.CreateDirectory(targetBaseDir);

        // Copy all generated files
        foreach (var sourceFile in generatedFiles)
        {
            // Calculate relative path from source base directory
            var relativePath = Path.GetRelativePath(sourceBaseDir, sourceFile);
            var targetFile = Path.Combine(targetBaseDir, relativePath);

            // Create target subdirectories if needed
            var targetDir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Copy file
            File.Copy(sourceFile, targetFile, overwrite: true);
            Debug.WriteLine($"  Copied: {relativePath}");
        }

        // Also copy WorldConfigurations.xml (critical for refs)
        var sourceConfigFile = Path.Combine(sourceBaseDir, "WorldConfigurations.xml");
        var targetConfigFile = Path.Combine(targetBaseDir, "WorldConfigurations.xml");

        if (File.Exists(sourceConfigFile))
        {
            File.Copy(sourceConfigFile, targetConfigFile, overwrite: true);
            Debug.WriteLine($"  Copied: WorldConfigurations.xml");
        }
    }
}
