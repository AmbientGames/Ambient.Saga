using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.StoryGenerator;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

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
    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        // Center the selection window
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(viewport.Size.X * 0.5f, viewport.Size.Y * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("World Selection", ref isOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Select a World to Load");
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

            // Generate button (appears when world has a generation config)
            var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var configsDir = Path.Combine(solutionDir, "Ambient.Saga.StoryGenerator", "GenerationConfigs");
            var generationConfigLoader = new GenerationConfigurationLoader(configsDir);
            if (generationConfigLoader.HasGenerationConfig(viewModel.SelectedConfiguration.RefName))
            {
                ImGui.TextColored(new Vector4(1, 0.647f, 0, 1), "Story Generation:");
                ImGui.Spacing();

                if (ImGui.Button("Generate Story Content", new Vector2(-1, 30)))
                {
                    Debug.WriteLine($"Generate button clicked for: {viewModel.SelectedConfiguration.RefName}");

                    try
                    {
                        var generator = new StoryGenerator.StoryGenerator();
                        var generationConfig = generationConfigLoader.LoadConfig(viewModel.SelectedConfiguration.RefName);

                        // Get the source WorldDefinitions directory (not bin)
                        var outputDirectory = Path.Combine(solutionDir, "Ambient.Saga.Sandbox.WindowsUI", "WorldDefinitions");

                        Debug.WriteLine($"Generating story content to: {outputDirectory}");
                        var generatedFiles = generator.GenerateStoryContent(viewModel.SelectedConfiguration, generationConfig, outputDirectory);

                        Debug.WriteLine($"Generated {generatedFiles.Count} files:");
                        foreach (var file in generatedFiles)
                        {
                            Debug.WriteLine($"  - {file}");
                        }

                        Debug.WriteLine($"Updated WorldConfigurations.xml refs for {viewModel.SelectedConfiguration.RefName}");

                        // Copy generated files to exe directory for game loading
                        var exeDirectory = AppContext.BaseDirectory;
                        var exeWorldDefinitions = Path.Combine(exeDirectory, "WorldDefinitions");

                        Debug.WriteLine($"Copying generated files to exe directory: {exeWorldDefinitions}");
                        CopyGeneratedFilesToExeDirectory(outputDirectory, exeWorldDefinitions, generatedFiles);

                        Debug.WriteLine("Story generation and deployment complete!");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error generating story content: {ex.Message}");
                        Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // Load button
            var canLoad = viewModel.LoadSelectedConfigurationCommand?.CanExecute(null) == true;
            if (!canLoad)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Load World", new Vector2(-1, 40)))
            {
                if (viewModel.LoadSelectedConfigurationCommand.CanExecute(null))
                {
                    viewModel.LoadSelectedConfigurationCommand.Execute(null);
                }
            }

            if (!canLoad)
            {
                ImGui.EndDisabled();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Please select a world configuration to continue.");
        }

        ImGui.End();
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
