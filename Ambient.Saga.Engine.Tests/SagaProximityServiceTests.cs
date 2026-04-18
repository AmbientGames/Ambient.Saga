using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain.Services;
using Ambient.Saga.Engine.Tests.Helpers;

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
        var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.Longitude, world);
        var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.Latitude, world);

        // Act
        var results = await SagaProximityService.QueryAllInteractionsAtPositionAsync(
            sagaModelX, sagaModelZ, null, world);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Type == SagaInteractionType.SagaTrigger);
        Assert.Contains(results, r => r.EntityRef == sagaTrigger.RefName);
    }


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
            }
        };

        // Provide a world repository with a saga instance so DetermineTriggerStatusAsync
        // can replay state and check quest token requirements (no tokens awarded = locked)
        var worldRepo = new StubWorldStateRepository();
        worldRepo.AddSagaInstance(avatar.AvatarId.ToString(), saga.RefName, new SagaInstance
        {
            InstanceId = Guid.NewGuid(),
            SagaRef = saga.RefName,
            OwnerAvatarId = avatar.AvatarId,
            Transactions = new List<SagaTransaction>()
        });

        var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.Longitude, world);
        var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.Latitude, world);

        // Act
        var results = await SagaProximityService.QueryAllInteractionsAtPositionAsync(
            sagaModelX, sagaModelZ, avatar, world, worldRepo);

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
            Capabilities = new ItemCollection()
        };

        var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.Longitude, world);
        var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.Latitude, world);

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
            SagaTriggersLookup = new System.Collections.Concurrent.ConcurrentDictionary<string, List<SagaTrigger>>()
        };

        return world;
    }

    private SagaArc CreateTestSaga(string refName, double latitude, double longitude)
    {
        return new SagaArc
        {
            RefName = refName,
            DisplayName = $"Test {refName}",
            Latitude = latitude,
            Longitude = longitude
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
