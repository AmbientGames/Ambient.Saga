using Ambient.Domain.Contracts;

namespace Ambient.Domain.GameLogic.Gameplay.WorldManagers;

/// <summary>
/// Utilities for converting between geographic coordinates (latitude/longitude) and image pixel coordinates.
/// </summary>
public static class CoordinateConverter
{
    // Earth's circumference at equator in meters
    private const double EarthCircumferenceMeters = 40075016.686;
    
    // Meters per degree at equator
    private const double MetersPerDegreeAtEquator = EarthCircumferenceMeters / 360.0;

    /// <summary>
    /// Converts longitude to pixel X coordinate within a height map image.
    /// </summary>
    /// <param name="longitude">Longitude in degrees</param>
    /// <param name="metadata">Height map metadata containing image bounds</param>
    /// <returns>X pixel coordinate</returns>
    public static double HeightMapLongitudeToPixelX(double longitude, IHeightMapMetadata metadata)
    {
        // Normalize longitude to 0-1 within image bounds
        var normalized = (longitude - metadata.West) / (metadata.East - metadata.West);
        return normalized * metadata.ImageWidth;
    }

    /// <summary>
    /// Converts latitude to pixel Y coordinate within a height map image.
    /// Note: North is at the top of the image (Y=0), so coordinates are inverted.
    /// </summary>
    /// <param name="latitude">Latitude in degrees</param>
    /// <param name="metadata">Height map metadata containing image bounds</param>
    /// <returns>Y pixel coordinate</returns>
    public static double HeightMapLatitudeToPixelY(double latitude, IHeightMapMetadata metadata)
    {
        // Normalize latitude to 0-1 within image bounds (note: North is top, so invert)
        var normalized = (metadata.North - latitude) / (metadata.North - metadata.South);
        return normalized * (metadata.ImageHeight - 1);
    }

    /// <summary>
    /// Converts pixel X coordinate to longitude.
    /// </summary>
    /// <param name="pixelX">X pixel coordinate</param>
    /// <param name="metadata">Height map metadata containing image bounds</param>
    /// <returns>Longitude in degrees</returns>
    public static double HeightMapPixelXToLongitude(double pixelX, IHeightMapMetadata metadata)
    {
        var normalized = pixelX / metadata.ImageWidth;
        return metadata.West + normalized * (metadata.East - metadata.West);
    }

    /// <summary>
    /// Converts pixel Y coordinate to latitude.
    /// </summary>
    /// <param name="pixelY">Y pixel coordinate</param>
    /// <param name="metadata">Height map metadata containing image bounds</param>
    /// <returns>Latitude in degrees</returns>
    public static double HeightMapPixelYToLatitude(double pixelY, IHeightMapMetadata metadata)
    {
        var normalized = pixelY / (metadata.ImageHeight - 1);
        return metadata.North - normalized * (metadata.North - metadata.South);
    }

    /// <summary>
    /// Converts meters to pixels for circular radius rendering.
    /// Compromise approximation: slightly too big in one direction, slightly too small in the other.
    /// Vertical scale is correct for Y axis; horizontal adjusted to match Y's magnitude.
    /// </summary>
    /// <param name="meters">Distance in meters</param>
    /// <param name="metadata">Height map metadata containing image bounds</param>
    /// <returns>Distance in pixels</returns>
    public static double HeightMapMetersToPixelsApproximate(double meters, IHeightMapMetadata metadata)
    {
        // Calculate approximate meters per pixel at the center latitude
        var centerLatitude = (metadata.North + metadata.South) / 2.0;
        var latitudeCorrectionFactor = Math.Cos(centerLatitude * Math.PI / 180.0);

        // Degrees per pixel
        var degreesPerPixelX = metadata.Width / metadata.ImageWidth;
        var degreesPerPixelY = metadata.Height / metadata.ImageHeight;

        // Meters per pixel (accounting for latitude)
        var metersPerPixelX = degreesPerPixelX * MetersPerDegreeAtEquator * latitudeCorrectionFactor;
        var metersPerPixelY = degreesPerPixelY * MetersPerDegreeAtEquator;

        // Average horizontal (X adjusted to Y magnitude) and vertical scales for circle radius
        var adjustedHorizontal = metersPerPixelY * (metersPerPixelY / metersPerPixelX);
        var vertical = metersPerPixelY;
        var averageMetersPerPixel = (adjustedHorizontal + vertical) / 2.0;

        return meters / averageMetersPerPixel;
    }

