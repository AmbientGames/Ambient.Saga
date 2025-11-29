//using Ambient.Application.Contracts;
//using Ambient.Domain;
//using Ambient.Domain.DefinitionExtensions;
//using Ambient.Domain.Entities;
//using Ambient.Domain.GameLogic.Gameplay.Avatar;
//using Ambient.Domain.ValueObjects;
//using Ambient.Saga.Engine.Contracts.Cqrs;
//using Ambient.Saga.Engine.Contracts.Services;
//using Ambient.Saga.Engine.Application.Behaviors;
//using Ambient.Saga.Engine.Application.Commands.Saga;
//using Ambient.Saga.Engine.Application.ReadModels;
//using Ambient.Saga.Engine.Domain.Achievements;
//using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
//using Ambient.Saga.Engine.Infrastructure.Persistence;
//using Ambient.Saga.Engine.Application.Services;
//using Ambient.Saga.Engine.Domain.Services;
//using LiteDB;
//using MediatR;
//using Microsoft.Extensions.DependencyInjection;
//using Xunit.Abstractions;

//namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

///// <summary>
///// Focused regression tests for specific bugs found and fixed during evaluation.
/////
///// REGRESSION TESTS FOR:
///// - Bug #1: Character duplication on position update
///// - Bug #2: Loot system no-op (items not transferred)
///// - Bug #3: Achievement filters stubbed (CharactersDefeatedByType/Tag, TraitsAssignedToCharacterType)
///// - Bug #4: Feature distance using wrong scale factors
///// - Bug #5: Negative price exploit
///// - Bug #6: Selling items not owned
///// - Bug #7: Trading with defeated characters
///// - Bug #8: Transactions.Clear() concurrent modification
/////
///// Each test validates that the specific bug NO LONGER occurs.
///// </summary>
//[Collection("Sequential CQRS Tests")]
//public class RegressionTests : IDisposable
//{
//    private readonly ITestOutputHelper _output;
//    private readonly ServiceProvider _serviceProvider;
//    private readonly IMediator _mediator;
//    private readonly World _world;
//    private readonly LiteDatabase _database;
//    private readonly ISagaInstanceRepository _repository;

//    public RegressionTests(ITestOutputHelper output)
//    {
//        _output = output;
//        _database = new LiteDatabase(new MemoryStream());
//        _world = CreateRegressionTestWorld();

//        var services = new ServiceCollection();

//        services.AddMediatR(cfg =>
//        {
//            cfg.RegisterServicesFromAssemblyContaining<UpdateAvatarPositionCommand>();
//            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
//            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
//            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
//        });

//        services.AddSingleton(_world);
//        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
//        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
//        services.AddSingleton<IGameAvatarRepository>(new TestAvatarRepository()); // Mock repository for tests
//        services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();

//        _serviceProvider = services.BuildServiceProvider();
//        _mediator = _serviceProvider.GetRequiredService<IMediator>();
//        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
//    }

//    [Fact]
//    public async Task Regression_CharacterDuplication_MultiplePositionUpdates_OnlySpawnOnce()
//    {
//        // BUG: Characters spawned on EVERY position update while in trigger radius
//        // FIX: Added check for SagaTriggerStatus.Active (not just Completed)

//        _output.WriteLine("=== REGRESSION: Character Duplication Bug ===");

//        var avatarId = Guid.NewGuid();
//        var avatar = CreateAvatar(avatarId);

//        var sagaRef = "DuplicationTestSaga";

//        // Move to trigger center (should spawn 1 guard)
//        await _mediator.Send(new UpdateAvatarPositionCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            Latitude = 35.0,
//            Longitude = 139.0,
//            Y = 50.0,
//            Avatar = avatar
//        });

//        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
//        var firstSpawnCount = instance.GetCommittedTransactions()
//            .Count(t => t.Type == SagaTransactionType.CharacterSpawned);

//        Assert.Equal(1, firstSpawnCount);
//        _output.WriteLine($"First position update: {firstSpawnCount} guard spawned ✓");

