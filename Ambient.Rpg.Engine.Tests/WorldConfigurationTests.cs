using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Infrastructure.GameLogic.Loading;

namespace Ambient.Rpg.Engine.Tests;

public class WorldConfigurationTests
{
    private readonly IWorldConfigurationLoader _configurationLoader = TestWorldFactory.CreateTestWorldConfigurationLoader();
    private readonly string _dataDirectory;
    private readonly string _definitionDirectory;

    public WorldConfigurationTests()
    {
        // Content/xsd is copied to output directory by Ambient.Domain
        _definitionDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "xsd");

        // Content/Worlds is at solution root (shared by all Sandboxes)
        _dataDirectory = FindWorldsDirectory();
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
}
