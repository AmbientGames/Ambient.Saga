using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Ambient.Domain.Sampling;
using Ambient.Domain.Contracts;

namespace Ambient.Saga.UI.Services;

public class HeightMapProcessor
{
    public class ProcessedHeightMap
    {
        public required bool[,] WaterMask { get; init; }
        public required ushort[,] ElevationData { get; init; }
        public required ushort SeaLevel { get; init; }
        public required ushort MinElevation { get; init; }
        public required ushort MaxElevation { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public ElevationWaterMap ElevationWaterMap { get; internal set; }
    }
    /// <summary>
    /// Preprocesses height map to detect water areas using the shared ElevationWaterMap logic.
    /// </summary>
    /// <param name="image">Height map image</param>
    /// <param name="minWaterAreaSize">Minimum size of area to be considered water</param>
    /// <returns>Processed height map with water detection</returns>
    public static ProcessedHeightMap ProcessHeightMap(Image<L16> image, int minWaterAreaSize, bool adjustMinWaterAreaSizeByElevation, IEnumerable<FlattenLocation>? flattenLocations, int verticalShift)
    {
        // Use the shared ElevationWaterMap logic
        var elevationWaterMap = ElevationWaterMap.FromHeightMap(image, minWaterAreaSize, adjustMinWaterAreaSizeByElevation, flattenLocations, verticalShift);

        // Convert back to the legacy format for compatibility
        var width = elevationWaterMap.Width;
        var height = elevationWaterMap.Height;
        var waterMask = new bool[width, height];
        var elevationData = new ushort[width, height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (elevation, isWater) = elevationWaterMap.GetData(x, y);
                elevationData[x, y] = elevation;
                waterMask[x, y] = isWater;
            }
        }

        return new ProcessedHeightMap
        {
            ElevationWaterMap = elevationWaterMap,
            WaterMask = waterMask,
            ElevationData = elevationData,
            SeaLevel = elevationWaterMap.SeaLevel,
            MinElevation = elevationWaterMap.MinElevation,
            MaxElevation = elevationWaterMap.MaxElevation,
            Width = width,
            Height = height
        };
    }

    public static IEnumerable<FlattenLocation> GetFlattenLocations(IWorld world)
    {
        const int StructureElevationOffset = 4;
        const int StructureRadius = 5;
        const int DefaultElevationOffset = 2;
        const int DefaultRadius = 3;

        // Check if we have the necessary data
        if (world.SagaArcLookup == null || world.SagaArcLookup.Count == 0)
            yield break;

        if (world.HeightMapMetadata == null)
            yield break;

        foreach (var sagaArc in world.SagaArcLookup.Values)
        {
            // Convert GPS coordinates to heightmap pixel coordinates
            var pixelX = (int)Ambient.Domain.GameLogic.Gameplay.WorldManagers.CoordinateConverter.HeightMapLongitudeToPixelX(sagaArc.LongitudeX, world.HeightMapMetadata);
            var pixelY = (int)Ambient.Domain.GameLogic.Gameplay.WorldManagers.CoordinateConverter.HeightMapLatitudeToPixelY(sagaArc.LatitudeZ, world.HeightMapMetadata);

            // Determine elevation offset and radius based on feature type
            // Categories with large structures need more terrain flattening
            var elevationOffset = DefaultElevationOffset / world.VerticalScale;
            var radius = DefaultRadius;

            var isLargeStructure = sagaArc.Category is
                Domain.SagaArcCategory.Stronghold or
                Domain.SagaArcCategory.Facility or
                Domain.SagaArcCategory.Religious or
                Domain.SagaArcCategory.Ruin or
                Domain.SagaArcCategory.Service or
                Domain.SagaArcCategory.Camp;

            if (isLargeStructure)
            {
                elevationOffset = StructureElevationOffset / world.VerticalScale;
                radius = StructureRadius;
            }

            // Ensure within bounds (accounting for sample radius which is radius + 1)
            var sampleRadius = radius + 1;
            if (pixelX < sampleRadius || pixelX >= world.HeightMapMetadata.ImageWidth - sampleRadius ||
                pixelY < sampleRadius || pixelY >= world.HeightMapMetadata.ImageHeight - sampleRadius)
                continue;

            yield return new FlattenLocation(pixelX, pixelY, (int)Math.Round(elevationOffset), radius);
        }
    }

    /// <summary>
    /// Gets color for elevation considering water detection
    /// </summary>
    public static (byte R, byte G, byte B) GetElevationColorWithWater(int x, int y, ProcessedHeightMap processedMap)
    {
        // Water areas - blue tones
        if (processedMap.WaterMask[x, y])
        {
            var elevation = processedMap.ElevationData[x, y];
            var depthFromSeaLevel = Math.Max(0, processedMap.SeaLevel - elevation);
            
            // Deeper water = darker blue
            if (depthFromSeaLevel >= 1)
            {
                return (20, 110, 210); // Deep water - slightly darker blue
            }
            else
            {
                return (30, 144, 255); // Shallow water - light blue
            }
        }

        // Land areas - use existing elevation coloring
        var landElevation = processedMap.ElevationData[x, y];
        if (processedMap.MaxElevation == processedMap.MinElevation)
            return (128, 128, 128); // Gray for flat areas

        var normalized = (double)(landElevation - processedMap.SeaLevel) / (processedMap.MaxElevation - processedMap.SeaLevel);
        normalized = Math.Max(0, Math.Min(1, normalized)); // Clamp to 0-1

        // Land elevation coloring
        if (normalized < 0.3)
        {
            // Low elevation - sandy/beach colors
            var intensity = (byte)(180 + normalized / 0.3 * 75);
            return (intensity, (byte)(intensity * 0.8), (byte)(intensity * 0.6)); // Sandy brown
        }
        else if (normalized < 0.7)
        {
            // Medium elevation - green
            var greenFactor = (normalized - 0.3) / 0.4;
            var greenIntensity = (byte)(60 + greenFactor * 140);
            return (0, greenIntensity, 0);
        }
        else
        {
            // High elevation - rocky/snowy
            var peakFactor = (normalized - 0.7) / 0.3;
            var intensity = (byte)(150 + peakFactor * 105);
            return (intensity, intensity, intensity); // Gray to white for peaks
        }
    }
}