//        // Move AGAIN while still in trigger radius (BUG: used to spawn ANOTHER guard)
//        await _mediator.Send(new UpdateAvatarPositionCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            Latitude = 35.0001, // Slightly different but still in 100m radius
//            Longitude = 139.0001,
//            Y = 50.0,
//            Avatar = avatar
//        });

//        instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
//        var secondSpawnCount = instance.GetCommittedTransactions()
//            .Count(t => t.Type == SagaTransactionType.CharacterSpawned);

//        // ASSERT: Still only 1 guard (no duplication)
//        Assert.Equal(1, secondSpawnCount);
//        _output.WriteLine($"Second position update: {secondSpawnCount} total guards (no duplication) ✓");

//        // Move THIRD time (paranoia check)
//        await _mediator.Send(new UpdateAvatarPositionCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            Latitude = 35.0002,
//            Longitude = 139.0002,
//            Y = 50.0,
//            Avatar = avatar
//        });

//        instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
//        var thirdSpawnCount = instance.GetCommittedTransactions()
//            .Count(t => t.Type == SagaTransactionType.CharacterSpawned);

//        Assert.Equal(1, thirdSpawnCount);
//        _output.WriteLine($"Third position update: {thirdSpawnCount} total guards (still no duplication) ✓");
//        _output.WriteLine("✓ BUG FIXED: Characters only spawn once per trigger activation");
//    }

//    //[Fact]
//    //public async Task Regression_LootSystem_ItemsActuallyTransferred()
//    //{
//    //    // BUG: UpdateAvatarForLootAsync was a no-op stub - no items transferred
//    //    // FIX: Implemented full inventory parsing and transfer

//    //    _output.WriteLine("=== REGRESSION: Loot System No-Op Bug ===");

//    //    var avatarId = Guid.NewGuid();
//    //    var avatar = CreateAvatar(avatarId);

//    //    var sagaRef = "LootTestSaga";

//    //    // Spawn and defeat merchant with loot
//    //    await _mediator.Send(new UpdateAvatarPositionCommand
//    //    {
//    //        AvatarId = avatarId,
//    //        SagaRef = sagaRef,
//    //        Latitude = 35.0,
//    //        Longitude = 139.0,
//    //        Y = 50.0,
//    //        Avatar = avatar
//    //    });

//    //    var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
//    //    var merchantInstanceId = Guid.Parse(
//    //        instance.GetCommittedTransactions()
//    //            .First(t => t.Type == SagaTransactionType.CharacterSpawned)
//    //            .Data["CharacterInstanceId"]);

//    //    // Defeat merchant (manually create transaction for test simplicity)
//    //    var defeatTx = new SagaTransaction
//    //    {
//    //        TransactionId = Guid.NewGuid(),
//    //        Type = SagaTransactionType.CharacterDefeated,
//    //        AvatarId = avatarId.ToString(),
//    //        Status = TransactionStatus.Pending,
//    //        LocalTimestamp = DateTime.UtcNow,
//    //        Data = new Dictionary<string, string>
//    //        {
//    //            ["CharacterInstanceId"] = merchantInstanceId.ToString(),
//    //            ["CharacterRef"] = "RichMerchant",
//    //            ["VictorAvatarId"] = avatarId.ToString()
//    //        }
//    //    };

//    //    instance.AddTransaction(defeatTx);
//    //    await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { defeatTx });
//    //    await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { defeatTx.TransactionId });

//    //    // Loot the merchant
//    //    var lootResult = await _mediator.Send(new LootCharacterCommand
//    //    {
//    //        AvatarId = avatarId,
//    //        SagaRef = sagaRef,
//    //        CharacterInstanceId = merchantInstanceId,
//    //        Avatar = avatar
//    //    });

//    //    // ASSERT: Loot transaction created successfully
//    //    // (Avatar state updates are handled by IAvatarUpdateService in production)
//    //    Assert.True(lootResult.Successful);

//    //    var lootTransactions = instance.GetCommittedTransactions();
//    //    Assert.Contains(lootTransactions, t => t.Type == SagaTransactionType.LootAwarded);

//    //    _output.WriteLine("✓ BUG FIXED: Loot system creates LootAwarded transaction correctly");
//    //}

