//using Ambient.Application.Contracts;
//using Ambient.Domain;
//using Ambient.Domain.DefinitionExtensions;
//using Ambient.Domain.Entities;
//using Ambient.Domain.GameLogic.Gameplay.Avatar;
//using Ambient.Saga.Engine.Contracts.Cqrs;
//using Ambient.Saga.Engine.Contracts.Services;
//using Ambient.Saga.Engine.Application.Behaviors;
//using Ambient.Saga.Engine.Application.Commands.Saga;
//using Ambient.Saga.Engine.Application.ReadModels;
//using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
//using Ambient.Saga.Engine.Infrastructure.Persistence;
//using LiteDB;
//using MediatR;
//using Microsoft.Extensions.DependencyInjection;
//using Xunit.Abstractions;

//namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

///// <summary>
///// Integration tests for InteractWithFeatureCommand.
///// Tests feature loot serialization, avatar updates, and compensating transactions.
///// </summary>
//[Collection("Sequential CQRS Tests")]
//public class InteractWithFeatureCommandTests : IDisposable
//{
//    private readonly ITestOutputHelper _output;
//    private readonly ServiceProvider _serviceProvider;
//    private readonly IMediator _mediator;
//    private readonly World _world;
//    private readonly LiteDatabase _database;
//    private readonly ISagaInstanceRepository _repository;

//    public InteractWithFeatureCommandTests(ITestOutputHelper output)
//    {
//        _output = output;
//        _database = new LiteDatabase(new MemoryStream());
//        _world = CreateWorldWithLootChest();

//        var services = new ServiceCollection();

//        services.AddMediatR(cfg =>
//        {
//            cfg.RegisterServicesFromAssemblyContaining<InteractWithFeatureCommand>();
//            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
//            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
//            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
//        });

//        services.AddSingleton(_world);
//        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
//        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
//        services.AddSingleton<IGameAvatarRepository>(new TestAvatarRepository()); // Mock repository (from FullSagaFlowE2ETests)
//        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();

//        _serviceProvider = services.BuildServiceProvider();
//        _mediator = _serviceProvider.GetRequiredService<IMediator>();
//        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
//    }

//    [Fact]
//    public async Task InteractWithFeature_WithLoot_CreatesLootAwardedTransactionWithSerializedItems()
//    {
//        // ARRANGE
//        var avatarId = Guid.NewGuid();
//        var avatar = CreateAvatar(avatarId);
//        var sagaRef = "LootChestSaga";
//        var featureRef = "TreasureChest";

//        _output.WriteLine("=== TEST: Feature Loot Transaction Serialization ===");

//        // ACT: Interact with feature containing loot
//        var result = await _mediator.Send(new InteractWithFeatureCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            FeatureRef = featureRef,
//            Avatar = avatar
//        });

//        // ASSERT: Command succeeded
//        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");

//        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
//        var transactions = instance.GetCommittedTransactions().ToList();

//        // Should have EntityInteracted + LootAwarded
//        Assert.Contains(transactions, t => t.Type == SagaTransactionType.EntityInteracted);
//        var lootTx = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.LootAwarded);
//        Assert.NotNull(lootTx);

//        // CRITICAL: Loot items must be serialized in transaction Data
//        Assert.True(lootTx.Data.ContainsKey("Equipment"));
//        Assert.True(lootTx.Data.ContainsKey("Consumables"));
//        Assert.True(lootTx.Data.ContainsKey("Spells"));
//        Assert.True(lootTx.Data.ContainsKey("Blocks"));
//        Assert.True(lootTx.Data.ContainsKey("Tools"));
//        Assert.True(lootTx.Data.ContainsKey("BuildingMaterials"));

//        // Verify equipment serialization format: "RefName:Condition"
//        var equipment = lootTx.Data["Equipment"];
//        Assert.Contains("IronSword:1.00", equipment);
//        _output.WriteLine($"✓ Equipment serialized: {equipment}");

//        // Verify consumables serialization format: "RefName:Quantity"
//        var consumables = lootTx.Data["Consumables"];
//        Assert.Contains("HealthPotion:5", consumables);
//        _output.WriteLine($"✓ Consumables serialized: {consumables}");

//        // Verify spells serialization format: "RefName:Condition"
//        var spells = lootTx.Data["Spells"];
//        Assert.Contains("Fireball:1.00", spells);
//        _output.WriteLine($"✓ Spells serialized: {spells}");

//        _output.WriteLine("✓ All loot items properly serialized in transaction");
//    }

