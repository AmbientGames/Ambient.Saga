using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Infrastructure.Loading;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Unit tests for WorldAssetLoader calculation methods and World calculated properties.
/// Tests the formula: UnitsToDegrees = 1.0 / DegreesToUnits / Scale
/// </summary>
public class WorldCalculationTests
{
    private readonly IWorldFactory _worldFactory = new TestWorldFactory();
    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public WorldCalculationTests()
    {
        // DefinitionXsd is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefinitionXsd");

        // WorldDefinitions still lives in Sandbox source directory
        var sandboxDirectory = FindSandboxDirectory();
        _dataDirectory = Path.Combine(sandboxDirectory, "WorldDefinitions");
    }

    private static string FindSandboxDirectory()
    {
        var directory = AppDomain.CurrentDomain.BaseDirectory;
        while (directory != null)
        {
            var domainPath = Path.Combine(directory, "Ambient.Saga.Sandbox.WindowsUI");
            if (Directory.Exists(domainPath))
                return domainPath;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Could not find Sandbox directory");
    }

    //[Fact]
    //public void CalculateUnitsToDegrees_ProceduralWorld_ShouldUseScaleOfOne()
    //{
    //    // Arrange
    //    var world = new World();
    //    var worldConfiguration = new WorldConfiguration
    //    {
    //        LatitudeDegreesToUnits = 1000,
    //        LongitudeDegreesToUnits = 1000,
    //        Item = new ProceduralSettings
    //        {
    //            ProceduralGenerationMode = ProceduralGenerationMode.Rugged
    //        }
    //    };

    //    // Act
    //    WorldConfigurationService.CalculateUnitsToDegrees(world, worldConfiguration);

    //    // Assert
    //    // For procedural worlds, scale = 1.0
    //    // Expected: 1.0 / 1000 / 1.0 = 0.001
    //    Assert.Equal(0.001, world.LatitudeUnitsToDegrees_Calculated, 6);
    //    Assert.Equal(0.001, world.LongitudeUnitsToDegrees_Calculated, 6);
    //}

    //[Fact]
    //public void CalculateUnitsToDegrees_HeightMapWorld_ShouldUseHeightMapScale()
    //{
    //    // Arrange
    //    var world = new World();
    //    var worldConfiguration = new WorldConfiguration
    //    {
    //        LatitudeDegreesToUnits = 59300,
    //        LongitudeDegreesToUnits = 59000,
    //        Item = new HeightMapSettings
    //        {
    //            RelativePath = "test.tif",
    //            MapResolutionInMeters = 200,
    //            HorizontalScale = 0.1,
    //            VerticalScale = 0.1,
    //            VerticalShift = -109,
    //        }
    //    };

    //    // Act
    //    WorldConfigurationService.CalculateUnitsToDegrees(world, worldConfiguration);

    //    // Assert
    //    // Expected: 1.0 / 59300 / 0.1 = 0.0001686 (approximately)
    //    // Expected: 1.0 / 59000 / 0.1 = 0.0001694 (approximately)
    //    var expectedLatitude = 1.0 / 59300 / 0.1;
    //    var expectedLongitude = 1.0 / 59000 / 0.1;
        
    //    Assert.Equal(expectedLatitude, world.LatitudeUnitsToDegrees_Calculated, 10);
    //    Assert.Equal(expectedLongitude, world.LongitudeUnitsToDegrees_Calculated, 10);
    //}

    //[Theory]
    //[InlineData(110850, 95000, 0.333333)] // Kagoshima/Sakurajima
    //[InlineData(110730, 91000, 0.333333)] // MountFuji
    //[InlineData(111070, 68450, 0.333333)] // Saskatchewan
    //[InlineData(111320, 98310, 0.333333)] // EverestRegion
    //public void CalculateUnitsToDegrees_VariousHeightMapConfigurations_ShouldCalculateCorrectly(
    //    double latitudeDegreesToUnits, double longitudeDegreesToUnits, double scale)
    //{
    //    // Arrange
    //    var world = new World();
    //    var worldConfiguration = new WorldConfiguration
    //    {
    //        LatitudeDegreesToUnits = latitudeDegreesToUnits,
    //        LongitudeDegreesToUnits = longitudeDegreesToUnits,
    //        Item = new HeightMapSettings
    //        {
    //            RelativePath = "test.tif",
    //            MapResolutionInMeters = 30,
    //            HorizontalScale = scale,
    //            VerticalScale = scale,
    //            VerticalShift = -109,
    //        }
    //    };

    //    // Act
    //    WorldConfigurationService.CalculateUnitsToDegrees(world, worldConfiguration);

    //    // Assert
    //    var expectedLatitude = 1.0 / latitudeDegreesToUnits / scale;
    //    var expectedLongitude = 1.0 / longitudeDegreesToUnits / scale;
        
    //    Assert.Equal(expectedLatitude, world.LatitudeUnitsToDegrees_Calculated, 10);
    //    Assert.Equal(expectedLongitude, world.LongitudeUnitsToDegrees_Calculated, 10);
    //}

    //[Fact]
    //public void CalculateUnitsToDegrees_VallesMarinerisExample_ShouldMatchExpectedValues()
    //{
    //    // Arrange - Using the actual VallesMarineris values
    //    var world = new World();
    //    var worldConfiguration = new WorldConfiguration
    //    {
    //        LatitudeDegreesToUnits = 59300,
    //        LongitudeDegreesToUnits = 59000,
    //        Item = new HeightMapSettings
    //        {
    //            RelativePath = "GeographicData/VallesMarineris_Lowspot_35700_29500_Shifted_L16.tif",
    //            MapResolutionInMeters = 200,
    //            HorizontalScale = 0.1,
    //            VerticalScale = 0.1,
    //            VerticalShift = -109,
    //        }
    //    };

    //    // Act
    //    WorldConfigurationService.CalculateUnitsToDegrees(world, worldConfiguration);

    //    // Assert - These should match what HeightMapInitializer produces for VallesMarineris
    //    // HeightMapInitializer: world.LatitudeUnitsToDegrees_Wip = 1.0 / 59300 / world.Scale_Wip;
    //    var expectedLatitude = 1.0 / 59300 / 0.1; // ≈ 0.0001686
    //    var expectedLongitude = 1.0 / 59000 / 0.1; // ≈ 0.0001694
        
    //    Assert.Equal(expectedLatitude, world.LatitudeUnitsToDegrees_Calculated, 10);
    //    Assert.Equal(expectedLongitude, world.LongitudeUnitsToDegrees_Calculated, 10);
    //}

    //[Fact]
    //public void CalculateUnitsToDegrees_ProceduralDefaultValues_ShouldMatchExpectedValues()
    //{
    //    // Arrange - Using typical procedural world values
    //    var world = new World();
    //    var worldConfiguration = new WorldConfiguration
    //    {
    //        LatitudeDegreesToUnits = 1000,
    //        LongitudeDegreesToUnits = 1000,
    //        Item = new ProceduralSettings
    //        {
    //            ProceduralGenerationMode = ProceduralGenerationMode.Rugged
    //        }
    //    };

    //    // Act
    //    WorldConfigurationService.CalculateUnitsToDegrees(world, worldConfiguration);

    //    // Assert - For procedural, scale = 1.0
    //    // HeightMapInitializer: world.LatitudeUnitsToDegrees_Wip = 1.0 / 1000 / world.Scale_Wip;
    //    var expectedLatitude = 1.0 / 1000 / 1.0; // = 0.001
    //    var expectedLongitude = 1.0 / 1000 / 1.0; // = 0.001
        
    //    Assert.Equal(expectedLatitude, world.LatitudeUnitsToDegrees_Calculated, 6);
    //    Assert.Equal(expectedLongitude, world.LongitudeUnitsToDegrees_Calculated, 6);
    //}

    //[Fact]
    //public async Task LoadWorldByConfigurationAsync_WithValidRefName_ShouldLoadCorrectWorld()
    //{
    //    // Act
    //    var world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Lat0Height256");

    //    // Assert
    //    Assert.NotNull(world);
    //    Assert.NotNull(world.WorldConfiguration);
    //    Assert.Equal("Lat0Height256", world.WorldConfiguration.RefName);
    //    Assert.IsType<ProceduralSettings>(world.WorldConfiguration.Item);
        
    //    // Verify calculated values were set
    //    Assert.NotEqual(0, world.LatitudeUnitsToDegrees_Calculated);
    //    Assert.NotEqual(0, world.LongitudeUnitsToDegrees_Calculated);
    //}

    [Fact]
    public async Task LoadWorldByConfigurationAsync_WithInvalidRefName_ShouldThrowException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WorldAssetLoader.LoadWorldByConfigurationAsync(_worldFactory, _dataDirectory, _definitionDirectory, "NonExistentConfig"));
        
        Assert.Contains("WorldConfiguration with RefName 'NonExistentConfig' not found", exception.Message);
    }
}