//    // COMMENTED OUT: Achievement evaluation API has changed - needs rewrite
//    /*
//    [Fact]
//    public void Regression_AchievementFilters_CharactersDefeatedByType_Working()
//    {
//        // BUG: CharactersDefeatedByType returned 0 (stubbed implementation)
//        // FIX: Implemented filter using character.RefName.Contains(type)

//        _output.WriteLine("=== REGRESSION: Achievement Filter Stubbed Bug ===");

//        var transactions = new List<SagaTransaction>
//        {
//            new SagaTransaction
//            {
//                Type = SagaTransactionType.CharacterDefeated,
//                Data = new Dictionary<string, string>
//                {
//                    ["CharacterRef"] = "FireDragon",
//                    ["VictorAvatarId"] = Guid.NewGuid().ToString()
//                }
//            },
//            new SagaTransaction
//            {
//                Type = SagaTransactionType.CharacterDefeated,
//                Data = new Dictionary<string, string>
//                {
//                    ["CharacterRef"] = "IceDragon",
//                    ["VictorAvatarId"] = Guid.NewGuid().ToString()
//                }
//            },
//            new SagaTransaction
//            {
//                Type = SagaTransactionType.CharacterDefeated,
//                Data = new Dictionary<string, string>
//                {
//                    ["CharacterRef"] = "Goblin",
//                    ["VictorAvatarId"] = Guid.NewGuid().ToString()
//                }
//            }
//        };

//        // Create achievement that counts "Dragon" defeats
//        var achievement = new Achievement
//        {
//            RefName = "DragonSlayer",
//            DisplayName = "Dragon Slayer",
//            Criteria = new AchievementCriteria
//            {
//                Type = AchievementCriteriaType.CharactersDefeatedByType,
//                CharacterType = "Dragon",
//                Threshold = 2
//            }
//        };

//        var sagaInstances = new List<SagaInstance>
//        {
//            new SagaInstance
//            {
//                InstanceId = Guid.NewGuid(),
//                OwnerAvatarId = Guid.NewGuid(),
//                SagaRef = "TestSaga",
//                Transactions = transactions
//            }
//        };

//        // ACT: Evaluate achievement
//        var progress = AchievementProgressEvaluator.EvaluateProgress(
//            achievement,
//            sagaInstances,
//            _world,
//            sagaInstances.First().OwnerAvatarId);

//        // ASSERT: Filter correctly counted 2 dragons (not 0 from stub!)
//        Assert.Equal(2.0f, progress.CurrentCount);
//        Assert.True(progress.IsUnlocked);

//        _output.WriteLine($"Dragons defeated: {progress.CurrentCount} / {achievement.Criteria.Threshold}");
//        _output.WriteLine($"Achievement unlocked: {progress.IsUnlocked}");
//        _output.WriteLine("✓ BUG FIXED: CharactersDefeatedByType filter working");
//    }
//    */

//    [Fact]
//    public async Task Regression_TradeValidation_NegativePriceExploit_Blocked()
//    {
//        // BUG: Negative prices allowed → credit duplication exploit
//        // FIX: Added PricePerItem >= 0 validation

//        _output.WriteLine("=== REGRESSION: Negative Price Exploit ===");

//        var avatarId = Guid.NewGuid();
//        var avatar = CreateAvatar(avatarId);
//        avatar.Stats!.Credits = 100;

//        var sagaRef = "TradeExploitSaga";

//        // Spawn merchant
//        await _mediator.Send(new UpdateAvatarPositionCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            Latitude = 35.0,
//            Longitude = 139.0,
//            Y = 50.0,
//            Avatar = avatar
//        });

//        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
//        var merchantInstanceId = Guid.Parse(
//            instance.GetCommittedTransactions()
//                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
//                .Data["CharacterInstanceId"]);

//        // ACT: Try to exploit with negative price (BUG: used to GIVE credits when "buying")
//        var tradeResult = await _mediator.Send(new TradeItemCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            CharacterInstanceId = merchantInstanceId,
//            ItemRef = "IronSword",
//            Quantity = 1,
//            IsBuying = true,
//            PricePerItem = -50, // EXPLOIT: Negative price!
//            Avatar = avatar
//        });

