using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure.GameLogic.Services;
using Ambient.Infrastructure.Sampling;
using Ambient.Infrastructure.Utilities;
using System.Diagnostics;

namespace Ambient.SagaEngine.Infrastructure.Loading;

public static class WorldAssetLoader
{
    /// <summary>
    /// Loads a world by finding the WorldConfiguration with the specified RefName.
    /// This is a convenience method that combines LoadAvailableWorldConfigurationsAsync and LoadWorldAsync.
    /// </summary>
    /// <param name="dataDirectory">Base data directory</param>
    /// <param name="definitionDirectory">Definition directory for validation</param>
    /// <param name="configurationRefName">The RefName of the WorldConfiguration to load (e.g., "Lat0Height256", "Kagoshima")</param>
    /// <returns>A fully loaded World with the specified configuration</returns>
    /// <exception cref="InvalidOperationException">Thrown if the configuration RefName is not found</exception>
    public static async Task<World> LoadWorldByConfigurationAsync(string dataDirectory, string definitionDirectory, string configurationRefName)
    {
        // Load available configurations
        var configurations = await LoadAvailableWorldConfigurationsAsync(dataDirectory, definitionDirectory);
        
        // Find the requested configuration
        var worldConfiguration = configurations.FirstOrDefault(c => c.RefName == configurationRefName);
        if (worldConfiguration == null)
        {
            throw new InvalidOperationException($"WorldConfiguration with RefName '{configurationRefName}' not found");
        }

        // Load the world using the found configuration
        return await LoadWorldAsync(dataDirectory, definitionDirectory, worldConfiguration);
    }

    private static async Task<World> LoadWorldAsync(string dataDirectory, string definitionDirectory, WorldConfiguration worldConfiguration)
    {
        var world = new World
        {
            WorldConfiguration = worldConfiguration
        };

        // Determine the template directory from WorldConfiguration.Template
        var templateDirectory = Path.Combine(dataDirectory, worldConfiguration.Template);

        world.WorldTemplate.Metadata = await LoadMetadataAsync(templateDirectory, definitionDirectory);
        await LoadGamePlayAsync(templateDirectory, definitionDirectory, world);
        await LoadSimulationAsync(templateDirectory, definitionDirectory, world);
        await LoadPresentationAsync(templateDirectory, definitionDirectory, world);

        LoadWorldConfigurationItem(world);
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


        //// Calculate derived values from WorldConfiguration
        //WorldConfigurationService.CalculateUnitsToDegrees(world, worldConfiguration);
        //world.HeightMapLatitudeScale_Validated = world.IsProcedural ? 1 : (int)Math.Round(worldConfiguration.HeightMapSettings.MapResolutionInMeters * worldConfiguration.HeightMapSettings.HorizontalScale);

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

        //ConvertSagasToAvatarCoordinates(world);

        WorldConfigurationService.ApplyScaleAndShiftSettings(world);

        return world;
    }

    private static void LoadWorldConfigurationItem(World world)
    {
        // Handle based on actual runtime type (no enum needed)
        switch (world.WorldConfiguration.Item)
        {
            case ProceduralSettings proceduralSettings:
                world.WorldConfiguration.ProceduralSettings = proceduralSettings;
                break;

            case HeightMapSettings mapSettings:
                world.WorldConfiguration.HeightMapSettings = mapSettings;
                break;

            default:
                Debug.WriteLine("world.WorldConfiguration defaulting to procedural");
                world.WorldConfiguration.ProceduralSettings = new ProceduralSettings();
                break;
        }
    }

    //private static void ConvertSagasToAvatarCoordinates(World world)
    //{
    //    if (world.Gameplay.Sagas != null)
    //    {
    //        foreach (var saga in world.Gameplay.Sagas)
    //        {
    //            saga.LongitudeXFerRealz = (long)Math.Round(SagaManager.GetLongitudeInput(world, saga.LongitudeX));
    //            saga.LatitudeZFerRealz = (long)Math.Round(SagaManager.GetLatitudeInput(world, saga.LatitudeZ));
    //        }
    //    }
    //}

    private static async Task LoadGamePlayAsync(string dataDirectory, string definitionDirectory, World world)
    {
        await GameplayComponentLoader.LoadAsync(dataDirectory, definitionDirectory, world);
    }

    private static async Task LoadSimulationAsync(string dataDirectory, string definitionDirectory, World world)
    {
        //await SimulationComponentLoader.LoadAsync(dataDirectory, definitionDirectory, world);
    }

    private static async Task LoadPresentationAsync(string dataDirectory, string definitionDirectory, World world)
    {
        //await PresentationComponentLoader.LoadAsync(dataDirectory, definitionDirectory, world);
    }

    public static async Task<WorldConfiguration[]> LoadAvailableWorldConfigurationsAsync(string dataDirectory, string definitionDirectory)
    {
        var xsdFilePath = Path.Combine(definitionDirectory, "WorldConfiguration.xsd");
        return (await XmlLoader.LoadFromXmlAsync<WorldConfigurations>(Path.Combine(dataDirectory, "WorldConfigurations.xml"), xsdFilePath)).WorldConfiguration;
    }

    public static async Task<TemplateMetadata> LoadMetadataAsync(string dataDirectory, string definitionDirectory)
    {
        var xsdFilePath = Path.Combine(definitionDirectory, "WorldTemplateMetadata.xsd");
        return await XmlLoader.LoadFromXmlAsync<TemplateMetadata>(Path.Combine(dataDirectory, "TemplateMetadata.xml"), xsdFilePath);
    }

    private static async Task LoadHeightMapMetadata(string dataDirectory, World world)
    {
        var relativePath = Path.Combine(dataDirectory, world.WorldConfiguration.HeightMapSettings.RelativePath);

        world.HeightMapMetadata = GeoTiffReader.ReadMetadata(relativePath);
    }
}