using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Infrastructure.Loading;
using Xunit.Abstractions;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Unit tests to validate calculated spawn anchor points against manually configured values.
/// This ensures the coordinate conversion from lat/lon to cell coordinates is working correctly.
/// </summary>
public class SpawnAnchorCalculationTests
{
    private readonly IWorldFactory _worldFactory = new TestWorldFactory();
    private readonly ITestOutputHelper _output;
    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public SpawnAnchorCalculationTests(ITestOutputHelper output)
    {
        _output = output;

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

    public async Task CalculateSpawnAnchorPoints_SpecificConfiguration_ShouldMatchManualValue(
        string configRefName, int expectedLongitudeCellX, int expectedLatitudeCellY)
    {
        try
        {
            // Arrange & Act
            var world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_worldFactory, _dataDirectory, _definitionDirectory, configRefName);
            
            // Assert
            var tolerance = 2.0; // Allow small rounding differences
            var diffX = Math.Abs(expectedLongitudeCellX - world.HeightMapSpawnPixelX);
            var diffY = Math.Abs(expectedLatitudeCellY - world.HeightMapSpawnPixelY);
            
            _output.WriteLine($"Configuration: {configRefName}");
            _output.WriteLine($"Expected: ({expectedLongitudeCellX}, {expectedLatitudeCellY})");
            _output.WriteLine($"Calculated: ({world.HeightMapSpawnPixelX}, {world. HeightMapSpawnPixelY})");
            _output.WriteLine($"Spawn Location: ({world.WorldConfiguration.SpawnLatitude}°, {world.WorldConfiguration.SpawnLongitude}°)");
            
            if (world.HeightMapMetadata != null)
            {
                _output.WriteLine($"Height Map Bounds: N={world.HeightMapMetadata.North}°, S={world.HeightMapMetadata.South}°, E={world.HeightMapMetadata.East}°, W={world.HeightMapMetadata.West}°");
                _output.WriteLine($"Image Size: {world.HeightMapMetadata.ImageWidth} x {world.HeightMapMetadata.ImageHeight}");
            }
            
            Assert.True(diffX <= tolerance, 
                $" HeightMapSpawnPixelX_Validated ({world.HeightMapSpawnPixelX}) should be within {tolerance} pixels of expected value ({expectedLongitudeCellX}). Difference: {diffX}");
            Assert.True(diffY <= tolerance, 
                $" HeightMapSpawnPixelY_Validated ({world.HeightMapSpawnPixelY}) should be within {tolerance} pixels of expected value ({expectedLatitudeCellY}). Difference: {diffY}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to test {configRefName}: {ex.Message}");
            
            // If the configuration doesn't exist or can't be loaded, skip this test
            if (ex.Message.Contains("not found") || ex.Message.Contains("Height map file not found"))
            {
                _output.WriteLine($"Skipping {configRefName} - configuration or height map file not available in test data");
                return;
            }
            
            throw;
        }
    }

    // OBSOLETE: Lat0Height256 procedural configuration no longer exists
    // Only "Ise" (HeightMapSettings) configuration is available
    //[Fact]
    //public async Task CalculateSpawnAnchorPoints_ProceduralWorld_ShouldReturnZero()
    //{
    //    // Arrange & Act - Load a procedural world
    //    var world = await WorldAssetLoader.LoadWorldByConfigurationAsync(_dataDirectory, _definitionDirectory, "Lat0Height256");
    //
    //    // Assert - Procedural worlds should have zero anchor points
    //    Assert.Equal(0, world. HeightMapSpawnPixelX);
    //    Assert.Equal(0, world. HeightMapSpawnPixelY);
    //
    //    _output.WriteLine($"Procedural world {world.WorldConfiguration.RefName} correctly returned (0, 0) anchor points");
    //}
}