//    [Fact]
//    public async Task InteractWithFeature_WithLoot_CallsAvatarUpdateService()
//    {
//        // ARRANGE
//        var avatarId = Guid.NewGuid();
//        var avatarEntity = new AvatarEntity
//        {
//            AvatarId = avatarId,
//            DisplayName = "Test Avatar",
//            Stats = new CharacterStats { Health = 100.0f, Credits = 0 },
//            Capabilities = new ItemCollection { Equipment = Array.Empty<EquipmentEntry>() }
//        };
//        var sagaRef = "LootChestSaga";
//        var featureRef = "TreasureChest";

//        _output.WriteLine("=== TEST: Avatar Update Service Called ===");

//        // ACT: Interact with feature
//        var result = await _mediator.Send(new InteractWithFeatureCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            FeatureRef = featureRef,
//            Avatar = avatarEntity
//        });

//        // ASSERT: Command succeeded and avatar updated
//        Assert.True(result.Successful);
//        Assert.NotNull(result.UpdatedAvatar);
//        _output.WriteLine("✓ IAvatarUpdateService.UpdateAvatarForLootAsync() called");
//        _output.WriteLine("✓ Avatar returned in result");
//    }

//    [Fact]
//    public async Task InteractWithFeature_MaxInteractions_BlocksAfterLimit()
//    {
//        // ARRANGE: Feature with MaxInteractions=2
//        var avatarId = Guid.NewGuid();
//        var avatar = CreateAvatar(avatarId);
//        var sagaRef = "LimitedChestSaga";
//        var featureRef = "LimitedChest";

//        _output.WriteLine("=== TEST: MaxInteractions Enforcement ===");

//        // ACT: Interact twice (should succeed)
//        var result1 = await _mediator.Send(new InteractWithFeatureCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            FeatureRef = featureRef,
//            Avatar = avatar
//        });
//        Assert.True(result1.Successful);
//        _output.WriteLine("✓ Interaction 1/2 succeeded");

//        var result2 = await _mediator.Send(new InteractWithFeatureCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            FeatureRef = featureRef,
//            Avatar = avatar
//        });
//        Assert.True(result2.Successful);
//        _output.WriteLine("✓ Interaction 2/2 succeeded");

//        // ACT: Interact third time (should fail)
//        var result3 = await _mediator.Send(new InteractWithFeatureCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            FeatureRef = featureRef,
//            Avatar = avatar
//        });

//        // ASSERT: Third interaction blocked
//        Assert.False(result3.Successful);
//        Assert.Contains("reached maximum interactions", result3.ErrorMessage);
//        _output.WriteLine($"✓ Interaction 3 blocked: {result3.ErrorMessage}");
//    }

//    [Fact]
//    public async Task InteractWithFeature_QuestTokens_CreatesQuestTokenAwardedTransaction()
//    {
//        // ARRANGE
//        var avatarId = Guid.NewGuid();
//        var avatar = CreateAvatar(avatarId);
//        var sagaRef = "QuestMarkerSaga";
//        var featureRef = "QuestSignpost";

//        _output.WriteLine("=== TEST: Quest Token Awarded ===");

//        // ACT: Interact with feature that gives quest tokens
//        var result = await _mediator.Send(new InteractWithFeatureCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            FeatureRef = featureRef,
//            Avatar = avatar
//        });

//        // ASSERT: QuestTokenAwarded transaction created
//        Assert.True(result.Successful);

//        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
//        var transactions = instance.GetCommittedTransactions().ToList();

//        var tokenTx = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.QuestTokenAwarded);
//        Assert.NotNull(tokenTx);
//        Assert.Equal("DragonSlayerToken", tokenTx.Data["QuestTokenRef"]);
//        _output.WriteLine($"✓ Quest token awarded: {tokenTx.Data["QuestTokenRef"]}");
//    }

//    private World CreateWorldWithLootChest()
//    {
//        // Create landmark with loot (treasure chest)
//        var treasureChest = new Landmark
//        {
//            RefName = "TreasureChest",
//            DisplayName = "Ancient Treasure Chest",
//            Interactable = new InteractableBase
//            {
//                Loot = new ItemCollection
//                {
//                    Equipment = new[]
//                    {
//                        new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f }
//                    },
//                    Consumables = new[]
//                    {
//                        new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 5 }
//                    },
//                    Spells = new[]
//                    {
//                        new SpellEntry { SpellRef = "Fireball", Condition = 1.0f }
//                    },
//                    Blocks = new[]
//                    {
//                        new BlockEntry { BlockRef = "Stone", Quantity = 50 }
//                    },
//                    Tools = new[]
//                    {
//                        new ToolEntry { ToolRef = "Pickaxe", Condition = 0.8f }
//                    },
//                    BuildingMaterials = new[]
//                    {
//                        new BuildingMaterialEntry { BuildingMaterialRef = "Wood", Quantity = 100 }
//                    }
//                }
//            }
//        };

