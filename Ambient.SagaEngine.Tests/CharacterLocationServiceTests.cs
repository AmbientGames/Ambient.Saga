using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.ValueObjects;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Tests;

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

    [Fact]
    public void CalculateCircularSpawnPositions_WithZeroCount_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 10.0;

        // Act
        var positions = CharacterLocationService.CalculateCircularSpawnPositions(
            centerLat, centerLon, radius, 0, world);

        // Assert
        Assert.Empty(positions);
    }

    [Fact]
    public void CalculateCircularSpawnPositions_WithNegativeCount_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 10.0;

        // Act
        var positions = CharacterLocationService.CalculateCircularSpawnPositions(
            centerLat, centerLon, radius, -5, world);

        // Assert
        Assert.Empty(positions);
    }

    [Fact]
    public void CalculateCircularSpawnPositions_WithOnePosition_ReturnsOnePosition()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 10.0;

        // Act
        var positions = CharacterLocationService.CalculateCircularSpawnPositions(
            centerLat, centerLon, radius, 1, world);

        // Assert
        Assert.Single(positions);
    }

    [Fact]
    public void CalculateCircularSpawnPositions_WithFourPositions_ReturnsFourPositions()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 10.0;

        // Act
        var positions = CharacterLocationService.CalculateCircularSpawnPositions(
            centerLat, centerLon, radius, 4, world);

        // Assert
        Assert.Equal(4, positions.Count);
    }

    [Fact]
    public void CalculateCircularSpawnPositions_AllPositionsAreApproximatelyAtRadius()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 50.0; // 50 meters
        var count = 8;

        // Act
        var positions = CharacterLocationService.CalculateCircularSpawnPositions(
            centerLat, centerLon, radius, count, world);

        // Assert
        foreach (var (lat, lon) in positions)
        {
            var distance = CharacterLocationService.GetDistanceBetweenPositions(
                centerLat, centerLon, lat, lon, world);

            // Allow 1% tolerance for floating point precision
            Assert.InRange(distance, radius * 0.99, radius * 1.01);
        }
    }

    [Fact]
    public void CalculateCircularSpawnPositions_PositionsAreEvenlyDistributed()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 30.0;
        var count = 4; // Should form a square pattern

        // Act
        var positions = CharacterLocationService.CalculateCircularSpawnPositions(
            centerLat, centerLon, radius, count, world);

        // Assert - Check that adjacent positions have consistent spacing
        var distances = new List<double>();
        for (var i = 0; i < positions.Count; i++)
        {
            var next = (i + 1) % positions.Count;
            var distance = CharacterLocationService.GetDistanceBetweenPositions(
                positions[i].latitude, positions[i].longitude,
                positions[next].latitude, positions[next].longitude,
                world);
            distances.Add(distance);
        }

        // All adjacent distances should be approximately equal
        var avgDistance = distances.Average();
        foreach (var distance in distances)
        {
            Assert.InRange(distance, avgDistance * 0.99, avgDistance * 1.01);
        }
    }

    [Fact]
    public void CalculateCircularSpawnPositions_WithHorizontalScale_AdjustsRadius()
    {
        // Arrange
        var scale = 2.0;
        var world = CreateTestWorld(isProcedural: false, horizontalScale: scale);
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 25.0;

        // Act
        var positions = CharacterLocationService.CalculateCircularSpawnPositions(
            centerLat, centerLon, radius, 4, world);

        // Assert - Positions should be scaled correctly
        foreach (var (lat, lon) in positions)
        {
            var distance = CharacterLocationService.GetDistanceBetweenPositions(
                centerLat, centerLon, lat, lon, world);

            // Distance should be the requested radius (scale is applied internally)
            Assert.InRange(distance, radius * 0.99, radius * 1.01);
        }
    }

    // Note: Procedural and heightmap worlds use different coordinate conversion logic,
    // so they won't produce identical results even at scale=1.0. Both are tested separately above.

    #endregion

    #region GetDistanceBetweenPositions Tests

    [Fact]
    public void GetDistanceBetweenPositions_SamePosition_ReturnsZero()
    {
        // Arrange
        var world = CreateTestWorld();
        var lat = 31.5955;
        var lon = 130.5569;

        // Act
        var distance = CharacterLocationService.GetDistanceBetweenPositions(
            lat, lon, lat, lon, world);

        // Assert
        Assert.Equal(0.0, distance, precision: 10);
    }

    [Fact]
    public void GetDistanceBetweenPositions_DifferentPositions_ReturnsPositiveDistance()
    {
        // Arrange
        var world = CreateTestWorld();
        var lat1 = 31.5955;
        var lon1 = 130.5569;
        var lat2 = 31.5965;
        var lon2 = 130.5579;

        // Act
        var distance = CharacterLocationService.GetDistanceBetweenPositions(
            lat1, lon1, lat2, lon2, world);

        // Assert
        Assert.True(distance > 0);
    }

    [Fact]
    public void GetDistanceBetweenPositions_IsSymmetric()
    {
        // Arrange
        var world = CreateTestWorld();
        var lat1 = 31.5955;
        var lon1 = 130.5569;
        var lat2 = 31.5965;
        var lon2 = 130.5579;

        // Act
        var distance1to2 = CharacterLocationService.GetDistanceBetweenPositions(
            lat1, lon1, lat2, lon2, world);
        var distance2to1 = CharacterLocationService.GetDistanceBetweenPositions(
            lat2, lon2, lat1, lon1, world);

        // Assert
        Assert.Equal(distance1to2, distance2to1, precision: 10);
    }

    [Fact]
    public void GetDistanceBetweenPositions_WithHorizontalScale_ReturnsUnscaledMeters()
    {
        // Arrange
        var scale = 3.0;
        var world = CreateTestWorld(isProcedural: false, horizontalScale: scale);
        var lat1 = 31.5955;
        var lon1 = 130.5569;
        var lat2 = 31.5965;
        var lon2 = 130.5579;

        // Act
        var distance = CharacterLocationService.GetDistanceBetweenPositions(
            lat1, lon1, lat2, lon2, world);

        // Assert - Should return real-world meters (unscaled)
        Assert.True(distance > 0);
        // Distance should be reasonable for ~0.001 degree offset (roughly 100-150 meters)
        Assert.InRange(distance, 50, 200);
    }

    #endregion

    #region IsCharacterSpawnedNearby Tests

    [Fact]
    public void IsCharacterSpawnedNearby_WithNoSpawnedCharacters_ReturnsFalse()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 50.0;
        var characterRef = "TestCharacter";
        var spawnedCharacters = new List<CharacterState>();

        // Act
        var result = CharacterLocationService.IsCharacterSpawnedNearby(
            characterRef, centerLat, centerLon, radius, world, spawnedCharacters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsCharacterSpawnedNearby_WithDifferentCharacterRef_ReturnsFalse()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 50.0;
        var characterRef = "TestCharacter";
        var spawnedCharacters = new List<CharacterState>
        {
            new CharacterState
            {
                CharacterRef = "DifferentCharacter",
                CurrentLatitudeZ = centerLat,
                CurrentLongitudeX = centerLon
            }
        };

        // Act
        var result = CharacterLocationService.IsCharacterSpawnedNearby(
            characterRef, centerLat, centerLon, radius, world, spawnedCharacters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsCharacterSpawnedNearby_WithMatchingCharacterWithinDoubleRadius_ReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 50.0;
        var characterRef = "TestCharacter";

        // Spawn character at exactly radius distance
        var spawnedPos = CharacterLocationService.CalculateCircularSpawnPositions(
            centerLat, centerLon, radius, 1, world)[0];

        var spawnedCharacters = new List<CharacterState>
        {
            new CharacterState
            {
                CharacterRef = characterRef,
                CurrentLatitudeZ = spawnedPos.latitude,
                CurrentLongitudeX = spawnedPos.longitude
            }
        };

        // Act - Within 2x radius = considered "nearby"
        var result = CharacterLocationService.IsCharacterSpawnedNearby(
            characterRef, centerLat, centerLon, radius, world, spawnedCharacters);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsCharacterSpawnedNearby_WithMatchingCharacterBeyondDoubleRadius_ReturnsFalse()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 50.0;
        var characterRef = "TestCharacter";

        // Spawn character at 2.5x radius distance (beyond 2x threshold)
        var spawnedPos = CharacterLocationService.CalculateCircularSpawnPositions(
            centerLat, centerLon, radius * 2.5, 1, world)[0];

        var spawnedCharacters = new List<CharacterState>
        {
            new CharacterState
            {
                CharacterRef = characterRef,
                CurrentLatitudeZ = spawnedPos.latitude,
                CurrentLongitudeX = spawnedPos.longitude
            }
        };

        // Act
        var result = CharacterLocationService.IsCharacterSpawnedNearby(
            characterRef, centerLat, centerLon, radius, world, spawnedCharacters);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsCharacterSpawnedNearby_WithMultipleCharacters_ChecksAllOfThem()
    {
        // Arrange
        var world = CreateTestWorld();
        var centerLat = 31.5955;
        var centerLon = 130.5569;
        var radius = 50.0;
        var characterRef = "TestCharacter";

        var spawnedCharacters = new List<CharacterState>
        {
            new CharacterState
            {
                CharacterRef = "OtherCharacter",
                CurrentLatitudeZ = centerLat,
                CurrentLongitudeX = centerLon
            },
            new CharacterState
            {
                CharacterRef = characterRef,
                CurrentLatitudeZ = centerLat + 0.0001, // Very close
                CurrentLongitudeX = centerLon + 0.0001
            }
        };

        // Act
        var result = CharacterLocationService.IsCharacterSpawnedNearby(
            characterRef, centerLat, centerLon, radius, world, spawnedCharacters);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region GetDistanceToCharacter Tests

    [Fact]
    public void GetDistanceToCharacter_WithCharacterAtSamePosition_ReturnsZero()
    {
        // Arrange
        var world = CreateTestWorld();
        var lat = 31.5955;
        var lon = 130.5569;
        var character = new CharacterState
        {
            CurrentLatitudeZ = lat,
            CurrentLongitudeX = lon
        };

        // Act
        var distance = CharacterLocationService.GetDistanceToCharacter(
            character, lat, lon, world);

        // Assert
        Assert.Equal(0.0, distance, precision: 10);
    }

    [Fact]
    public void GetDistanceToCharacter_WithCharacterAtDifferentPosition_ReturnsDistance()
    {
        // Arrange
        var world = CreateTestWorld();
        var targetLat = 31.5955;
        var targetLon = 130.5569;
        var character = new CharacterState
        {
            CurrentLatitudeZ = 31.5965,
            CurrentLongitudeX = 130.5579
        };

        // Act
        var distance = CharacterLocationService.GetDistanceToCharacter(
            character, targetLat, targetLon, world);

        // Assert
        Assert.True(distance > 0);
    }

    #endregion

    #region IsPointNearCharacterInModelSpace Tests

    [Fact]
    public void IsPointNearCharacterInModelSpace_PointAtCharacterPosition_ReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var characterX = 100.0;
        var characterZ = 200.0;
        var pointX = 100.0;
        var pointZ = 200.0;
        var radius = 10.0;

        // Act
        var result = CharacterLocationService.IsPointNearCharacterInModelSpace(
            characterX, characterZ, pointX, pointZ, radius, world);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsPointNearCharacterInModelSpace_PointWithinRadius_ReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var characterX = 100.0;
        var characterZ = 200.0;
        var pointX = 105.0;
        var pointZ = 200.0; // 5 units away
        var radius = 10.0;

        // Act
        var result = CharacterLocationService.IsPointNearCharacterInModelSpace(
            characterX, characterZ, pointX, pointZ, radius, world);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsPointNearCharacterInModelSpace_PointBeyondRadius_ReturnsFalse()
    {
        // Arrange
        var world = CreateTestWorld();
        var characterX = 100.0;
        var characterZ = 200.0;
        var pointX = 115.0;
        var pointZ = 200.0; // 15 units away
        var radius = 10.0;

        // Act
        var result = CharacterLocationService.IsPointNearCharacterInModelSpace(
            characterX, characterZ, pointX, pointZ, radius, world);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsPointNearCharacterInModelSpace_WithHorizontalScale_ScalesRadius()
    {
        // Arrange
        var scale = 2.0;
        var world = CreateTestWorld(isProcedural: false, horizontalScale: scale);
        var characterX = 100.0;
        var characterZ = 200.0;
        var pointX = 115.0;
        var pointZ = 200.0; // 15 model units away
        var radiusMeters = 10.0; // 10 real-world meters = 20 model units with scale 2.0

        // Act
        var result = CharacterLocationService.IsPointNearCharacterInModelSpace(
            characterX, characterZ, pointX, pointZ, radiusMeters, world);

        // Assert
        // 15 model units < 20 model units (10m * 2.0 scale), so should be within
        Assert.True(result);
    }

    [Fact]
    public void IsPointNearCharacterInModelSpace_OnRadiusEdge_ReturnsTrue()
    {
        // Arrange
        var world = CreateTestWorld();
        var characterX = 0.0;
        var characterZ = 0.0;
        var pointX = 10.0;
        var pointZ = 0.0; // Exactly 10 units away
        var radius = 10.0;

        // Act
        var result = CharacterLocationService.IsPointNearCharacterInModelSpace(
            characterX, characterZ, pointX, pointZ, radius, world);

        // Assert
        Assert.True(result); // Should include points exactly on the edge
    }

    #endregion
}
