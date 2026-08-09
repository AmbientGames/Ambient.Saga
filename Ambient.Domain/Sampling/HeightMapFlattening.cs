using Ambient.Domain.Contracts;

namespace Ambient.Domain.Sampling;

/// <summary>
/// Structure-pad flattening for height-map worlds: every arc location gets a flattened
/// terrain disc sized by its category, so buildings sit on level ground. ONE home
/// (hoisted from the Rpg.Ui HeightMapProcessor 2026-08-01 so the server could load
/// elevation maps with byte-identical terrain — client and server MUST produce the same
/// flatten set or their generated ground diverges under every structure).
/// </summary>
public static class HeightMapFlattening
{
    public static IEnumerable<FlattenLocation> GetFlattenLocations(IWorld world)
    {
        // all offsets are in blocks
        const int StructureElevationOffset = 2;
        const double StructureRadius = 30;
        const int DefaultElevationOffset = 1;
        const double DefaultRadius = 10;

        // Check if we have the necessary data
        if (world.ArcLookup == null || world.ArcLookup.Count == 0)
            yield break;

        if (world.HeightMapMetadata == null)
            yield break;

        foreach (var arc in world.ArcLookup.Values)
        {
            // Convert GPS coordinates to heightmap pixel coordinates
            var pixelX = (int)GameLogic.Gameplay.WorldManagers.CoordinateConverter.HeightMapLongitudeToPixelX(arc.Longitude, world.HeightMapMetadata);
            var pixelY = (int)GameLogic.Gameplay.WorldManagers.CoordinateConverter.HeightMapLatitudeToPixelY(arc.Latitude, world.HeightMapMetadata);

            // Determine elevation offset and radius based on feature type
            // Categories with large structures need more terrain flattening
            var elevationOffset = DefaultElevationOffset / world.WorldConfiguration.HeightMapSettings.VerticalScale;
            var radius = DefaultRadius / world.WorldConfiguration.HeightMapSettings.HorizontalScale / world.WorldConfiguration.HeightMapSettings.MapResolutionInMeters;

            var isLargeStructure = arc.Category is
                ArcCategory.Stronghold or
                ArcCategory.Facility or
                ArcCategory.Religious or
                ArcCategory.Ruin or
                ArcCategory.Service or
                ArcCategory.Camp;

            if (isLargeStructure)
            {
                elevationOffset = StructureElevationOffset / world.WorldConfiguration.HeightMapSettings.VerticalScale;
                radius = StructureRadius / world.WorldConfiguration.HeightMapSettings.HorizontalScale / world.WorldConfiguration.HeightMapSettings.MapResolutionInMeters;
            }

            // Ensure within bounds (accounting for sample radius which is radius + 1)
            var sampleRadius = radius + 1;
            if (pixelX < sampleRadius || pixelX >= world.HeightMapMetadata.ImageWidth - sampleRadius ||
                pixelY < sampleRadius || pixelY >= world.HeightMapMetadata.ImageHeight - sampleRadius)
                continue;

            yield return new FlattenLocation(pixelX, pixelY, (int)Math.Round(elevationOffset), (int)Math.Round(radius));
        }
    }
}