    /// <summary>
    /// Converts pixels to meters for distance calculations.
    /// </summary>
    /// <param name="pixels">Distance in pixels</param>
    /// <param name="metadata">Height map metadata containing image bounds</param>
    /// <returns>Distance in meters</returns>
    public static double HeightMapPixelsToMetersApproximate(double pixels, IHeightMapMetadata metadata)
    {
        var centerLatitude = (metadata.North + metadata.South) / 2.0;
        var latitudeCorrectionFactor = Math.Cos(centerLatitude * Math.PI / 180.0);

        var degreesPerPixelX = metadata.Width / metadata.ImageWidth;
        var degreesPerPixelY = metadata.Height / metadata.ImageHeight;

        var metersPerPixelX = degreesPerPixelX * MetersPerDegreeAtEquator * latitudeCorrectionFactor;
        var metersPerPixelY = degreesPerPixelY * MetersPerDegreeAtEquator;

        var averageMetersPerPixel = (metersPerPixelX + metersPerPixelY) / 2.0;

        return pixels * averageMetersPerPixel;
    }

    // ============================================================================
    // Model Coordinate Conversions (All World Types)
    // ============================================================================

    /// <summary>
    /// Converts model X coordinate to longitude.
    /// Model space has spawn at origin (0,0,0).
    /// Works for both procedural and height map worlds.
    /// </summary>
    public static double ModelXToLongitude(double modelX, IWorld world)
    {
        if (world.IsProcedural)
        {
            // Procedural: simple degrees to units conversion
            return modelX / world.WorldConfiguration.ProceduralSettings.LongitudeDegreesToUnits;
        }
        else
        {
            // Height map: convert through pixel coordinates
            var pixelOffsetX = modelX / world.HeightMapLongitudeScale;
            var absolutePixelX = world.HeightMapSpawnPixelX + pixelOffsetX;
            return HeightMapPixelXToLongitude(absolutePixelX, world.HeightMapMetadata);
        }
    }

    /// <summary>
    /// Converts longitude to model X coordinate.
    /// Model space has spawn at origin (0,0,0).
    /// Works for both procedural and height map worlds.
    /// </summary>
    public static double LongitudeToModelX(double longitude, IWorld world)
    {
        if (world.IsProcedural)
        {
            // Procedural: simple degrees to units conversion
            return longitude * world.WorldConfiguration.ProceduralSettings.LongitudeDegreesToUnits;
        }
        else
        {
            // Height map: convert through pixel coordinates
            var pixelX = HeightMapLongitudeToPixelX(longitude, world.HeightMapMetadata);
            var pixelOffsetX = pixelX - world.HeightMapSpawnPixelX;
            return pixelOffsetX * world.HeightMapLongitudeScale;
        }
    }

    /// <summary>
    /// Converts model Z coordinate to latitude.
    /// Model space has spawn at origin (0,0,0). Positive Z goes north.
    /// Works for both procedural and height map worlds.
    /// </summary>
    public static double ModelZToLatitude(double modelZ, IWorld world)
    {
        if (world.IsProcedural)
        {
            // Procedural: simple degrees to units conversion
            return modelZ / world.WorldConfiguration.ProceduralSettings.LatitudeDegreesToUnits;
        }
        else
        {
            // Height map: convert through pixel coordinates
            var pixelOffsetZ = modelZ / world.HeightMapLatitudeScale;
            var absolutePixelY = world.HeightMapSpawnPixelY - pixelOffsetZ;
            return HeightMapPixelYToLatitude(absolutePixelY, world.HeightMapMetadata);
        }
    }

