using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Infrastructure.GameLogic.Services;
using Ambient.Infrastructure.Sampling;
using Microsoft.Extensions.Logging;

namespace Ambient.Infrastructure.GameLogic.Loading;

/// <summary>
/// Loads world assets including configuration, metadata, and gameplay data.
/// </summary>
public class WorldAssetLoader : IWorldLoader
{
    private readonly IWorldFactory _worldFactory;
    private readonly IWorldConfigurationLoader _configurationLoader;
    private readonly IGameplayComponentLoader _gameplayLoader;
    private readonly IContentPathResolver _contentPathResolver;
    private readonly ILogger<WorldAssetLoader>? _logger;

    public WorldAssetLoader(
        IWorldFactory worldFactory,
        IWorldConfigurationLoader configurationLoader,
        IGameplayComponentLoader gameplayLoader,
        IContentPathResolver contentPathResolver,
        ILogger<WorldAssetLoader>? logger = null)
    {
        _worldFactory = worldFactory ?? throw new ArgumentNullException(nameof(worldFactory));
        _configurationLoader = configurationLoader ?? throw new ArgumentNullException(nameof(configurationLoader));
        _gameplayLoader = gameplayLoader ?? throw new ArgumentNullException(nameof(gameplayLoader));
        _contentPathResolver = contentPathResolver ?? throw new ArgumentNullException(nameof(contentPathResolver));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IWorld> LoadWorldByConfigurationAsync(string dataDirectory, string? definitionDirectory, string configurationRefName)
    {
        _logger?.LogDebug("Loading world by configuration: {RefName}", configurationRefName);

        var configurations = await _configurationLoader.LoadAvailableWorldConfigurationsAsync(dataDirectory, definitionDirectory);

        var worldConfiguration = configurations.FirstOrDefault(c => c.RefName == configurationRefName);
        if (worldConfiguration == null)
        {
            throw new InvalidOperationException($"WorldConfiguration with RefName '{configurationRefName}' not found");
        }

        return await LoadWorldAsync(dataDirectory, definitionDirectory, worldConfiguration);
    }

    /// <inheritdoc />
    public async Task<IWorld> LoadWorldAsync(string dataDirectory, string? definitionDirectory, IWorldConfiguration worldConfiguration)
    {
        _logger?.LogInformation("Loading world: {WorldName}", worldConfiguration.RefName);

        var world = _worldFactory.CreateWorld();
        world.WorldConfiguration = worldConfiguration;

        await _gameplayLoader.LoadAsync(definitionDirectory, world);

        world.IsProcedural = world.WorldConfiguration.ProceduralSettings != null;

        if (!world.IsProcedural)
        {
            await LoadHeightMapMetadata(world);
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
        WorldValidationService.ValidateReferentialIntegrity(world, _logger);

        _logger?.LogInformation("World loaded successfully: {WorldName}", worldConfiguration.RefName);

        return world;
    }

    private async Task LoadHeightMapMetadata(IWorld world)
    {
        var config = world.WorldConfiguration;
        var resolvedPath = _contentPathResolver.ResolveGeographicDataPath(config.ContentPackLibrary, config.Namespace, config.HeightMapSettings.FileName);

        if (resolvedPath == null)
        {
            throw new FileNotFoundException($"Geographic data not found: {config.HeightMapSettings.FileName}");
        }

        _logger?.LogDebug("Loading height map from: {Path}", resolvedPath);
        world.HeightMapMetadata = GeoTiffReader.ReadMetadata(resolvedPath);
    }
}