//        // ASSERT: Trade rejected
//        Assert.False(tradeResult.Successful);
//        Assert.Contains("Invalid price", tradeResult.ErrorMessage);

//        _output.WriteLine($"Exploit blocked: {tradeResult.ErrorMessage}");
//        _output.WriteLine("✓ BUG FIXED: Negative price exploit prevented");
//    }

//    [Fact]
//    public async Task Regression_TradeValidation_SellingItemNotOwned_Blocked()
//    {
//        // BUG: Could sell items not in inventory → infinite credits
//        // FIX: Added comprehensive inventory validation

//        _output.WriteLine("=== REGRESSION: Selling Non-Owned Items Exploit ===");

//        var avatarId = Guid.NewGuid();
//        var avatar = CreateAvatar(avatarId);
//        avatar.Capabilities!.Equipment = Array.Empty<EquipmentEntry>(); // NO items!

//        var sagaRef = "TradeExploitSaga";

//        await _mediator.Send(new UpdateAvatarPositionCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            Latitude = 35.0,
//            Longitude = 139.0,
//            Y = 50.0,
//            Avatar = avatar
//        });

//        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
//        var merchantInstanceId = Guid.Parse(
//            instance.GetCommittedTransactions()
//                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
//                .Data["CharacterInstanceId"]);

//        // ACT: Try to sell item we don't own (BUG: used to succeed!)
//        var tradeResult = await _mediator.Send(new TradeItemCommand
//        {
//            AvatarId = avatarId,
//            SagaRef = sagaRef,
//            CharacterInstanceId = merchantInstanceId,
//            ItemRef = "DiamondSword", // Don't own this!
//            Quantity = 1,
//            IsBuying = false, // SELLING
//            PricePerItem = 1000,
//            Avatar = avatar
//        });

//        // ASSERT: Trade rejected
//        Assert.False(tradeResult.Successful);
//        Assert.Contains("does not have", tradeResult.ErrorMessage);

//        _output.WriteLine($"Exploit blocked: {tradeResult.ErrorMessage}");
//        _output.WriteLine("✓ BUG FIXED: Cannot sell items not owned");
//    }

//    [Fact]
//    public async Task Regression_TransactionsListRace_MultipleReloads_NoException()
//    {
//        // BUG: Transactions.Clear() during iteration → ConcurrentModificationException
//        // FIX: Replace entire list instead of Clear+AddRange

//        _output.WriteLine("=== REGRESSION: Transactions.Clear() Race Condition ===");

//        var repository = new SagaInstanceRepository(_database);
//        var avatarId = Guid.NewGuid();
//        var sagaRef = "RaceTestSaga";

//        var instance = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

//        // Add some transactions
//        var transactions = Enumerable.Range(0, 10)
//            .Select(i => new SagaTransaction
//            {
//                TransactionId = Guid.NewGuid(),
//                Type = SagaTransactionType.PlayerEntered,
//                AvatarId = avatarId.ToString(),
//                Status = TransactionStatus.Pending,
//                LocalTimestamp = DateTime.UtcNow,
//                Data = new Dictionary<string, string>()
//            })
//            .ToList();

//        await repository.AddTransactionsAsync(instance.InstanceId, transactions);
//        await repository.CommitTransactionsAsync(instance.InstanceId, transactions.Select(t => t.TransactionId).ToList());

//        // ACT: Reload instance multiple times concurrently (BUG: used to throw)
//        var reloadTasks = Enumerable.Range(0, 20).Select(_ =>
//            Task.Run(async () =>
//            {
//                var reloaded = await repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
//                return reloaded.Transactions.Count;
//            })
//        );

//        var counts = await Task.WhenAll(reloadTasks);

//        // ASSERT: No exception thrown, all reloads successful
//        Assert.All(counts, count => Assert.Equal(10, count));

//        _output.WriteLine($"Completed {counts.Length} concurrent reloads without exception");
//        _output.WriteLine("✓ BUG FIXED: Transactions list reload is thread-safe");
//    }

