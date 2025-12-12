using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Infrastructure.Loading;

namespace Ambient.Saga.Engine.Tests;

public class WorldConfigurationTests
{
    private readonly IWorldConfigurationLoader _configurationLoader = new WorldConfigurationLoader();
    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public WorldConfigurationTests()
    {
        // DefinitionXsd is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefinitionXsd");

        // WorldDefinitions is at solution root (shared by all Sandboxes)
        _dataDirectory = FindWorldDefinitionsDirectory();
    }

    private static string FindWorldDefinitionsDirectory()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory != null)
        {
            var worldDefPath = Path.Combine(directory, "WorldDefinitions");
            if (Directory.Exists(worldDefPath))
                return worldDefPath;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find WorldDefinitions directory");
    }

    // OBSOLETE: These tests depend on configurations (Lat0Height256, Kagoshima, VallesMarineris)
    // that no longer exist in the project. Only "Ise" configuration is currently available.
    // Commented out rather than deleted to preserve test patterns for future use.

    //[Fact]
    //public async Task LoadAvailableWorldConfigurations_ShouldReturnMultipleConfigurations()
    //{
    //    // Act
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

    //    // Assert
    //    Assert.NotNull(configurations);
    //    Assert.True(configurations.Length > 1);

    //    // Verify we have expected configurations
    //    var refNames = configurations.Select(c => c.RefName).ToList();
    //    Assert.Contains("Lat0Height256", refNames);
    //    Assert.Contains("Kagoshima", refNames);
    //    Assert.Contains("VallesMarineris", refNames);
    //}

    [Fact]
    public async Task LoadMetadata_ShouldReturnValidMetadata()
    {
        // Arrange - Use the Japan template directory
        var japanTemplateDirectory = Path.Combine(_dataDirectory, "Japan");

        // Act
        var metadata = await WorldAssetLoader.LoadMetadataAsync(japanTemplateDirectory, _definitionDirectory);

        // Assert
        Assert.NotNull(metadata);
        Assert.NotNull(metadata.Name);
        Assert.NotNull(metadata.Description);
        Assert.NotNull(metadata.Version);
        Assert.NotNull(metadata.Author);
    }

    //[Fact]
    //public async Task LoadWorldByConfiguration_Procedural_ShouldLoadSuccessfully()
    //{
    //    // Act - Use the new configuration-based loading approach
    //    var world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Lat0Height256");

    //    // Assert
    //    Assert.NotNull(world);
    //    Assert.NotNull(world.WorldConfiguration);
    //    Assert.Equal("Lat0Height256", world.WorldConfiguration.RefName);
    //    Assert.Equal("Procedural", world.WorldConfiguration.Template);
    //    Assert.IsType<ProceduralSettings>(world.WorldConfiguration.Item);

    //    // Verify world template was loaded
    //    Assert.NotNull(world.WorldTemplate);
    //    Assert.NotNull(world.WorldTemplate.Metadata);

    //    // Verify calculated values were set
    //    Assert.NotEqual(0, world.LatitudeUnitsToDegrees_Calculated);
    //    Assert.NotEqual(0, world.LongitudeUnitsToDegrees_Calculated);
    //}

    // OBSOLETE: Tests for missing configurations
    //[Theory]
    //[InlineData("Lat0Height256")]
    //[InlineData("Kagoshima")]
    //[InlineData("VallesMarineris")]
    //public async Task SpecificConfiguration_ShouldHaveCorrectType(string configRefName)
    //{
    //    // Arrange
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);
    //    var config = configurations.FirstOrDefault(c => c.RefName == configRefName);

    //    // Assert
    //    Assert.NotNull(config);

    //    if (configRefName == "Lat0Height256")
    //    {
    //        Assert.IsType<ProceduralSettings>(config.Item);
    //        var proceduralSettings = (ProceduralSettings)config.Item;
    //        Assert.InRange((int)proceduralSettings.ProceduralGenerationMode, 0, 1000);
    //    }
    //    else
    //    {
    //        Assert.IsType<HeightMapSettings>(config.Item);
    //        var heightMapSettings = (HeightMapSettings)config.Item;
    //    }
    //}

    // OBSOLETE: Tests for missing Lat0Height256 configuration
    //[Fact]
    //public async Task ProceduralSettings_ShouldHaveValidProperties()
    //{
    //    // Arrange
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);
    //    var proceduralConfig = configurations.First(c => c.RefName == "Lat0Height256");
    //    var proceduralSettings = (ProceduralSettings)proceduralConfig.Item;

    //    // Assert
    //    Assert.NotNull(proceduralSettings);
    //    Assert.NotEqual(ProceduralGenerationMode.Flat, proceduralSettings.ProceduralGenerationMode); // Should be Rugged based on XML
    //}

    //[Fact]
    //public async Task HeightMapSettings_ShouldHaveValidProperties()
    //{
    //    // Arrange
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);
    //    var heightMapConfig = configurations.First(c => c.Variant == WorldConfigurationVariant.HeightMap);
    //    var heightMapSettings = (HeightMapSettings)heightMapConfig.Item;

    //    // Assert
    //    Assert.NotNull(heightMapSettings);
    //    // HeightMapPath and HorizontalMapScaleMultiplier might be empty/0 in test data
    //}

    // OBSOLETE: Tests for missing configurations
    //[Fact]
    //public async Task ConfigurationChoice_ShouldBeSerializableAndDeserializable()
    //{
    //    // This test ensures the choice-based XML structure works correctly
    //    // Arrange
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

    //    // Act & Assert
    //    foreach (var config in configurations)
    //    {
    //        // Verify the choice was deserialized correctly
    //        Assert.NotNull(config.Item);

    //        if (config.RefName == "Lat0Height256")
    //        {
    //            Assert.IsType<ProceduralSettings>(config.Item);
    //        }
    //    }
    //}

    // ===== DIRECT ITEM PROPERTY TESTS (WITHOUT DELEGATES) =====

    //[Fact]
    //public async Task WorldConfiguration_ItemProperty_ShouldNotBeNull()
    //{
    //    // Test the Item property directly without any delegation
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

    //    foreach (var config in configurations)
    //    {
    //        // Direct property access - no delegation
    //        Assert.NotNull(config.Item);
    //        Assert.True(config.Variant == WorldConfigurationVariant.Procedural || config.Variant == WorldConfigurationVariant.HeightMap);
    //    }
    //}

    // OBSOLETE: Tests for missing Lat0Height256 configuration
    //[Fact]
    //public async Task WorldConfiguration_ItemProperty_ProceduralType_ShouldHaveCorrectProperties()
    //{
    //    // Test ProceduralSettings through Item property without casting or delegation
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);
    //    var proceduralConfig = configurations.FirstOrDefault(c => c.RefName == "Lat0Height256");

    //    Assert.NotNull(proceduralConfig);
    //    Assert.NotNull(proceduralConfig.Item);

    //    // Direct type check and access without switch or delegation
    //    if (proceduralConfig.Item.GetType() == typeof(ProceduralSettings))
    //    {
    //        var item = proceduralConfig.Item;
    //        Assert.IsType<ProceduralSettings>(item);

    //        // Use reflection to access properties without casting
    //        var itemType = item.GetType();
    //        var modeProperty = itemType.GetProperty("ProceduralGenerationMode");

    //        Assert.NotNull(modeProperty);

    //        var modeValue = modeProperty.GetValue(item);

    //        Assert.NotNull(modeValue);
    //        Assert.IsType<ProceduralGenerationMode>(modeValue);
    //    }
    //}

    [Fact]
    public async Task WorldConfiguration_ItemProperty_HeightMapType_ShouldHaveCorrectProperties()
    {
        // Test HeightMapSettings through Item property without casting or delegation
        var configurations = await _configurationLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);
        var heightMapConfig = configurations.FirstOrDefault(c => c.Item?.GetType() == typeof(HeightMapSettings));

        Assert.NotNull(heightMapConfig);
        Assert.NotNull(heightMapConfig.Item);

        // Direct type check and access without switch or delegation
        if (heightMapConfig.Item.GetType() == typeof(HeightMapSettings))
        {
            var item = heightMapConfig.Item;
            Assert.IsType<HeightMapSettings>(item);

            // Use reflection to access properties without casting
            var itemType = item.GetType();
            var pathProperty = itemType.GetProperty("RelativePath");

            Assert.NotNull(pathProperty);

            var pathValue = pathProperty.GetValue(item);

            Assert.IsType<string>(pathValue);
        }
    }

    // OBSOLETE: Tests for multiple configurations (only Ise exists)
    //[Fact]
    //public async Task WorldConfiguration_ItemProperty_ShouldMatchXmlChoicePattern()
    //{
    //    // Test that Item property correctly represents the XML choice pattern
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

    //    // Count configurations by type using Item property directly
    //    var proceduralCount = configurations.Count(c => c.Item?.GetType() == typeof(ProceduralSettings));
    //    var heightMapCount = configurations.Count(c => c.Item?.GetType() == typeof(HeightMapSettings));

    //    Assert.True(proceduralCount >= 1, "Should have at least one ProceduralSettings configuration");
    //    Assert.True(heightMapCount >= 1, "Should have at least one HeightMapSettings configuration");
    //    Assert.Equal(configurations.Length, proceduralCount + heightMapCount);
    //}

    [Fact]
    public async Task WorldConfiguration_ItemProperty_ShouldBeAccessibleWithoutCasting()
    {
        // Test accessing Item properties using GetType() and reflection instead of casting
        var configurations = await _configurationLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

        foreach (var config in configurations)
        {
            Assert.NotNull(config.Item);

            var itemType = config.Item.GetType();
            var itemTypeName = itemType.Name;

            // Test that we can identify the type without casting or switch statements
            Assert.True(itemTypeName == "ProceduralSettings" || itemTypeName == "HeightMapSettings");

            // Test that we can access properties via reflection without delegation
            var properties = itemType.GetProperties();
            Assert.NotEmpty(properties);

            if (itemTypeName == "ProceduralSettings")
            {
                var proceduralGenerationModeProperty = properties.FirstOrDefault(p => p.Name == "ProceduralGenerationMode");
                Assert.NotNull(proceduralGenerationModeProperty);
            }
            else if (itemTypeName == "HeightMapSettings")
            {
                var verticalScaleProperty = properties.FirstOrDefault(p => p.Name == "VerticalScale");
                Assert.NotNull(verticalScaleProperty);
                Assert.Equal(typeof(double), verticalScaleProperty.PropertyType);
            }
        }
    }

    // OBSOLETE: Tests for missing configurations
    //[Theory]
    //[InlineData("Lat0Height256", typeof(ProceduralSettings))]
    //[InlineData("Kagoshima", typeof(HeightMapSettings))]
    //[InlineData("VallesMarineris", typeof(HeightMapSettings))]
    //public async Task WorldConfiguration_ItemProperty_ShouldHaveExpectedTypeForConfiguration(string configRefName, Type expectedType)
    //{
    //    // Test specific configurations have expected Item types without casting
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);
    //    var config = configurations.FirstOrDefault(c => c.RefName == configRefName);

    //    Assert.NotNull(config);
    //    Assert.NotNull(config.Item);
    //    Assert.Equal(expectedType, config.Item.GetType());
    //}

    // ===== DIRECT CASTING TESTS (WHEN TYPE IS KNOWN) =====

    // OBSOLETE: Tests for missing Lat0Height256 configuration
    //[Fact]
    //public async Task WorldConfiguration_DirectCast_ProceduralSettings_WhenTypeIsKnown()
    //{
    //    // Test direct casting when you KNOW it's ProceduralSettings (to replace delegates)
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);
    //    var proceduralConfig = configurations.FirstOrDefault(c => c.RefName == "Lat0Height256");

    //    Assert.NotNull(proceduralConfig);
    //    Assert.NotNull(proceduralConfig.Item);
    //    Assert.IsType<ProceduralSettings>(proceduralConfig.Item);

    //    // Direct cast when you KNOW the type - this is what you'd use instead of delegates
    //    var proceduralSettings = (ProceduralSettings)proceduralConfig.Item;

    //    // Now you can use proceduralSettings directly without any delegation
    //    Assert.NotNull(proceduralSettings);
    //    Assert.NotEqual(ProceduralGenerationMode.Flat, proceduralSettings.ProceduralGenerationMode);

    //    // This demonstrates you can access properties directly without world.GenerationMode delegates
    //    var mode = proceduralSettings.ProceduralGenerationMode;

    //    Assert.True(Enum.IsDefined(typeof(ProceduralGenerationMode), mode));
    //}

    // OBSOLETE: Tests for missing Kagoshima configuration
    //[Fact]
    //public async Task WorldConfiguration_DirectCast_HeightMapSettings_WhenTypeIsKnown()
    //{
    //    // Test direct casting when you KNOW it's HeightMapSettings (to replace delegates)
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);
    //    var heightMapConfig = configurations.FirstOrDefault(c => c.RefName == "Kagoshima");

    //    Assert.NotNull(heightMapConfig);
    //    Assert.NotNull(heightMapConfig.Item);
    //    Assert.IsType<HeightMapSettings>(heightMapConfig.Item);

    //    // Direct cast when you KNOW the type - this is what you'd use instead of delegates
    //    var heightMapSettings = (HeightMapSettings)heightMapConfig.Item;

    //    // Now you can use heightMapSettings directly without any delegation
    //    Assert.NotNull(heightMapSettings);

    //    // This demonstrates you can access properties directly without delegates
    //    var heightMapPath = heightMapSettings.RelativePath;
    //    var verticalScale = heightMapSettings.VerticalScale;

    //    Assert.NotNull(heightMapPath); // Can be empty string, but not null
    //    Assert.True(verticalScale >= 0);
    //}

    //[Fact]
    //public async Task WorldConfiguration_SafeCasting_WithTypeCheck()
    //{
    //    // Test safe casting pattern with type checking (alternative to switch statement)
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

    //    foreach (var config in configurations)
    //    {
    //        Assert.NotNull(config.Item);

    //        // Pattern 1: Switch on Variant enum for clean type handling
    //        switch (config.Variant)
    //        {
    //            case WorldConfigurationVariant.Procedural:
    //                var proceduralSettings = config.AsProcedural!;
    //                Assert.True(Enum.IsDefined(typeof(ProceduralGenerationMode), proceduralSettings.ProceduralGenerationMode));
    //                // You could set world properties directly:
    //                // world.ProceduralSettings = proceduralSettings;
    //                break;

    //            case WorldConfigurationVariant.HeightMap:
    //                var heightMapSettings = config.AsHeightMap!;
    //                Assert.NotNull(heightMapSettings.RelativePath);
    //                // You could set world properties directly:
    //                // world.HeightMapSettings = heightMapSettings;
    //                break;

    //            default:
    //                Assert.True(false, $"Unexpected configuration variant: {config.Variant}");
    //                break;
    //        }
    //    }
    //}

    // OBSOLETE: Tests for missing configurations
    //[Theory]
    //[InlineData("Lat0Height256")]
    //[InlineData("Kagoshima")]
    //[InlineData("VallesMarineris")]
    //public async Task WorldConfiguration_KnownTypeDirectCast_ShouldWork(string configRefName)
    //{
    //    // Test that you can safely cast when you know the configuration type
    //    var configurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);
    //    var config = configurations.FirstOrDefault(c => c.RefName == configRefName);

    //    Assert.NotNull(config);
    //    Assert.NotNull(config.Item);

    //    // Based on configuration name, you KNOW the type, so cast directly
    //    if (configRefName == "Lat0Height256")
    //    {
    //        // You know this is ProceduralSettings
    //        var settings = (ProceduralSettings)config.Item;
    //        Assert.NotNull(settings);
    //    }
    //    else
    //    {
    //        // You know this is HeightMapSettings
    //        var settings = (HeightMapSettings)config.Item;
    //        Assert.NotNull(settings);
    //    }
    //}
}
