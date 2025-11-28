using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Infrastructure.Loading;

namespace Ambient.SagaEngine.Tests;

public class LoadingTests : IAsyncLifetime
{
    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;
    private World? _world;

    public LoadingTests()
    {
        var solutionRoot = FindSolutionRoot();
        _dataDirectory = Path.Combine(solutionRoot, "Ambient.Saga.Sandbox.WindowsUI", "WorldDefinitions");
        _definitionDirectory = Path.Combine(solutionRoot, "Ambient.Saga.Sandbox.WindowsUI", "DefinitionXsd");
    }

    private static string FindSolutionRoot()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory != null && !File.Exists(Path.Combine(directory, "Ambient.Saga.sln")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        if (directory == null)
            throw new InvalidOperationException("Could not find solution root (Ambient.Saga.sln)");

        return directory;
    }

    public async Task InitializeAsync()
    {
        _world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Ise");
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public void WorldCombined_ShouldNotBeNull()
    {
        Assert.NotNull(_world);
    }

    //[Fact]
    //public void WorldCombinedTexturesGenerated_IsNotEmpty()
    //{
    //    Assert.NotNull(_world);
    //    Assert.NotNull(_world.Presentation.BlockTextures);
    //    Assert.NotEmpty(_world.Presentation.BlockTextures.Generated);
    //}

    [Fact]
    public void WorlConfiguration_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.WorldConfiguration);
    }

    //[Fact]
    //public void Blocks_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Simulation.Blocks);
    //}

    //[Fact]
    //public void Textures_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Presentation.BlockTextures);
    //}

    [Fact]
    public void Consumables_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.Consumables);
    }

    [Fact]
    public void Equipment_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.Equipment);
    }

    [Fact]
    public void Characters_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.Characters);
    }

    [Fact]
    public void Avatars_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.AvatarArchetypes);
    }

    [Fact]
    public void Tools_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.Tools);
    }

    [Fact]
    public void DialogueTrees_ShouldNotBeNull()
    {
        Assert.NotNull(_world?.Gameplay.DialogueTrees);
    }

    //[Fact]
    //public void Structures_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Gameplay.Structures);
    //}

    //[Fact]
    //public void Substances_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Simulation.Substances);
    //}

    //[Fact]
    //public void LivingShapes_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Simulation.LivingShapes);
    //}

    //[Fact]
    //public void States_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Simulation.States);
    //}

    //[Fact]
    //public void Styles_ShouldNotBeNull()
    //{
    //    Assert.NotNull(_world?.Simulation.Styles);
    //}

    //[Fact]
    //public async Task AvailableWorldConfigurations_ShouldLoadMultipleConfigurations()
    //{
    //    // Act
    //    var availableConfigurations = await WorldAssetLoader.LoadAvailableWorldConfigurationsAsync(_dataDirectory, _definitionDirectory);

    //    // Assert
    //    Assert.NotNull(availableConfigurations);
    //    Assert.True(availableConfigurations.Length > 1);
        
    //    // Verify we have both procedural and height map configurations
    //    var hasProcedural = availableConfigurations.Any(config =>
    //        config.Variant == WorldConfigurationVariant.Procedural);
    //    var hasHeightMap = availableConfigurations.Any(config =>
    //        config.Variant == WorldConfigurationVariant.HeightMap);
        
    //    Assert.True(hasProcedural, "Should have at least one ProceduralSettings configuration");  
    //    Assert.True(hasHeightMap, "Should have at least one HeightMapSettings configuration");
    //}

    [Fact]
    public void SelectedWorldConfiguration_ShouldBeAssigned()
    {
        Assert.NotNull(_world?.WorldConfiguration);
        Assert.NotNull(_world.WorldConfiguration.RefName);
        Assert.NotEmpty(_world.WorldConfiguration.RefName);
    }

    [Fact]
    public void ProceduralConfiguration_ShouldCastCorrectly()
    {
        // The loaded world should have Procedural configuration
        Assert.NotNull(_world?.WorldConfiguration);
        Assert.Equal("Lat0Height256", _world.WorldConfiguration.RefName);
        Assert.IsType<ProceduralSettings>(_world.WorldConfiguration.Item);
        
        var proceduralSettings = (ProceduralSettings)_world.WorldConfiguration.Item;
        Assert.NotNull(proceduralSettings);
        Assert.InRange((int)proceduralSettings.ProceduralGenerationMode, 0, 1000);
    }

    [Fact]
    public async Task HeightMapConfiguration_ShouldCastCorrectly()
    {
        // Act - Load Kagoshima world which uses HeightMapSettings
        var kagoshimaWorld = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Kagoshima");
        
        // Assert
        Assert.NotNull(kagoshimaWorld.WorldConfiguration);
        Assert.Equal("Kagoshima", kagoshimaWorld.WorldConfiguration.RefName);
        Assert.IsType<HeightMapSettings>(kagoshimaWorld.WorldConfiguration.Item);
        
        var heightMapSettings = (HeightMapSettings)kagoshimaWorld.WorldConfiguration.Item;
        Assert.NotNull(heightMapSettings);
    }

    [Fact]
    public void ConfigurationSwitchPattern_ShouldWorkForProcedural()
    {
        // The world is loaded with Procedural configuration
        Assert.NotNull(_world?.WorldConfiguration);
        Assert.Equal("Lat0Height256", _world.WorldConfiguration.RefName);
        Assert.IsType<ProceduralSettings>(_world.WorldConfiguration.Item);
    }

    [Fact]
    public async Task ConfigurationSwitchPattern_ShouldWorkForHeightMap()
    {
        // Load a HeightMap configuration to test switch pattern
        var heightMapWorld = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Kagoshima");
        Assert.NotNull(heightMapWorld.WorldConfiguration);
        Assert.IsType<HeightMapSettings>(heightMapWorld.WorldConfiguration.Item);
    }

    [Fact]
    public void ConfigurationProperties_ShouldBeAccessible()
    {
        Assert.NotNull(_world?.WorldConfiguration);
        
        // Test basic properties
        Assert.NotNull(_world.WorldConfiguration.DisplayName);
        Assert.NotNull(_world.WorldConfiguration.Description);
        Assert.NotNull(_world.WorldConfiguration.Template);
    }

    [Fact]
    public void WorldTemplate_ShouldHaveMetadata()
    {
        Assert.NotNull(_world?.WorldTemplate?.Metadata);
        Assert.NotNull(_world.WorldTemplate.Metadata.Name);
        Assert.NotNull(_world.WorldTemplate.Metadata.Description);
        Assert.NotNull(_world.WorldTemplate.Metadata.Version);
        Assert.NotNull(_world.WorldTemplate.Metadata.Author);
    }

    [Fact]
    public void Sagas_ShouldBeLoaded()
    {
        Assert.NotNull(_world?.Gameplay.SagaArcs);
        Assert.NotEmpty(_world.Gameplay.SagaArcs);

        // Test that sagas have proper coordinates
        foreach (var saga in _world.Gameplay.SagaArcs)
        {
            Assert.NotNull(saga.RefName);
            Assert.NotNull(saga.DisplayName);
            // Coordinates can be 0, so just check they're defined
            Assert.True(saga.LatitudeZ != double.MinValue);
            Assert.True(saga.LongitudeX != double.MinValue);
        }
    }

    [Fact]
    public void SagasLookup_ShouldWork()
    {
        Assert.NotNull(_world?.SagaArcLookup);
        Assert.NotEmpty(_world.SagaArcLookup);

        // Test that we can find sagas by RefName
        var firstSaga = _world.Gameplay.SagaArcs.First();
        var foundSaga = _world.GetSagaArcByRefName(firstSaga.RefName);

        Assert.Equal(firstSaga.RefName, foundSaga.RefName);
        Assert.Equal(firstSaga.DisplayName, foundSaga.DisplayName);
    }

    [Fact]
    public async Task HeightMapMetadata_ShouldBeLoadedForHeightMapConfigurations()
    {
        // Act - Load Kagoshima world which uses HeightMapSettings
        var kagoshimaWorld = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Kagoshima");
        
        // Assert
        Assert.NotNull(kagoshimaWorld.HeightMapMetadata);
        
        // Verify basic metadata properties are accessible
        Assert.True(kagoshimaWorld.HeightMapMetadata.ImageWidth > 0);
        Assert.True(kagoshimaWorld.HeightMapMetadata.ImageHeight > 0);
    }

    [Fact]
    public async Task HeightMapBounds_ShouldBeLoadedForHeightMapConfigurations()
    {
        // Act - Load Kagoshima world which uses HeightMapSettings
        var kagoshimaWorld = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Kagoshima");
        
        // Assert
        Assert.NotNull(kagoshimaWorld.HeightMapMetadata);
        
        // Now we can do proper math operations on the bounds!
        var metadata = kagoshimaWorld.HeightMapMetadata;
        Assert.True(metadata.North > metadata.South, "North should be greater than South");
        Assert.True(metadata.East > metadata.West, "East should be greater than West");
        Assert.True(metadata.Width > 0, "Width should be positive");
        Assert.True(metadata.Height > 0, "Height should be positive");
        
        // Verify the bounds make sense for Japan (Kagoshima area)
        Assert.InRange(metadata.North, 24.0, 46.0);
        Assert.InRange(metadata.South, 24.0, 46.0);
        Assert.InRange(metadata.East, 123.0, 146.0);
        Assert.InRange(metadata.West, 123.0, 146.0);
    }

    [Fact]
    public void ProceduralConfiguration_ShouldNotHaveHeightMapMetadata()
    {
        // The default _world uses ProceduralSettings, not HeightMapSettings
        Assert.NotNull(_world?.WorldConfiguration);
        Assert.IsType<ProceduralSettings>(_world.WorldConfiguration.Item);
        
        // Should not have height map metadata for procedural worlds
        Assert.Null(_world.HeightMapMetadata);
    }

    [Fact]
    public async Task HeightMapMetadata_ShouldContainGeoTiffInformation()
    {
        // Act - Load Kagoshima world which uses HeightMapSettings  
        var kagoshimaWorld = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Kagoshima");
        
        // Assert - Check for GeoTIFF-specific metadata
        Assert.NotNull(kagoshimaWorld.HeightMapMetadata);
        var metadata = kagoshimaWorld.HeightMapMetadata;
        
        // Should have pixel scale and tie point information for proper GeoTIFF files
        Assert.True(metadata.PixelScale.X > 0);
        Assert.True(metadata.PixelScale.Y > 0);
        Assert.True(metadata.PixelScale.Z >= 0); // Z can be 0
        
        // Verify tie point has reasonable values for raster and model coordinates
        Assert.True(metadata.TiePoint.I >= 0); // Raster X
        Assert.True(metadata.TiePoint.J >= 0); // Raster Y
        Assert.True(metadata.TiePoint.X != 0); // Model X (longitude)
        Assert.True(metadata.TiePoint.Y != 0); // Model Y (latitude)
    }

    [Fact]
    public async Task HeightMapMetadata_ShouldSupportMathOperations()
    {
        // Act - Load Kagoshima world which uses HeightMapSettings
        var kagoshimaWorld = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Kagoshima");
        
        // Assert
        Assert.NotNull(kagoshimaWorld.HeightMapMetadata);
        var metadata = kagoshimaWorld.HeightMapMetadata;
        
        // Demonstrate math operations that are now possible
        var centerLatitude = (metadata.North + metadata.South) / 2;
        var centerLongitude = (metadata.East + metadata.West) / 2;
        var aspectRatio = metadata.Width / metadata.Height;
        var area = metadata.Width * metadata.Height; // in square degrees
        
        // Calculate pixel size in degrees
        var pixelSizeX = metadata.Width / metadata.ImageWidth;
        var pixelSizeY = metadata.Height / metadata.ImageHeight;
        
        // Verify calculated values are reasonable
        Assert.InRange(centerLatitude, metadata.South, metadata.North);
        Assert.InRange(centerLongitude, metadata.West, metadata.East);
        Assert.True(aspectRatio > 0);
        Assert.True(area > 0);
        Assert.True(pixelSizeX > 0);
        Assert.True(pixelSizeY > 0);
        
        // For a 1x1 degree tile, area should be close to 1
        Assert.InRange(area, 0.5, 2.0);
        
        // Verify we can access both bounds and image properties
        Assert.True(metadata.ImageWidth > 0);
        Assert.True(metadata.ImageHeight > 0);
    }
}