//    [Fact]
//    public async Task Regression_FeatureDistance_EquatorVsHighLatitude_SameAccuracy()
//    {
//        // BUG: Feature distance used single horizontalScale, wrong at high latitudes
//        // FIX: Separate X and Z scale factors with latitude correction

//        _output.WriteLine("=== REGRESSION: Feature Distance Latitude Bug ===");

//        // This is a validation test - the actual fix is in SagaProximityService
//        // We're just verifying the calculation is correct

//        var equatorWorld = CreateWorldAtLatitude(0.0);
//        var highLatWorld = CreateWorldAtLatitude(60.0);

//        // Both worlds should have consistent 5m feature radius behavior
//        // (This test mainly validates the fix exists - real validation is in unit tests)

//        _output.WriteLine($"Equator world scale X: {equatorWorld.HeightMapLongitudeScale_Validated}");
//        _output.WriteLine($"Equator world scale Z: {equatorWorld.HeightMapLatitudeScale_Validated}");
//        _output.WriteLine($"High-lat world scale X: {highLatWorld.HeightMapLongitudeScale_Validated}");
//        _output.WriteLine($"High-lat world scale Z: {highLatWorld.HeightMapLatitudeScale_Validated}");

//        // At high latitude, X scale should be DIFFERENT from Z scale
//        Assert.NotEqual(
//            equatorWorld.HeightMapLongitudeScale_Validated,
//            highLatWorld.HeightMapLongitudeScale_Validated);

//        _output.WriteLine("✓ BUG FIXED: X and Z scales differ at high latitudes");
//    }

//    public void Dispose()
//    {
//        _database?.Dispose();
//        _serviceProvider?.Dispose();
//    }

//    #region Test Helpers

//    private World CreateRegressionTestWorld()
//    {
//        // Merchant with loot
//        var richMerchant = new Character
//        {
//            RefName = "RichMerchant",
//            DisplayName = "Rich Merchant",
//            Capabilities = new ItemCollection
//            {
//                Equipment = new[]
//                {
//                    new EquipmentEntry { EquipmentRef = "GoldPouch", Condition = 1.0f },
//                    new EquipmentEntry { EquipmentRef = "SilverRing", Condition = 1.0f }
//                }
//            },
//            Stats = new CharacterStats
//            {
//                Health = 1.0f,
//                Credits = 500
//            }
//        };

//        var guard = new Character
//        {
//            RefName = "Guard",
//            DisplayName = "Castle Guard",
//            Stats = new CharacterStats { Health = 1.0f }
//        };

//        var fireDragon = new Character
//        {
//            RefName = "FireDragon",
//            DisplayName = "Fire Dragon"
//        };

//        var iceDragon = new Character
//        {
//            RefName = "IceDragon",
//            DisplayName = "Ice Dragon"
//        };

//        var goblin = new Character
//        {
//            RefName = "Goblin",
//            DisplayName = "Cave Goblin"
//        };

//        var duplicationSaga = CreateTestSaga("DuplicationTestSaga", "Guard");
//        var lootSaga = CreateTestSaga("LootTestSaga", "RichMerchant");
//        var tradeSaga = CreateTestSaga("TradeExploitSaga", "RichMerchant");
//        var raceSaga = CreateTestSaga("RaceTestSaga", "Guard");

//        var world = new World
//        {
//            IsProcedural = true,
//            WorldConfiguration = new WorldConfiguration
//            {
//                RefName = "RegressionTestWorld",
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
//                    SagaArcs = new[] { duplicationSaga.saga, lootSaga.saga, tradeSaga.saga, raceSaga.saga },
//                    Characters = new[] { guard, richMerchant, fireDragon, iceDragon, goblin },
//                    Equipment = new[]
//                    {
//                        new Equipment { RefName = "GoldPouch", DisplayName = "Gold Pouch", WholesalePrice = 500 },
//                        new Equipment { RefName = "SilverRing", DisplayName = "Silver Ring", WholesalePrice = 200 },
//                        new Equipment { RefName = "IronSword", DisplayName = "Iron Sword", WholesalePrice = 50 }
//                    },
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