    /// <summary>
    /// Converts latitude to model Z coordinate.
    /// Model space has spawn at origin (0,0,0). Positive Z goes north.
    /// Works for both procedural and height map worlds.
    /// </summary>
    public static double LatitudeToModelZ(double latitude, IWorld world)
    {
        if (world.IsProcedural)
        {
            // Procedural: simple degrees to units conversion
            return latitude * world.WorldConfiguration.ProceduralSettings.LatitudeDegreesToUnits;
        }
        else
        {
            // Height map: convert through pixel coordinates
            var pixelY = HeightMapLatitudeToPixelY(latitude, world.HeightMapMetadata);
            var pixelOffsetZ = world.HeightMapSpawnPixelY - pixelY;
            return pixelOffsetZ * world.HeightMapLatitudeScale;
        }
    }

    public static double GetAltitude(IWorld world, int altitudeInt)
    {
        return GetAltitude(world.BlocksBeneathSeaLevel, world.VerticalScale, world.VerticalShift, altitudeInt);
    }

    public static double GetAltitude(int blocksBeneathSeaLevel_Wip, double worldScale, double verticalShift, int altitudeInt)
    {
        var blocksHeight = (double)altitudeInt - blocksBeneathSeaLevel_Wip;
        var realWorldLessOffset = blocksHeight / worldScale;
        var altitude = realWorldLessOffset - verticalShift;
        return altitude;
    }

    // ============================================================================
    // Saga-Relative Coordinate Conversions
    // ============================================================================

    /// <summary>
    /// Converts Saga-relative X coordinate (meters from Saga center) to world longitude.
    /// Uses proper model coordinate conversion for accuracy across all world types.
    /// </summary>
    public static double SagaRelativeXToLongitude(double sagaRelativeX, double sagaCenterLongitude, IWorld world)
    {
        // Convert Saga center to model coordinates
        var sagaCenterModelX = LongitudeToModelX(sagaCenterLongitude, world);

        // Add Saga-relative offset (already in meters) to get character position in model space
        var characterModelX = sagaCenterModelX + sagaRelativeX;

        // Convert back to world longitude
        return ModelXToLongitude(characterModelX, world);
    }

    /// <summary>
    /// Converts Saga-relative Z coordinate (meters from Saga center) to world latitude.
    /// Uses proper model coordinate conversion for accuracy across all world types.
    /// </summary>
    public static double SagaRelativeZToLatitude(double sagaRelativeZ, double sagaCenterLatitude, IWorld world)
    {
        // Convert Saga center to model coordinates
        var sagaCenterModelZ = LatitudeToModelZ(sagaCenterLatitude, world);

        // Add Saga-relative offset (already in meters) to get character position in model space
        var characterModelZ = sagaCenterModelZ + sagaRelativeZ;

        // Convert back to world latitude
        return ModelZToLatitude(characterModelZ, world);
    }

    /// <summary>
    /// Converts world longitude to Saga-relative X coordinate (meters from Saga center).
    /// Uses proper model coordinate conversion for accuracy across all world types.
    /// </summary>
    public static double LongitudeToSagaRelativeX(double longitude, double sagaCenterLongitude, IWorld world)
    {
        // Convert both to model coordinates (meters)
        var pointModelX = LongitudeToModelX(longitude, world);
        var sagaCenterModelX = LongitudeToModelX(sagaCenterLongitude, world);

        // Distance between them is the Saga-relative offset
        return pointModelX - sagaCenterModelX;
    }

    /// <summary>
    /// Converts world latitude to Saga-relative Z coordinate (meters from Saga center).
    /// Uses proper model coordinate conversion for accuracy across all world types.
    /// </summary>
    public static double LatitudeToSagaRelativeZ(double latitude, double sagaCenterLatitude, IWorld world)
    {
        // Convert both to model coordinates (meters)
        var pointModelZ = LatitudeToModelZ(latitude, world);
        var sagaCenterModelZ = LatitudeToModelZ(sagaCenterLatitude, world);

        // Distance between them is the Saga-relative offset
        return pointModelZ - sagaCenterModelZ;
    }

}