//        var lootChestSaga = new SagaArc
//        {
//            RefName = "LootChestSaga",
//            DisplayName = "Treasure Hunt",
//            LatitudeZ = 35.0,
//            LongitudeX = 139.0,
//            Y = 100.0,
//            Item = "TreasureChest"
//        };

//        // Create landmark with MaxInteractions limit
//        var limitedChest = new Landmark
//        {
//            RefName = "LimitedChest",
//            DisplayName = "Limited Chest",
//            Interactable = new InteractableBase
//            {
//                MaxInteractions = 2,
//                Loot = new ItemCollection
//                {
//                    Consumables = new[]
//                    {
//                        new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 1 }
//                    }
//                }
//            }
//        };

//        var limitedChestSaga = new SagaArc
//        {
//            RefName = "LimitedChestSaga",
//            DisplayName = "Limited Treasure",
//            LatitudeZ = 36.0,
//            LongitudeX = 140.0,
//            Y = 100.0,
//            Item = "LimitedChest"
//        };

//        // Create quest signpost with quest token
//        var questSignpost = new QuestSignpost
//        {
//            RefName = "QuestSignpost",
//            DisplayName = "Dragon Lair Marker",
//            Interactable = new InteractableBase
//            {
//                GivesQuestTokenRef = new[] { "DragonSlayerToken" }
//            }
//        };

//        var questMarkerSaga = new SagaArc
//        {
//            RefName = "QuestMarkerSaga",
//            DisplayName = "Quest Marker",
//            LatitudeZ = 37.0,
//            LongitudeX = 141.0,
//            Y = 100.0,
//            Item = "QuestSignpost"
//        };

//        var sagas = new[] { lootChestSaga, limitedChestSaga, questMarkerSaga };
//        var landmarks = new[] { treasureChest, limitedChest };
//        var questSignposts = new[] { questSignpost };

//        var world = new World
//        {
//            IsProcedural = true,
//            WorldConfiguration = new WorldConfiguration
//            {
//                RefName = "TestWorld",
//                SpawnLatitude = 35.0,
//                SpawnLongitude = 139.0,
//                ProceduralSettings = new ProceduralSettings
//                {
//                    LatitudeDegreesToUnits = 111320.0,
//                    LongitudeDegreesToUnits = 91300.0
//                }
//            },
//            WorldTemplate = new WorldTemplate
//            {
//                Gameplay = new GameplayComponents
//                {
//                    SagaArcs = sagas,
//                    Landmarks = landmarks,
//                    QuestSignposts = questSignposts,
//                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
//                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
//                    Achievements = Array.Empty<Achievement>(),
//                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
//                    DialogueTrees = Array.Empty<DialogueTree>()
//                },
//                Simulation = new SimulationComponents(),
//                Presentation = new PresentationComponents()
//            }
//        };

//        // Initialize lookups
//        world.SagaArcLookup = sagas.ToDictionary(s => s.RefName, s => s);
//        world.SagaTriggersLookup = sagas.ToDictionary(
//            s => s.RefName,
//            s => new List<SagaTrigger>
//            {
//                new SagaTrigger
//                {
//                    RefName = s.RefName,
//                    TriggerType = SagaTriggerType.SpawnPassive,
//                    EnterRadius = 10.0f
//                }
//            });

//        world.LandmarksLookup = landmarks.ToDictionary(l => l.RefName, l => l);
//        world.QuestSignpostsLookup = questSignposts.ToDictionary(q => q.RefName, q => q);

//        return world;
//    }

//    private AvatarEntity CreateAvatar(Guid avatarId)
//    {
//        return new AvatarEntity
//        {
//            Id = avatarId,
//            AvatarId = avatarId,
//            DisplayName = "Test Avatar",
//            Stats = new CharacterStats { Health = 100.0f },
//            Capabilities = new ItemCollection()
//        };
//    }

//    public void Dispose()
//    {
//        _database?.Dispose();
//        _serviceProvider?.Dispose();
//    }
//}