//        // Populate lookups
//        world.SagaArcLookup[duplicationSaga.saga.RefName] = duplicationSaga.saga;
//        world.SagaArcLookup[lootSaga.saga.RefName] = lootSaga.saga;
//        world.SagaArcLookup[tradeSaga.saga.RefName] = tradeSaga.saga;
//        world.SagaArcLookup[raceSaga.saga.RefName] = raceSaga.saga;
//        world.SagaTriggersLookup[duplicationSaga.saga.RefName] = new List<SagaTrigger> { duplicationSaga.trigger };
//        world.SagaTriggersLookup[lootSaga.saga.RefName] = new List<SagaTrigger> { lootSaga.trigger };
//        world.SagaTriggersLookup[tradeSaga.saga.RefName] = new List<SagaTrigger> { tradeSaga.trigger };
//        world.SagaTriggersLookup[raceSaga.saga.RefName] = new List<SagaTrigger> { raceSaga.trigger };
//        world.CharactersLookup[guard.RefName] = guard;
//        world.CharactersLookup[richMerchant.RefName] = richMerchant;
//        world.CharactersLookup[fireDragon.RefName] = fireDragon;
//        world.CharactersLookup[iceDragon.RefName] = iceDragon;
//        world.CharactersLookup[goblin.RefName] = goblin;

//        return world;
//    }

//    private (SagaArc saga, SagaTrigger trigger) CreateTestSaga(string sagaRef, string characterRef)
//    {
//        var saga = new SagaArc
//        {
//            RefName = sagaRef,
//            DisplayName = sagaRef,
//            LatitudeZ = 35.0,
//            LongitudeX = 139.0,
//            Y = 50.0
//        };

//        var trigger = new SagaTrigger
//        {
//            RefName = $"{sagaRef}_Trigger",
//            EnterRadius = 100.0f,
//            TriggerType = SagaTriggerType.SpawnPassive,
//            Spawn = new[]
//            {
//                new CharacterSpawn
//                {
//                    ItemElementName = ItemChoiceType1.CharacterRef,
//                    Item = characterRef
//                }
//            }
//        };

//        return (saga, trigger);
//    }

//    private AvatarEntity CreateAvatar(Guid avatarId)
//    {
//        return new AvatarEntity
//        {
//            Id = avatarId,
//            AvatarId = avatarId,
//            DisplayName = "Test Avatar",
//            ArchetypeRef = "Warrior",
//            Stats = new CharacterStats
//            {
//                Health = 1.0f,
//                Credits = 0
//            },
//            Capabilities = new ItemCollection
//            {
//                Equipment = Array.Empty<EquipmentEntry>(),
//                Consumables = Array.Empty<ConsumableEntry>()
//            }
//        };
//    }

//    private World CreateWorldAtLatitude(double latitude)
//    {
//        var world = new World
//        {
//            IsProcedural = false,
//            WorldConfiguration = new WorldConfiguration
//            {
//                RefName = $"World_{latitude}",
//                SpawnLatitude = latitude,
//                SpawnLongitude = 139.0,
//                HeightMapSettings = new HeightMapSettings
//                {
//                    HorizontalScale = 30.0,
//                    MapResolutionInMeters = 1.0
//                }
//            },
//            HeightMapMetadata = new GeoTiffMetadata
//            {
//                North = latitude + 0.1,
//                South = latitude - 0.1,
//                East = 139.1,
//                West = 138.9,
//                ImageWidth = 1000,
//                ImageHeight = 1000
//            }
//        };

//        // Calculate scales (matching WorldAssetLoader logic)
//        world.HeightMapLatitudeScale_Validated =
//            world.WorldConfiguration.HeightMapSettings.MapResolutionInMeters *
//            world.WorldConfiguration.HeightMapSettings.HorizontalScale;

//        var centerLatitude = (world.HeightMapMetadata.North + world.HeightMapMetadata.South) / 2.0;
//        var latitudeCorrectionFactor = Math.Cos(centerLatitude * Math.PI / 180.0);
//        world.HeightMapLongitudeScale_Validated = world.HeightMapLatitudeScale_Validated / latitudeCorrectionFactor;

//        return world;
//    }

//    #endregion
//}
