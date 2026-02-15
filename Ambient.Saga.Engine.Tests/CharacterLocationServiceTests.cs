using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Domain.ValueObjects;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Unit tests for CharacterLocationService which handles character spawning and positioning logic.
/// </summary>
public class CharacterLocationServiceTests
{
    private World CreateTestWorld(bool isProcedural = true, double horizontalScale = 1.0)
    {
        var world = new World
        {
            IsProcedural = isProcedural,
            WorldConfiguration = new WorldConfiguration
            {
                HeightMapSettings = new HeightMapSettings
                {
                    HorizontalScale = horizontalScale,
                    MapResolutionInMeters = 30.91 // 1 degree / 3600 pixels * 111319.5 m/degree
                },
                ProceduralSettings = new ProceduralSettings
                {
                    LongitudeDegreesToUnits = 111320.0, // Approximate meters per degree at equator
                    LatitudeDegreesToUnits = 110540.0   // Approximate meters per degree
                }
            }
        };

        if (!isProcedural)
        {
            // Set up test heightmap metadata (1 degree = 3600 pixels)
            world.HeightMapMetadata = new GeoTiffMetadata
            {
                North = 32.0,
                South = 31.0,
                East = 131.0,
                West = 130.0,
                ImageWidth = 3600,
                ImageHeight = 3600
            };

            // Set spawn at center of map
            world.HeightMapSpawnPixelX = 1800;
            world.HeightMapSpawnPixelY = 1800;

            // Calculate scale values (matching WorldAssetLoader logic)
            var mapResolution = world.WorldConfiguration.HeightMapSettings.MapResolutionInMeters;
            world.HeightMapLatitudeScale = mapResolution * horizontalScale;

            var centerLatitude = (world.HeightMapMetadata.North + world.HeightMapMetadata.South) / 2.0;
            var latitudeCorrectionFactor = Math.Cos(centerLatitude * Math.PI / 180.0);
            world.HeightMapLongitudeScale = world.HeightMapLatitudeScale / latitudeCorrectionFactor;
        }

        return world;
    }

    #region CalculateCircularSpawnPositions Tests

    // Note: Procedural and heightmap worlds use different coordinate conversion logic,
    // so they won't produce identical results even at scale=1.0. Both are tested separately above.

    #endregion

    #region GetDistanceBetweenPositions Tests

 

    #endregion

    #region IsCharacterSpawnedNearby Tests

 

    #endregion

 
}
