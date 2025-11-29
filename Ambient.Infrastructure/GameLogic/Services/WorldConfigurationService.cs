using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;

namespace Ambient.Infrastructure.GameLogic.Services;

public static class WorldConfigurationService
{
    /// <summary>
    /// Calculates spawn anchor points (cell coordinates) for height map worlds.
    /// This should be called after height map metadata is loaded.
    /// </summary>
    /// <param name="world">World to calculate anchor points for</param>
    public static void CalculateSpawnAnchorPoints(World world)
    {
        // Only calculate for height map worlds with metadata loaded
        if (world.IsProcedural)
        {
            world.HeightMapSpawnPixelX = 0;
            world.HeightMapSpawnPixelY = 0;
            return;
        }

        // Calculate the cell coordinates of the spawn location
        var spawnLongitude = world.WorldConfiguration.SpawnLongitude;
        var spawnLatitude = world.WorldConfiguration.SpawnLatitude;
        
        world.HeightMapSpawnPixelX = (int)Math.Round(CoordinateConverter.HeightMapLongitudeToPixelX(spawnLongitude, world.HeightMapMetadata));
        world.HeightMapSpawnPixelY = (int)Math.Round(CoordinateConverter.HeightMapLatitudeToPixelY(spawnLatitude, world.HeightMapMetadata));
    }

    public static double GetProceduralScaleFactor(double chunkHeight)
    {
        const double exponent = 0.25; // flatter growth than before
        const double A = 1750.0;      // adjusted to hit 7000m at 256 and 14000m at 4096, but realistically much less
        var scaleFactor = A * Math.Pow(chunkHeight, exponent) / chunkHeight;
        return scaleFactor;
    }

    public static void ApplyScaleAndShiftSettings(World world)
    {
        if (world.WorldConfiguration.ProceduralSettings != null)
        {
            world.VerticalScale = 1 / GetProceduralScaleFactor(world.WorldConfiguration.ChunkHeight);
            world.VerticalShift = 0;
        }
        else
        {
            world.VerticalScale = world.WorldConfiguration.HeightMapSettings.VerticalScale;
            world.VerticalShift = world.WorldConfiguration.HeightMapSettings.VerticalShift;
        }
    }

    public static void InitializeWorldTiming(World world)
    {
        world.UtcStartTick = DateTime.UtcNow.Ticks - 10 * world.WorldConfiguration.SecondsInHour * TimeSpan.TicksPerSecond;
    }
}