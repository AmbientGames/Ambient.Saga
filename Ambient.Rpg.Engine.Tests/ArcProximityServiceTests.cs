using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Domain.Services;
using Ambient.Rpg.Engine.Tests.Helpers;

namespace Ambient.Rpg.Engine.Tests;

/// <summary>
/// Unit tests for ArcProximityService which handles arc interaction queries.
/// </summary>
public class ArcProximityServiceTests
{
    [Fact]
    public async Task QueryAllInteractionsAtPosition_FindsProximityTrigger()
    {
        // Arrange
        var world = CreateTestWorld();
        var arc = CreateTestArc("TestArc", 31.5, 130.5);
        var arcTrigger = CreateTestArcTrigger("TestTrigger", enterRadius: 10f);

        world.WorldTemplate.Gameplay.Saga = new[] { arc };
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger> { arcTrigger };

        // Avatar at arc center (should be within 10m trigger)
        var arcModelX = CoordinateConverter.LongitudeToModelX(arc.Longitude, world);
        var arcModelZ = CoordinateConverter.LatitudeToModelZ(arc.Latitude, world);

        // Act
        var results = await ArcProximityService.QueryAllInteractionsAtPositionAsync(
            arcModelX, arcModelZ, null, world);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Type == ArcInteractionType.ArcTrigger);
        Assert.Contains(results, r => r.EntityRef == arcTrigger.RefName);
    }


    [Fact]
    public async Task QueryAllInteractionsAtPosition_LockedTrigger_WhenMissingQuestToken()
    {
        // Arrange
        var world = CreateTestWorld();
        var arc = CreateTestArc("TestArc", 31.5, 130.5);
        var trigger = CreateTestArcTrigger("TestTrigger", enterRadius: 10f, requiresQuestToken: "KEY_ITEM");

        world.WorldTemplate.Gameplay.Saga = new[] { arc };
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger> { trigger };

        var avatar = new AvatarBase
        {
            AvatarId = Guid.NewGuid(),
            Capabilities = new ItemCollection
            {
            }
        };

        // Provide a world repository with an arc instance so DetermineTriggerStatusAsync
        // can replay state and check quest token requirements (no tokens awarded = locked)
        var worldRepo = new StubWorldStateRepository();
        worldRepo.AddArcInstance(avatar.AvatarId.ToString(), arc.RefName, new ArcInstance
        {
            InstanceId = Guid.NewGuid(),
            ArcRef = arc.RefName,
            OwnerAvatarId = avatar.AvatarId,
            Transactions = new List<ArcTransaction>()
        });

        var arcModelX = CoordinateConverter.LongitudeToModelX(arc.Longitude, world);
        var arcModelZ = CoordinateConverter.LatitudeToModelZ(arc.Latitude, world);

        // Act
        var results = await ArcProximityService.QueryAllInteractionsAtPositionAsync(
            arcModelX, arcModelZ, avatar, world, worldRepo);

        // Assert
        Assert.NotEmpty(results);
        var interaction = results.First(r => r.Type == ArcInteractionType.ArcTrigger);
        Assert.Equal(InteractionStatus.Locked, interaction.Status);
    }

    [Fact]
    public async Task QueryAllInteractionsAtPosition_AvailableTrigger_WithQuestToken()
    {
        // Arrange
        var world = CreateTestWorld();
        var arc = CreateTestArc("TestArc", 31.5, 130.5);
        var trigger = CreateTestArcTrigger("TestTrigger", enterRadius: 10f, requiresQuestToken: "KEY_ITEM");

        world.WorldTemplate.Gameplay.Saga = new[] { arc };
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger> { trigger };

        var avatar = new AvatarBase
        {
            AvatarId = Guid.NewGuid(),
            Capabilities = new ItemCollection()
        };

        var arcModelX = CoordinateConverter.LongitudeToModelX(arc.Longitude, world);
        var arcModelZ = CoordinateConverter.LatitudeToModelZ(arc.Latitude, world);

        // Act
        var results = await ArcProximityService.QueryAllInteractionsAtPositionAsync(
            arcModelX, arcModelZ, avatar, world);

        // Assert
        Assert.NotEmpty(results);
        var interaction = results.First(r => r.Type == ArcInteractionType.ArcTrigger);
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
                    Saga = Array.Empty<Arc>()
                }
            },
            ArcTriggersLookup = new System.Collections.Concurrent.ConcurrentDictionary<string, List<ArcTrigger>>()
        };

        return world;
    }

    private Arc CreateTestArc(string refName, double latitude, double longitude)
    {
        return new Arc
        {
            RefName = refName,
            DisplayName = $"Test {refName}",
            Latitude = latitude,
            Longitude = longitude
        };
    }

    private ArcTrigger CreateTestArcTrigger(string refName, float enterRadius, string? requiresQuestToken = null)
    {
        return new ArcTrigger
        {
            RefName = refName,
            DisplayName = $"Test {refName}",
            EnterRadius = enterRadius,
            RequiresQuestTokenRef = requiresQuestToken != null ? new[] { requiresQuestToken } : null
        };
    }
}
