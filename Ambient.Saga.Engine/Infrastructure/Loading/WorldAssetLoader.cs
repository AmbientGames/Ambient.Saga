using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure.GameLogic.Services;
using Ambient.Infrastructure.Sampling;
using Ambient.Infrastructure.Utilities;
using System.Diagnostics;

namespace Ambient.Saga.Engine.Infrastructure.Loading;

public static class WorldAssetLoader
{
    /// <summary>
    /// Loads a world by finding the WorldConfiguration with the specified RefName.
    /// This is a convenience method that combines LoadAvailableWorldConfigurationsAsync and LoadWorldAsync.
    /// </summary>
    /// <param name="worldFactory">Factory to create world instances</param>
    /// <param name="dataDirectory">Base data directory</param>
    /// <param name="definitionDirectory">Definition directory for validation</param>
    /// <param name="configurationRefName">The RefName of the WorldConfiguration to load (e.g., "Lat0Height256", "Kagoshima")</param>
    /// <returns>A fully loaded World with the specified configuration</returns>
    /// <exception cref="InvalidOperationException">Thrown if the configuration RefName is not found</exception>
    public static async Task<IWorld> LoadWorldByConfigurationAsync(IWorldFactory worldFactory, string dataDirectory, string definitionDirectory, string configurationRefName)
    {
        // Load available configurations
        var configurations = await WorldConfigurationLoader.LoadAvailableWorldConfigurationsAsync(dataDirectory, definitionDirectory);

        // Find the requested configuration
        var worldConfiguration = configurations.FirstOrDefault(c => c.RefName == configurationRefName);
        if (worldConfiguration == null)
        {
            throw new InvalidOperationException($"WorldConfiguration with RefName '{configurationRefName}' not found");
        }

        // Load the world using the found configuration
        return await LoadWorldAsync(worldFactory, dataDirectory, definitionDirectory, worldConfiguration);
    }

    private static async Task<IWorld> LoadWorldAsync(IWorldFactory worldFactory, string dataDirectory, string definitionDirectory, WorldConfiguration worldConfiguration)
    {
        var world = worldFactory.CreateWorld();
        world.WorldConfiguration = worldConfiguration;

        // Determine the template directory from WorldConfiguration.Template
        var templateDirectory = Path.Combine(dataDirectory, worldConfiguration.Template);

        world.WorldTemplate.Metadata = await LoadMetadataAsync(templateDirectory, definitionDirectory);
        await LoadGamePlayAsync(templateDirectory, definitionDirectory, world);

        world.IsProcedural = world.WorldConfiguration.ProceduralSettings != null;

        // Load heightmap metadata first if needed (required for calculations)
        if (!world.IsProcedural)
        {
            await LoadHeightMapMetadata(dataDirectory, world);
        }

        if (!world.IsProcedural)
        {
            var metadata = world.HeightMapMetadata;
            var heightMapSettings = worldConfiguration.HeightMapSettings;

            const double metersPerDegreeAtEquator = 111319.5;

            heightMapSettings.MapResolutionInMeters = metadata.PixelScale.Y * metersPerDegreeAtEquator;
        }

        // Calculate derived values from WorldConfiguration
        world.HeightMapLatitudeScale = world.IsProcedural ? 1 : (int)Math.Round(worldConfiguration.HeightMapSettings.MapResolutionInMeters * worldConfiguration.HeightMapSettings.HorizontalScale);

        // Calculate longitude scale with latitude correction
        if (world.IsProcedural)
        {
            world.HeightMapLongitudeScale = 1;
        }
        else
        {
            var centerLatitude = (world.HeightMapMetadata.North + world.HeightMapMetadata.South) / 2.0;
            var latitudeCorrectionFactor = Math.Cos(centerLatitude * Math.PI / 180.0);
            world.HeightMapLongitudeScale = world.HeightMapLatitudeScale / latitudeCorrectionFactor;
        }


        // Calculate longitude scale with latitude correction
        if (world.IsProcedural)
        {
            world.HeightMapLongitudeScale = 1;
        }
        else
        {
            var centerLatitude = (world.HeightMapMetadata.North + world.HeightMapMetadata.South) / 2.0;
            var latitudeCorrectionFactor = Math.Cos(centerLatitude * Math.PI / 180.0);
            world.HeightMapLongitudeScale = world.HeightMapLatitudeScale / latitudeCorrectionFactor;
        }

        // Calculate spawn anchor points after height map metadata is loaded
        WorldConfigurationService.CalculateSpawnAnchorPoints(world);

        // Validate referential integrity after all lookups are built
        WorldValidationService.ValidateReferentialIntegrity(world);

        return world;
    }

    private static async Task LoadGamePlayAsync(string dataDirectory, string definitionDirectory, IWorld world)
    {
        await GameplayComponentLoader.LoadAsync(dataDirectory, definitionDirectory, world);
    }

    public static async Task<TemplateMetadata> LoadMetadataAsync(string dataDirectory, string definitionDirectory)
    {
        var xsdFilePath = Path.Combine(definitionDirectory, "WorldTemplateMetadata.xsd");
        return await XmlLoader.LoadFromXmlAsync<TemplateMetadata>(Path.Combine(dataDirectory, "TemplateMetadata.xml"), xsdFilePath);
    }

    private static async Task LoadHeightMapMetadata(string dataDirectory, IWorld world)
    {
        var relativePath = Path.Combine(dataDirectory, world.WorldConfiguration.HeightMapSettings.RelativePath);

        world.HeightMapMetadata = GeoTiffReader.ReadMetadata(relativePath);
    }
}