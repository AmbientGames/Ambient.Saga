using Ambient.Domain;
using Ambient.Infrastructure.GameLogic.Services;
using Ambient.Infrastructure.GameLogic.Loading;
using Ambient.Domain.Contracts;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Integration tests for WorldValidationService that load real world XML data from Ambient.Domain.
/// Validates that the actual world configurations pass all validation rules.
/// NOTE: "Ise" is the target configuration for RPG content and is now the only available configuration.
/// </summary>
public class WorldValidationServiceIntegrationTests
{
    private readonly IWorldFactory _worldFactory = new TestWorldFactory();
    private readonly IWorldConfigurationLoader _configurationLoader = new WorldConfigurationLoader();
    private readonly IWorldLoader _worldLoader;
    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;
    private const string TestWorldConfiguration = "Ise";

    public WorldValidationServiceIntegrationTests()
    {
        // Content/Schemas is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Schemas");

        // Content/Worlds is at solution root (shared by all Sandboxes)
        _dataDirectory = FindWorldsDirectory();

        _worldLoader = new WorldAssetLoader(_worldFactory, _configurationLoader);
    }

    private static string FindWorldsDirectory()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory != null)
        {
            var worldDefPath = Path.Combine(directory, "Content", "Worlds");
            if (Directory.Exists(worldDefPath))
                return worldDefPath;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find Content/Worlds directory");
    }

    [Fact]
    public async Task ValidateReferentialIntegrity_DefaultWorld_PassesAllValidation()
    {
        // Arrange - Load the actual world configuration
        var world = await _worldLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, TestWorldConfiguration);

        // Act & Assert - Should not throw any validation errors
        // This ensures the world configuration has:
        // - All dialogue item rewards exist in character loot pools
        // - All block/material/equipment rewards have sufficient quantities
        // - All currency transfers are backed by character credits
        // - All quest tokens promised in dialogue exist in character loot pools
        WorldValidationService.ValidateReferentialIntegrity(world);
    }

    [Fact]
    public async Task ValidateReferentialIntegrity_DefaultWorld_CharactersWithDialogueHavePromisedItems()
    {
        // Arrange - Load the actual world configuration
        var world = await _worldLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, TestWorldConfiguration);

        // Act - Validate and capture any errors
        var validationPassed = true;
        string? errorMessage = null;
        try
        {
            WorldValidationService.ValidateReferentialIntegrity(world);
        }
        catch (InvalidOperationException ex)
        {
            validationPassed = false;
            errorMessage = ex.Message;
        }

        // Assert - If validation fails, provide helpful debug info
        if (!validationPassed)
        {
            Assert.Fail($"Default world dialogue inventory validation failed:\n{errorMessage}");
        }

        // If we get here, validation passed
        Assert.True(validationPassed);
    }

    [Fact]
    public async Task ValidateReferentialIntegrity_DefaultWorld_BlockRewardsInDialogueAreValid()
    {
        // Arrange - Load the actual world configuration
        var world = await _worldLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, TestWorldConfiguration);

        // Act - Run validation
        WorldValidationService.ValidateReferentialIntegrity(world);

        // Assert - Check that if any dialogue gives blocks, those characters have blocks
        if (world.Gameplay.Characters != null && world.Gameplay.DialogueTrees != null)
        {
            foreach (var character in world.Gameplay.Characters)
            {
                var dialogueTreeRef = character.Interactable?.DialogueTreeRef;
                if (string.IsNullOrEmpty(dialogueTreeRef)) continue;

                if (world.DialogueTreesLookup.TryGetValue(dialogueTreeRef, out var dialogueTree))
                {
                    // Check if any nodes give blocks
                    var givesBlocks = dialogueTree.Node?.Any(n =>
                        n.Action?.Any(a => a.Type == DialogueActionType.GiveBlock) == true) == true;

                    if (givesBlocks)
                    {
                        // Character has dialogue that gives blocks - validation should have already checked loot pool
                        // This assertion just documents the expectation
                        Assert.NotNull(character.Interactable?.Loot?.Blocks);
                    }
                }
            }
        }

        // If we get here, all block rewards are valid
        Assert.True(true);
    }

    /// <summary>
    /// Loads and validates ALL world configurations defined in WorldConfigurations.xml.
    /// Each configuration is tested separately - if one fails, you'll see exactly which one.
    /// Validation happens automatically during load (WorldAssetLoader calls ValidateReferentialIntegrity).
    /// </summary>
    [Theory]
    [MemberData(nameof(GetAllWorldConfigurations))]
    public async Task ValidateReferentialIntegrity_AllWorldConfigurations_PassValidation(string configurationRefName)
    {
        // Arrange & Act - Load world configuration (validation happens automatically during load)
        var world = await _worldLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, configurationRefName);

        // Assert - If we got here without exception, validation passed
        Assert.NotNull(world);
        Assert.Equal(configurationRefName, world.WorldConfiguration?.RefName);
    }

    /// <summary>
    /// Provides all world configuration RefNames as test data.
    /// This method runs during test discovery to generate individual test cases for each configuration.
    /// </summary>
    public static IEnumerable<object[]> GetAllWorldConfigurations()
    {
        // Note: This runs before test execution to discover test cases
        // Content/Schemas is copied to output directory by Ambient.Domain
        var definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Schemas");

        // Content/Worlds is at solution root (shared by all Sandboxes)
        var dataDirectory = FindWorldsDirectory();

        var configurationLoader = new WorldConfigurationLoader();
        var configurations = configurationLoader.LoadAvailableWorldConfigurationsAsync(dataDirectory, definitionDirectory).GetAwaiter().GetResult();

        foreach (var config in configurations)
        {
            yield return new object[] { config.RefName };
        }
    }
}
