using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Saga.Engine.Domain.Services;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Unit tests for SagaProximityService which handles saga interaction queries.
/// </summary>
public class SagaProximityServiceTests
{
    [Fact]
    public async Task QueryAllInteractionsAtPosition_FindsProximityTrigger()
    {
        // Arrange
        var world = CreateTestWorld();
        var saga = CreateTestSaga("TestSaga", 31.5, 130.5);
        var sagaTrigger = CreateTestSagaTrigger("TestTrigger", enterRadius: 10f);

        world.WorldTemplate.Gameplay.SagaArcs = new[] { saga };
        world.SagaTriggersLookup[saga.RefName] = new List<SagaTrigger> { sagaTrigger };

        // Avatar at saga center (should be within 10m trigger)
        var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.LongitudeX, world);
        var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.LatitudeZ, world);

        // Act
        var results = await SagaProximityService.QueryAllInteractionsAtPositionAsync(
            sagaModelX, sagaModelZ, null, world);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Type == SagaInteractionType.SagaTrigger);
        Assert.Contains(results, r => r.EntityRef == sagaTrigger.RefName);
    }

    //[Fact]
    //public void QueryAllInteractionsAtPosition_FindsFeature_Within5Meters()
    //{
    //    // Arrange
    //    var world = CreateTestWorld();
    //    var saga = CreateTestSagaWithFeature("TestSaga", 31.5, 130.5, ItemChoiceType2.LandmarkRef, "TestLandmark");

    //    world.WorldTemplate.Gameplay.SagaArcs = new[] { saga };
    //    world.SagaTriggersLookup[saga.RefName] = new List<SagaTrigger>(); // No triggers
    //    world.LandmarksLookup["TestLandmark"] = new Landmark
    //    {
    //        RefName = "TestLandmark",
    //        DisplayName = "Test Landmark"
    //    };

    //    // Avatar at saga center (should be within 5m feature radius)
    //    var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.LongitudeX, world);
    //    var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.LatitudeZ, world);

    //    // Act
    //    var results = SagaProximityService.QueryAllInteractionsAtPosition(
    //        sagaModelX, sagaModelZ, null, world);

    //    // Assert
    //    Assert.NotEmpty(results);
    //    Assert.Contains(results, r => r.Type == SagaInteractionType.Feature);
    //    Assert.Contains(results, r => r.EntityRef == "TestLandmark");
    //}

    //[Fact]
    //public void QueryAllInteractionsAtPosition_PrioritizesFeatureOverTrigger()
    //{
    //    // Arrange
    //    var world = CreateTestWorld();
    //    var saga = CreateTestSagaWithFeature("TestSaga", 31.5, 130.5, ItemChoiceType2.StructureRef, "TestStructure");
    //    var sagaTrigger = CreateTestSagaTrigger("TestTrigger", enterRadius: 10f);

    //    world.WorldTemplate.Gameplay.SagaArcs = new[] { saga };
    //    world.SagaTriggersLookup[saga.RefName] = new List<SagaTrigger> { sagaTrigger };
    //    world.StructuresLookup["TestStructure"] = new Structure
    //    {
    //        RefName = "TestStructure",
    //        DisplayName = "Test Structure"
    //    };

    //    // Avatar at saga center (within both feature and trigger)
    //    var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.LongitudeX, world);
    //    var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.LatitudeZ, world);

    //    // Act
    //    var results = SagaProximityService.QueryAllInteractionsAtPosition(
    //        sagaModelX, sagaModelZ, null, world);

    //    // Assert
    //    Assert.True(results.Count >= 2, "Should find both feature and trigger");
    //    Assert.Equal(SagaInteractionType.Feature, results[0].Type); // Feature should be first (priority 2)
    //    Assert.Equal(SagaInteractionType.SagaTrigger, results[1].Type); // Trigger should be second (priority 3)
    //}

    [Fact]
    public async Task QueryAllInteractionsAtPosition_LockedTrigger_WhenMissingQuestToken()
    {
        // Arrange
        var world = CreateTestWorld();
        var saga = CreateTestSaga("TestSaga", 31.5, 130.5);
        var trigger = CreateTestSagaTrigger("TestTrigger", enterRadius: 10f, requiresQuestToken: "KEY_ITEM");

        world.WorldTemplate.Gameplay.SagaArcs = new[] { saga };
        world.SagaTriggersLookup[saga.RefName] = new List<SagaTrigger> { trigger };

        var avatar = new AvatarBase
        {
            AvatarId = Guid.NewGuid(),
            Capabilities = new ItemCollection
            {
                QuestTokens = Array.Empty<QuestTokenEntry>() // No quest tokens
            }
        };

        var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.LongitudeX, world);
        var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.LatitudeZ, world);

        // Act
        var results = await SagaProximityService.QueryAllInteractionsAtPositionAsync(
            sagaModelX, sagaModelZ, avatar, world);

        // Assert
        Assert.NotEmpty(results);
        var interaction = results.First(r => r.Type == SagaInteractionType.SagaTrigger);
        Assert.Equal(InteractionStatus.Locked, interaction.Status);
    }

    [Fact]
    public async Task QueryAllInteractionsAtPosition_AvailableTrigger_WithQuestToken()
    {
        // Arrange
        var world = CreateTestWorld();
        var saga = CreateTestSaga("TestSaga", 31.5, 130.5);
        var trigger = CreateTestSagaTrigger("TestTrigger", enterRadius: 10f, requiresQuestToken: "KEY_ITEM");

        world.WorldTemplate.Gameplay.SagaArcs = new[] { saga };
        world.SagaTriggersLookup[saga.RefName] = new List<SagaTrigger> { trigger };

        var avatar = new AvatarBase
        {
            AvatarId = Guid.NewGuid(),
            Capabilities = new ItemCollection
            {
                QuestTokens = new[] { new QuestTokenEntry { QuestTokenRef = "KEY_ITEM" } }
            }
        };

        var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.LongitudeX, world);
        var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.LatitudeZ, world);

        // Act
        var results = await SagaProximityService.QueryAllInteractionsAtPositionAsync(
            sagaModelX, sagaModelZ, avatar, world);

        // Assert
        Assert.NotEmpty(results);
        var interaction = results.First(r => r.Type == SagaInteractionType.SagaTrigger);
        Assert.Equal(InteractionStatus.Available, interaction.Status);
    }

    private World CreateTestWorld(bool isProcedural = true)
    {
        var world = new World
        {
            IsProcedural = isProcedural,
            WorldConfiguration = new WorldConfiguration
            {
                HeightMapSettings = new HeightMapSettings
                {
                    HorizontalScale = 1.0,
                    MapResolutionInMeters = 30.91
                },
                ProceduralSettings = new ProceduralSettings
                {
                    LongitudeDegreesToUnits = 111320.0,
                    LatitudeDegreesToUnits = 110540.0
                }
            },
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = Array.Empty<SagaArc>()
                }
            },
            SagaTriggersLookup = new Dictionary<string, List<SagaTrigger>>(),
            SagaFeaturesLookup = new Dictionary<string, SagaFeature>(),
        };

        return world;
    }

    private SagaArc CreateTestSaga(string refName, double latitude, double longitude)
    {
        return new SagaArc
        {
            RefName = refName,
            DisplayName = $"Test {refName}",
            LatitudeZ = latitude,
            LongitudeX = longitude
            // ItemElementName not set = no feature
        };
    }

    private SagaArc CreateTestSagaWithFeature(string refName, double latitude, double longitude, string featureRef)
    {
        return new SagaArc
        {
            RefName = refName,
            DisplayName = $"Test {refName}",
            LatitudeZ = latitude,
            LongitudeX = longitude,
            SagaFeatureRef = featureRef
        };
    }

    private SagaTrigger CreateTestSagaTrigger(string refName, float enterRadius, string? requiresQuestToken = null)
    {
        return new SagaTrigger
        {
            RefName = refName,
            DisplayName = $"Test {refName}",
            EnterRadius = enterRadius,
            RequiresQuestTokenRef = requiresQuestToken != null ? new[] { requiresQuestToken } : null
        };
    }
}
