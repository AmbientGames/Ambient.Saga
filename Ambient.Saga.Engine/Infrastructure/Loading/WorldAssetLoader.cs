using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure.GameLogic.Services;
using Ambient.Infrastructure.Sampling;
using Ambient.Infrastructure.Utilities;

namespace Ambient.Saga.Engine.Infrastructure.Loading;

/// <summary>
/// Loads world assets including configuration, metadata, and gameplay data.
/// </summary>
public class WorldAssetLoader : IWorldLoader
{
    private readonly IWorldFactory _worldFactory;
    private readonly IWorldConfigurationLoader _configurationLoader;

    public WorldAssetLoader(IWorldFactory worldFactory, IWorldConfigurationLoader configurationLoader)
    {
        _worldFactory = worldFactory;
        _configurationLoader = configurationLoader;
    }

    /// <inheritdoc />
    public async Task<IWorld> LoadWorldByConfigurationAsync(string dataDirectory, string definitionDirectory, string configurationRefName)
    {
        var configurations = await _configurationLoader.LoadAvailableWorldConfigurationsAsync(dataDirectory, definitionDirectory);

        var worldConfiguration = configurations.FirstOrDefault(c => c.RefName == configurationRefName);
        if (worldConfiguration == null)
        {
            throw new InvalidOperationException($"WorldConfiguration with RefName '{configurationRefName}' not found");
        }

        return await LoadWorldAsync(dataDirectory, definitionDirectory, worldConfiguration);
    }

    /// <inheritdoc />
    public async Task<IWorld> LoadWorldAsync(string dataDirectory, string definitionDirectory, WorldConfiguration worldConfiguration)
    {
        var world = _worldFactory.CreateWorld();
        world.WorldConfiguration = worldConfiguration;

        var templateDirectory = Path.Combine(dataDirectory, worldConfiguration.Template);

        world.WorldTemplate.Metadata = await LoadMetadataAsync(templateDirectory, definitionDirectory);
        await LoadGamePlayAsync(templateDirectory, definitionDirectory, world);

        world.IsProcedural = world.WorldConfiguration.ProceduralSettings != null;

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

        world.HeightMapLatitudeScale = world.IsProcedural ? 1 : (int)Math.Round(worldConfiguration.HeightMapSettings.MapResolutionInMeters * worldConfiguration.HeightMapSettings.HorizontalScale);

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

        WorldConfigurationService.CalculateSpawnAnchorPoints(world);
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
