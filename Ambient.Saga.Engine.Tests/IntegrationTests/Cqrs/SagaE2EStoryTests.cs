using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Xunit.Abstractions;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// End-to-End story tests for complete Saga flows.
///
/// These tests exercise the full system from avatar movement through character interactions
/// to final state verification, with human-readable logging to visualize the narrative flow.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class SagaE2EStoryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;
    private readonly StoryLogger _logger;

    public SagaE2EStoryTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new StoryLogger();

        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorld();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<UpdateAvatarPositionCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        services.AddSingleton(_world);
        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
    }

    private World CreateTestWorld()
    {
        // Create characters
        var merchant = new Character
        {
            RefName = "VillageMerchant",
            DisplayName = "Takeshi the Merchant",
            Interactable = new Interactable
            {
                DialogueTreeRef = "MerchantGreeting"
            }
        };

        var boss = new Character
        {
            RefName = "CastleLord",
            DisplayName = "Lord Yamamoto",
            Interactable = new Interactable
            {
                DialogueTreeRef = "BossChallenge"
            }
        };

        // Create Saga with two zones: merchant and boss
        var sagaArc = new SagaArc
        {
            RefName = "KagoshimaCastle",
            DisplayName = "Kagoshima Castle",
            LatitudeZ = 31.5955,
            LongitudeX = 130.5569
        };

        var merchantTrigger = new SagaTrigger
        {
            RefName = "MerchantZone",
            EnterRadius = 50.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    ItemElementName = ItemChoiceType.CharacterRef,
                    Item = "VillageMerchant"
                }
            }
        };

        var bossTrigger = new SagaTrigger
        {
            RefName = "BossZone",
            EnterRadius = 30.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    ItemElementName = ItemChoiceType.CharacterRef,
                    Item = "CastleLord"
                }
            }
        };

        // Create dialogue trees for the characters
        var merchantDialogue = new DialogueTree
        {
            RefName = "MerchantGreeting",
            StartNodeId = "greeting",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "greeting",
                    Text = new[] { "Welcome to Kagoshima Castle! What brings you here?" },
                    Choice = Array.Empty<DialogueChoice>()
                }
            }
        };

        var bossDialogue = new DialogueTree
        {
            RefName = "BossChallenge",
            StartNodeId = "challenge",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "challenge",
                    Text = new[] { "You have entered my domain. Prepare to face Lord Yamamoto!" },
                    Choice = Array.Empty<DialogueChoice>()
                }
            }
        };

        var world = new World
        {
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "TestWorld",
                SpawnLatitude = 31.5955,
                SpawnLongitude = 130.5569,
                ProceduralSettings = new ProceduralSettings
                {
                    LatitudeDegreesToUnits = 111320.0,
                    LongitudeDegreesToUnits = 91300.0
                }
            },
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = new[] { sagaArc },
                    Characters = new[] { merchant, boss },
                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = new[] { merchantDialogue, bossDialogue }
                },
                //Simulation = new SimulationComponents(),
                //Presentation = new PresentationComponents()
            }
        };

        // Populate lookups
        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger> { merchantTrigger, bossTrigger };
        world.CharactersLookup[merchant.RefName] = merchant;
        world.CharactersLookup[boss.RefName] = boss;
        world.DialogueTreesLookup[merchantDialogue.RefName] = merchantDialogue;
        world.DialogueTreesLookup[bossDialogue.RefName] = bossDialogue;

        return world;
    }

    private AvatarEntity CreateAvatar(string name = "Test Hero")
    {
        var archetype = new AvatarArchetype
        {
            RefName = "Warrior",
            DisplayName = "Warrior",
            Description = "A brave warrior",
            AffinityRef = "Physical",
            SpawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Hunger = 0f,
                Thirst = 0f,
                Temperature = 37f,
                Insulation = 0f,
                Credits = 500,  // Start with 500 credits
                Experience = 0,
                Strength = 0.10f,
                Defense = 0.10f,
                Speed = 0.10f,
                Magic = 0.10f
            },
            SpawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                Blocks = Array.Empty<BlockEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                BuildingMaterials = Array.Empty<BuildingMaterialEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            },
            RespawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Hunger = 0f,
                Thirst = 0f,
                Temperature = 37f,
                Insulation = 0f,
                Credits = 100,
                Experience = 0,
                Strength = 0.08f,
                Defense = 0.08f,
                Speed = 0.08f,
                Magic = 0.08f
            },
            RespawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                Blocks = Array.Empty<BlockEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                BuildingMaterials = Array.Empty<BuildingMaterialEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            }
        };

        var avatar = new AvatarEntity
        {
            Id = Guid.NewGuid(),
            ArchetypeRef = "Warrior",
            DisplayName = name,
            BlockOwnership = new Dictionary<string, float>()
        };

        AvatarSpawner.SpawnFromModelAvatar(
            avatar,
            archetype);

        return avatar;
    }

    [Fact]
    public async Task CompleteSagaStory_EnterZone_Dialogue_Trade_DefeatBoss_VerifyFinalState()
    {
        _logger.LogHeader("COMPLETE SAGA STORY TEST");
        _logger.LogSeparator();

        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar("Hiroshi");
        avatar.AvatarId = avatarId;

        _logger.LogEvent($"Avatar Created: {avatar.DisplayName} (ID: {avatarId})");
        _logger.LogEvent($"Starting Credits: {avatar.Stats.Credits}");
        _logger.LogSeparator();

        // ACT 1: Enter the Saga zone
        _logger.LogHeader("ACT 1: ENTERING KAGOSHIMA CASTLE");

        var positionResult = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            Latitude = 31.5955,  // Saga center - will activate both triggers
            Longitude = 130.5569,
            Y = 50.0,
            Avatar = avatar
        });

        Assert.True(positionResult.Successful, $"Position update failed: {positionResult.ErrorMessage}");

        _logger.LogEvent("✓ Position update command succeeded");
        _logger.LogEvent($"✓ Transactions created: {positionResult.TransactionIds.Count}");
        _logger.LogSeparator();

        // Query state to see what happened (pure CQRS pattern)
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "KagoshimaCastle");
        var saga = _world.SagaArcLookup["KagoshimaCastle"];
        var triggers = _world.SagaTriggersLookup["KagoshimaCastle"];
        var stateMachine = new SagaStateMachine(saga, triggers, _world);
        var currentState = stateMachine.ReplayToNow(instance);

        Assert.NotEmpty(currentState.Characters);

        var merchantInstance = currentState.Characters.Values.FirstOrDefault(c => c.CharacterRef == "VillageMerchant");
        var bossInstance = currentState.Characters.Values.FirstOrDefault(c => c.CharacterRef == "CastleLord");

        Assert.NotNull(merchantInstance);
        Assert.NotNull(bossInstance);

        var merchantTemplate = _world.CharactersLookup[merchantInstance.CharacterRef];
        var bossTemplate = _world.CharactersLookup[bossInstance.CharacterRef];

        _logger.LogEvent($"Merchant spawned: {merchantTemplate.DisplayName} (ID: {merchantInstance.CharacterInstanceId})");
        _logger.LogEvent($"Boss spawned: {bossTemplate.DisplayName} (ID: {bossInstance.CharacterInstanceId})");
        _logger.LogSeparator();

        // ACT 2: Talk to merchant
        _logger.LogHeader("ACT 2: APPROACHING THE MERCHANT");

        var dialogueResult = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            CharacterInstanceId = merchantInstance.CharacterInstanceId,
            Avatar = avatar
        });

        Assert.True(dialogueResult.Successful, $"Dialogue failed: {dialogueResult.ErrorMessage}");

        _logger.LogEvent($"✓ Started conversation with {merchantTemplate.DisplayName}");
        _logger.LogEvent($"✓ Dialogue: MerchantGreeting");
        _logger.LogSeparator();

        // ACT 3: Trade with merchant
        _logger.LogHeader("ACT 3: TRADING WITH MERCHANT");

        var initialCredits = avatar.Stats.Credits;
        var itemCost = 50;
        var itemQuantity = 3;

        var tradeResult = await _mediator.Send(new TradeItemCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            CharacterInstanceId = merchantInstance.CharacterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = itemQuantity,
            IsBuying = true,
            PricePerItem = itemCost,
            Avatar = avatar
        });

        Assert.True(tradeResult.Successful, $"Trade failed: {tradeResult.ErrorMessage}");

        _logger.LogEvent($"✓ Purchased {itemQuantity}x Health Potion");
        _logger.LogEvent($"✓ Total cost: {itemCost * itemQuantity} credits");
        _logger.LogEvent($"✓ Credits: {initialCredits} → {initialCredits - itemCost * itemQuantity}");
        _logger.LogSeparator();

        // ACT 4: Defeat the boss
        _logger.LogHeader("ACT 4: CONFRONTING LORD YAMAMOTO");

        _logger.LogEvent($"Boss: {bossTemplate.DisplayName}");
        _logger.LogEvent($"Boss Health: {bossInstance.CurrentHealth}");
        _logger.LogEvent("Engaging in combat...");

        var defeatResult = await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            CharacterInstanceId = bossInstance.CharacterInstanceId
        });

        Assert.True(defeatResult.Successful, $"Defeat failed: {defeatResult.ErrorMessage}");

        _logger.LogEvent($"✓ {bossTemplate.DisplayName} defeated!");
        _logger.LogSeparator();

        // FINAL STATE VERIFICATION
        _logger.LogHeader("FINAL STATE VERIFICATION");

        // Reload instance and replay to get final state
        instance = await _repository.GetOrCreateInstanceAsync(avatarId, "KagoshimaCastle");
        currentState = stateMachine.ReplayToNow(instance);

        // Verify boss is dead
        var finalBossState = currentState.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == bossInstance.CharacterInstanceId);
        Assert.NotNull(finalBossState);
        Assert.False(finalBossState.IsAlive);
        Assert.Equal(0.0f, finalBossState.CurrentHealth);

        _logger.LogEvent($"✓ Boss status: Dead (Health: {finalBossState.CurrentHealth})");

        // Verify transaction log
        var allTransactions = instance.GetCommittedTransactions().OrderBy(t => t.SequenceNumber).ToList();

        _logger.LogEvent($"✓ Total transactions recorded: {allTransactions.Count}");
        _logger.LogEvent($"  - TriggerActivated: {allTransactions.Count(t => t.Type == SagaTransactionType.TriggerActivated)}");
        _logger.LogEvent($"  - CharacterSpawned: {allTransactions.Count(t => t.Type == SagaTransactionType.CharacterSpawned)}");
        _logger.LogEvent($"  - DialogueStarted: {allTransactions.Count(t => t.Type == SagaTransactionType.DialogueStarted)}");
        _logger.LogEvent($"  - ItemTraded: {allTransactions.Count(t => t.Type == SagaTransactionType.ItemTraded)}");
        _logger.LogEvent($"  - CharacterDefeated: {allTransactions.Count(t => t.Type == SagaTransactionType.CharacterDefeated)}");

        // Verify all transactions are committed
        Assert.All(allTransactions, tx => Assert.Equal(TransactionStatus.Committed, tx.Status));
        _logger.LogEvent($"✓ All transactions committed");

        // Verify sequence numbers are ordered
        var sequenceNumbers = allTransactions.Select(t => t.SequenceNumber).ToList();
        var sortedSequenceNumbers = sequenceNumbers.OrderBy(s => s).ToList();
        Assert.Equal(sortedSequenceNumbers, sequenceNumbers);
        _logger.LogEvent($"✓ Transaction sequence numbers properly ordered");

        _logger.LogSeparator();
        _logger.LogEvent("STORY COMPLETE ✓");

        // Output the complete log
        _output.WriteLine(_logger.GetLog());
    }

    [Fact]
    public async Task DeterminismTest_RunSameStoryTwice_IdenticalResults()
    {
        _logger.LogHeader("DETERMINISM TEST");
        _logger.LogSeparator();

        // Run the story twice with identical inputs (different avatars, different saga instances)
        var result1 = await RunCompleteStory("Run 1", Guid.NewGuid());

        // Clear database for second run to avoid conflicts
        _database.DropCollection("SagaInstances");
        _database.DropCollection("SagaTransactions");

        var result2 = await RunCompleteStory("Run 2", Guid.NewGuid());

        _logger.LogHeader("COMPARING RESULTS");

        // Compare transaction counts
        _logger.LogEvent($"Run 1 transactions: {result1.TransactionCount}");
        _logger.LogEvent($"Run 2 transactions: {result2.TransactionCount}");
        Assert.Equal(result1.TransactionCount, result2.TransactionCount);
        _logger.LogEvent("✓ Transaction counts match");

        // Compare transaction types
        _logger.LogEvent($"Run 1 transaction breakdown: {string.Join(", ", result1.TransactionsByType)}");
        _logger.LogEvent($"Run 2 transaction breakdown: {string.Join(", ", result2.TransactionsByType)}");
        Assert.Equal(result1.TransactionsByType, result2.TransactionsByType);
        _logger.LogEvent("✓ Transaction type breakdown matches");

        // Compare final states
        _logger.LogEvent($"Run 1 boss defeated: {result1.BossDefeated}");
        _logger.LogEvent($"Run 2 boss defeated: {result2.BossDefeated}");
        Assert.Equal(result1.BossDefeated, result2.BossDefeated);
        _logger.LogEvent("✓ Boss defeat status matches");

        _logger.LogEvent($"Run 1 characters spawned: {result1.CharactersSpawned}");
        _logger.LogEvent($"Run 2 characters spawned: {result2.CharactersSpawned}");
        Assert.Equal(result1.CharactersSpawned, result2.CharactersSpawned);
        _logger.LogEvent("✓ Character spawn count matches");

        _logger.LogSeparator();
        _logger.LogEvent("DETERMINISM VERIFIED ✓");
        _logger.LogEvent("Same inputs produced identical outputs");

        _output.WriteLine(_logger.GetLog());
    }

    private async Task<StoryResult> RunCompleteStory(string runName, Guid avatarId)
    {
        var avatar = CreateAvatar($"Hero-{runName}");
        avatar.AvatarId = avatarId;

        // Enter zone
        var positionResult = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            Latitude = 31.5955,
            Longitude = 130.5569,
            Y = 50.0,
            Avatar = avatar
        });

        // Get spawned characters
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "KagoshimaCastle");
        var saga = _world.SagaArcLookup["KagoshimaCastle"];
        var triggers = _world.SagaTriggersLookup["KagoshimaCastle"];
        var stateMachine = new SagaStateMachine(saga, triggers, _world);
        var currentState = stateMachine.ReplayToNow(instance);

        var merchantInstance = currentState.Characters.Values.First(c => c.CharacterRef == "VillageMerchant");
        var bossInstance = currentState.Characters.Values.First(c => c.CharacterRef == "CastleLord");

        // Dialogue
        await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            CharacterInstanceId = merchantInstance.CharacterInstanceId,
            Avatar = avatar
        });

        // Trade
        await _mediator.Send(new TradeItemCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            CharacterInstanceId = merchantInstance.CharacterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 3,
            IsBuying = true,
            PricePerItem = 50,
            Avatar = avatar
        });

        // Defeat boss
        await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            CharacterInstanceId = bossInstance.CharacterInstanceId
        });

        // Collect final state
        instance = await _repository.GetOrCreateInstanceAsync(avatarId, "KagoshimaCastle");
        currentState = stateMachine.ReplayToNow(instance);
        var allTransactions = instance.GetCommittedTransactions().ToList();

        var transactionsByType = allTransactions
            .GroupBy(t => t.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var finalBossState = currentState.Characters.Values.First(c => c.CharacterInstanceId == bossInstance.CharacterInstanceId);

        return new StoryResult
        {
            TransactionCount = allTransactions.Count,
            TransactionsByType = transactionsByType,
            BossDefeated = !finalBossState.IsAlive,
            CharactersSpawned = currentState.Characters.Count
        };
    }

    [Fact]
    public async Task TransactionReplay_ExecuteStory_ThenReplayLog_ProducesSameState()
    {
        _logger.LogHeader("TRANSACTION REPLAY TEST");
        _logger.LogEvent("Testing that replaying transaction log reconstructs identical state");
        _logger.LogSeparator();

        // PART 1: Execute the story and log what happens
        _logger.LogHeader("PART 1: ORIGINAL EXECUTION (Commands → Transactions)");

        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar("Kenji");
        avatar.AvatarId = avatarId;

        _logger.LogEvent($"Avatar: {avatar.DisplayName} (ID: {avatarId})");
        _logger.LogEvent($"Starting Credits: {avatar.Stats.Credits}");
        _logger.LogSeparator();

        // Execute: Enter zone
        _logger.LogEvent("ACTION: Moving to Kagoshima Castle...");
        var positionResult = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            Latitude = 31.5955,
            Longitude = 130.5569,
            Y = 50.0,
            Avatar = avatar
        });
        Assert.True(positionResult.Successful);

        // Get initial state after entry
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "KagoshimaCastle");
        var saga = _world.SagaArcLookup["KagoshimaCastle"];
        var triggers = _world.SagaTriggersLookup["KagoshimaCastle"];
        var stateMachine = new SagaStateMachine(saga, triggers, _world);
        var state = stateMachine.ReplayToNow(instance);

        _logger.LogEvent($"  → Entered castle zone");
        _logger.LogEvent($"  → {state.Characters.Count} characters spawned");
        _logger.LogSeparator();

        var merchantInstance = state.Characters.Values.First(c => c.CharacterRef == "VillageMerchant");
        var bossInstance = state.Characters.Values.First(c => c.CharacterRef == "CastleLord");

        // Execute: Dialogue
        _logger.LogEvent($"ACTION: Starting dialogue with merchant (ID: {merchantInstance.CharacterInstanceId})...");
        var dialogueResult = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            CharacterInstanceId = merchantInstance.CharacterInstanceId,
            Avatar = avatar
        });
        Assert.True(dialogueResult.Successful);
        _logger.LogEvent($"  → Dialogue tree: MerchantGreeting");
        _logger.LogSeparator();

        // Execute: Trade
        _logger.LogEvent($"ACTION: Trading with merchant...");
        var tradeResult = await _mediator.Send(new TradeItemCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            CharacterInstanceId = merchantInstance.CharacterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 3,
            IsBuying = true,
            PricePerItem = 50,
            Avatar = avatar
        });
        Assert.True(tradeResult.Successful);
        _logger.LogEvent($"  → Bought 3x HealthPotion for 150 credits");
        _logger.LogSeparator();

        // Execute: Boss defeat
        _logger.LogEvent($"ACTION: Engaging boss (ID: {bossInstance.CharacterInstanceId})...");
        var defeatResult = await _mediator.Send(new DefeatCharacterCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "KagoshimaCastle",
            CharacterInstanceId = bossInstance.CharacterInstanceId
        });
        Assert.True(defeatResult.Successful);
        _logger.LogEvent($"  → Boss defeated!");
        _logger.LogSeparator();

        // PART 2: Get final state from first replay
        _logger.LogHeader("PART 2: FIRST REPLAY (Transactions → State A)");

        instance = await _repository.GetOrCreateInstanceAsync(avatarId, "KagoshimaCastle");
        var transactions = instance.GetCommittedTransactions().OrderBy(t => t.SequenceNumber).ToList();

        _logger.LogEvent($"Transaction log contains {transactions.Count} transactions:");
        foreach (var tx in transactions)
        {
            _logger.LogEvent($"  [{tx.SequenceNumber}] {tx.Type} - {tx.GetCanonicalTimestamp():HH:mm:ss.fff}");
            if (tx.Type == SagaTransactionType.DialogueStarted)
            {
                _logger.LogEvent($"      DialogueTree: {tx.Data.GetValueOrDefault("DialogueTreeRef", "?")}");
            }
            else if (tx.Type == SagaTransactionType.ItemTraded)
            {
                var item = tx.Data.GetValueOrDefault("ItemRef", "?");
                var qty = tx.Data.GetValueOrDefault("Quantity", "?");
                var buying = tx.Data.GetValueOrDefault("IsBuying", "?");
                var price = tx.Data.GetValueOrDefault("TotalPrice", "?");
                _logger.LogEvent($"      {(buying == "True" ? "Bought" : "Sold")} {qty}x {item} for {price} credits");
            }
            else if (tx.Type == SagaTransactionType.CharacterDefeated)
            {
                var charRef = state.Characters.Values
                    .FirstOrDefault(c => c.CharacterInstanceId.ToString() == tx.Data.GetValueOrDefault("CharacterInstanceId"))
                    ?.CharacterRef ?? "?";
                _logger.LogEvent($"      Character: {charRef}");
            }
        }
        _logger.LogSeparator();

        // Replay to get State A
        var stateA = stateMachine.ReplayToNow(instance);

        _logger.LogEvent("State A (from first replay):");
        _logger.LogEvent($"  Characters: {stateA.Characters.Count}");
        _logger.LogEvent($"  Active triggers: {stateA.Triggers.Count(t => t.Value.Status == SagaTriggerStatus.Active)}");
        var bossA = stateA.Characters.Values.First(c => c.CharacterRef == "CastleLord");
        _logger.LogEvent($"  Boss alive: {bossA.IsAlive}");
        _logger.LogEvent($"  Boss health: {bossA.CurrentHealth}");
        _logger.LogSeparator();

        // PART 3: Create fresh state machine and replay again
        _logger.LogHeader("PART 3: SECOND REPLAY (Same Transactions → State B)");
        _logger.LogEvent("Creating fresh state machine and replaying same transaction log...");
        _logger.LogSeparator();

        // Create a new state machine from scratch
        var stateMachine2 = new SagaStateMachine(saga, triggers, _world);

        // Replay the exact same transactions
        var stateB = stateMachine2.Replay(transactions);

        _logger.LogEvent("State B (from second replay):");
        _logger.LogEvent($"  Characters: {stateB.Characters.Count}");
        _logger.LogEvent($"  Active triggers: {stateB.Triggers.Count(t => t.Value.Status == SagaTriggerStatus.Active)}");
        var bossB = stateB.Characters.Values.First(c => c.CharacterRef == "CastleLord");
        _logger.LogEvent($"  Boss alive: {bossB.IsAlive}");
        _logger.LogEvent($"  Boss health: {bossB.CurrentHealth}");
        _logger.LogSeparator();

        // PART 4: Verify states match
        _logger.LogHeader("PART 4: STATE COMPARISON");

        _logger.LogEvent("Comparing State A vs State B:");

        Assert.Equal(stateA.Characters.Count, stateB.Characters.Count);
        _logger.LogEvent($"✓ Character count matches: {stateA.Characters.Count}");

        Assert.Equal(stateA.Triggers.Count, stateB.Triggers.Count);
        _logger.LogEvent($"✓ Trigger count matches: {stateA.Triggers.Count}");

        Assert.Equal(bossA.IsAlive, bossB.IsAlive);
        _logger.LogEvent($"✓ Boss alive status matches: {bossA.IsAlive}");

        Assert.Equal(bossA.CurrentHealth, bossB.CurrentHealth);
        _logger.LogEvent($"✓ Boss health matches: {bossA.CurrentHealth}");

        Assert.Equal(bossA.HasBeenLooted, bossB.HasBeenLooted);
        _logger.LogEvent($"✓ Boss looted status matches: {bossA.HasBeenLooted}");

        // Compare all characters
        foreach (var charA in stateA.Characters.Values)
        {
            var charB = stateB.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == charA.CharacterInstanceId);
            Assert.NotNull(charB);
            Assert.Equal(charA.IsAlive, charB.IsAlive);
            Assert.Equal(charA.CurrentHealth, charB.CurrentHealth);
        }
        _logger.LogEvent($"✓ All character states match");

        _logger.LogSeparator();
        _logger.LogEvent("REPLAY VERIFIED ✓");
        _logger.LogEvent("Transaction log successfully reconstructed identical state");

        _output.WriteLine(_logger.GetLog());
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    private class StoryResult
    {
        public int TransactionCount { get; set; }
        public Dictionary<SagaTransactionType, int> TransactionsByType { get; set; } = new();
        public bool BossDefeated { get; set; }
        public int CharactersSpawned { get; set; }
    }

    private class StoryLogger
    {
        private readonly StringBuilder _log = new();

        public void LogHeader(string header)
        {
            _log.AppendLine();
            _log.AppendLine($"=== {header} ===");
        }

        public void LogEvent(string message)
        {
            _log.AppendLine($"  {message}");
        }

        public void LogSeparator()
        {
            _log.AppendLine();
        }

        public string GetLog() => _log.ToString();
    }
}
