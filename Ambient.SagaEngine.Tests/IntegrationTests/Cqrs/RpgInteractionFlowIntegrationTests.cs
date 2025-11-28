using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.SagaEngine.Application.Behaviors;
using Ambient.SagaEngine.Application.Commands.Saga;
using Ambient.SagaEngine.Application.Queries.Saga;
using Ambient.SagaEngine.Application.ReadModels;
using Ambient.SagaEngine.Application.Services;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Contracts.Services;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.SagaEngine.Domain.Rpg.Trade;
using Ambient.SagaEngine.Infrastructure.Persistence;
using Ambient.SagaEngine.Tests.Helpers;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ambient.SagaEngine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for the complete RPG interaction flow:
/// - Dialogue → Trait Assignment → Trade Discounts
/// - Mid-Battle Dialogue → Surrender/Befriend
/// - Health-Based Dialogue Branching
/// </summary>
[Collection("Sequential CQRS Tests")]
public class RpgInteractionFlowIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly World _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;
    private readonly Guid _testAvatarId = Guid.NewGuid();

    public RpgInteractionFlowIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateRpgTestWorld();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<StartDialogueCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        services.AddSingleton(_world);
        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IGameAvatarRepository, FakeAvatarRepository>();
        services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
    }

    #region Test 1: Dialogue Assigns Friendly Trait → 10% Trade Discount

    [Fact]
    public async Task DialogueAssignsFriendlyTrait_TradePricesShowDiscount()
    {
        // ARRANGE: Spawn merchant character
        var avatar = CreateTestAvatar();
        var sagaRef = "MerchantSaga";

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, sagaRef);
        var merchantInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        _output.WriteLine($"Merchant spawned: {merchantInstanceId}");

        // ACT: Start dialogue and select choice that assigns "Friendly" trait
        var dialogueResult = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            CharacterInstanceId = merchantInstanceId,
            Avatar = avatar
        });

        Assert.True(dialogueResult.Successful);

        // Select the "friendly" choice (identified by NextNodeId)
        var choiceResult = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            CharacterInstanceId = merchantInstanceId,
            ChoiceId = "friendly_response",  // ChoiceId = NextNodeId
            Avatar = avatar
        });

        Assert.True(choiceResult.Successful);

        // ASSERT: Verify trait was assigned
        var sagaState = await _mediator.Send(new GetSagaStateQuery
        {
            AvatarId = _testAvatarId,
            SagaRef = sagaRef
        });

        Assert.NotNull(sagaState);
        Assert.True(sagaState.CharacterTraits.ContainsKey("FriendlyMerchant"));
        Assert.Contains("Friendly", sagaState.CharacterTraits["FriendlyMerchant"]);

        _output.WriteLine($"✓ Friendly trait assigned to merchant");

        // Verify trade prices show 10% discount
        var tradeEngine = new TradeEngine(_world);

        var basePrice = tradeEngine.CalculateBuyPrice(
            _world.EquipmentLookup["IronSword"],
            isMerchant: true,
            characterTraits: null);

        var discountedPrice = tradeEngine.CalculateBuyPrice(
            _world.EquipmentLookup["IronSword"],
            isMerchant: true,
            characterTraits: new List<string> { "Friendly" });

        var expectedDiscountedPrice = (int)(basePrice * 0.9);

        _output.WriteLine($"Base price: {basePrice}, Discounted price: {discountedPrice}, Expected: {expectedDiscountedPrice}");
        Assert.Equal(expectedDiscountedPrice, discountedPrice);
    }

    #endregion

    #region Test 2: Dialogue Assigns TradeDiscount Trait → 20% Discount

    [Fact]
    public async Task DialogueAssignsTradeDiscountTrait_TradePricesShow20PercentDiscount()
    {
        // ARRANGE: Spawn merchant
        var avatar = CreateTestAvatar();
        var sagaRef = "MerchantSaga";

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, sagaRef);
        var merchantInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        // ACT: Start dialogue and select choice that leads to TradeDiscount trait
        await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            CharacterInstanceId = merchantInstanceId,
            Avatar = avatar
        });

        var choiceResult = await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            CharacterInstanceId = merchantInstanceId,
            ChoiceId = "discount_response",  // ChoiceId = NextNodeId
            Avatar = avatar
        });

        Assert.True(choiceResult.Successful);

        // ASSERT: Verify trait assigned
        var sagaState = await _mediator.Send(new GetSagaStateQuery
        {
            AvatarId = _testAvatarId,
            SagaRef = sagaRef
        });

        Assert.True(sagaState.CharacterTraits.ContainsKey("FriendlyMerchant"));
        Assert.Contains("TradeDiscount", sagaState.CharacterTraits["FriendlyMerchant"]);

        // Verify 20% discount
        var tradeEngine = new TradeEngine(_world);
        var basePrice = tradeEngine.CalculateBuyPrice(
            _world.EquipmentLookup["IronSword"],
            isMerchant: true,
            characterTraits: null);

        var discountedPrice = tradeEngine.CalculateBuyPrice(
            _world.EquipmentLookup["IronSword"],
            isMerchant: true,
            characterTraits: new List<string> { "TradeDiscount" });

        var expectedDiscountedPrice = (int)(basePrice * 0.8);

        _output.WriteLine($"Base: {basePrice}, Discounted: {discountedPrice}, Expected: {expectedDiscountedPrice}");
        Assert.Equal(expectedDiscountedPrice, discountedPrice);
    }

    #endregion

    #region Test 3: Both Traits Stack → 28% Discount

    [Fact]
    public async Task BothTraitsAssigned_PricesShowStackedDiscount()
    {
        // ARRANGE: Spawn merchant
        var avatar = CreateTestAvatar();
        var sagaRef = "MerchantSaga";

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(_testAvatarId, sagaRef);
        var merchantInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        // ACT: Assign Friendly trait first
        await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            CharacterInstanceId = merchantInstanceId,
            Avatar = avatar
        });

        await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            CharacterInstanceId = merchantInstanceId,
            ChoiceId = "friendly_response",
            Avatar = avatar
        });

        // Continue dialogue and assign TradeDiscount trait
        await _mediator.Send(new SelectDialogueChoiceCommand
        {
            AvatarId = _testAvatarId,
            SagaArcRef = sagaRef,
            CharacterInstanceId = merchantInstanceId,
            ChoiceId = "discount_response",
            Avatar = avatar
        });

        // ASSERT: Verify both traits present
        var sagaState = await _mediator.Send(new GetSagaStateQuery
        {
            AvatarId = _testAvatarId,
            SagaRef = sagaRef
        });

        Assert.True(sagaState.CharacterTraits.ContainsKey("FriendlyMerchant"));
        Assert.Contains("Friendly", sagaState.CharacterTraits["FriendlyMerchant"]);
        Assert.Contains("TradeDiscount", sagaState.CharacterTraits["FriendlyMerchant"]);

        // Verify 28% stacked discount (0.9 * 0.8 = 0.72)
        var tradeEngine = new TradeEngine(_world);
        var basePrice = tradeEngine.CalculateBuyPrice(
            _world.EquipmentLookup["IronSword"],
            isMerchant: true,
            characterTraits: null);

        var discountedPrice = tradeEngine.CalculateBuyPrice(
            _world.EquipmentLookup["IronSword"],
            isMerchant: true,
            characterTraits: new List<string> { "Friendly", "TradeDiscount" });

        var expectedDiscountedPrice = (int)(basePrice * 0.72);

        _output.WriteLine($"Base: {basePrice}, Stacked Discount: {discountedPrice}, Expected: {expectedDiscountedPrice}");
        Assert.Equal(expectedDiscountedPrice, discountedPrice);
    }

    #endregion

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region Test World Setup

    private World CreateRpgTestWorld()
    {
        var merchantSaga = new SagaArc
        {
            RefName = "MerchantSaga",
            DisplayName = "Friendly Merchant",
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            Y = 50.0
        };

        var merchantTrigger = new SagaTrigger
        {
            RefName = "MerchantTrigger",
            EnterRadius = 100.0f,
            TriggerType = SagaTriggerType.SpawnPassive,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    ItemElementName = ItemChoiceType.CharacterRef,
                    Item = "FriendlyMerchant"
                }
            }
        };

        // Dialogue tree with trait assignment
        // Key points:
        // - Text is string[], not DialogueText[]
        // - Actions are on the TARGET node, not on choices
        // - Choices are identified by NextNodeId (no Id property)
        var dialogueTree = new DialogueTree
        {
            RefName = "MerchantDialogue",
            StartNodeId = "greeting",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "greeting",
                    Text = new[] { "Welcome, traveler!" },
                    Choice = new[]
                    {
                        new DialogueChoice
                        {
                            Text = "You seem friendly!",
                            NextNodeId = "friendly_response"
                        },
                        new DialogueChoice
                        {
                            Text = "Can I get a discount?",
                            NextNodeId = "discount_response"
                        }
                    }
                },
                new DialogueNode
                {
                    NodeId = "friendly_response",
                    Text = new[] { "Thank you! I'll remember your kindness." },
                    Action = new[]
                    {
                        new DialogueAction
                        {
                            Type = DialogueActionType.AssignTrait,
                            Trait = CharacterTraitType.Friendly,
                            TraitSpecified = true
                        }
                    },
                    Choice = new[]
                    {
                        new DialogueChoice
                        {
                            Text = "Can I get a discount too?",
                            NextNodeId = "discount_response"
                        }
                    }
                },
                new DialogueNode
                {
                    NodeId = "discount_response",
                    Text = new[] { "Of course! You've earned it." },
                    Action = new[]
                    {
                        new DialogueAction
                        {
                            Type = DialogueActionType.AssignTrait,
                            Trait = CharacterTraitType.TradeDiscount,
                            TraitSpecified = true
                        }
                    },
                    Choice = Array.Empty<DialogueChoice>()
                }
            }
        };

        var merchant = new Character
        {
            RefName = "FriendlyMerchant",
            DisplayName = "Friendly Merchant",
            Interactable = new Interactable
            {
                DialogueTreeRef = "MerchantDialogue",
                Loot = new ItemCollection
                {
                    Equipment = new[]
                    {
                        new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f },
                        new EquipmentEntry { EquipmentRef = "LeatherArmor", Condition = 1.0f }
                    }
                }
            },
            Stats = new CharacterStats
            {
                Health = 1.0f
            }
        };

        var ironSword = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            WholesalePrice = 100,
            MerchantMarkupMultiplier = 1.5f,
            SlotRef = "RightHand"
        };

        var leatherArmor = new Equipment
        {
            RefName = "LeatherArmor",
            DisplayName = "Leather Armor",
            WholesalePrice = 150,
            MerchantMarkupMultiplier = 1.5f,
            SlotRef = "Chest"
        };

        var world = new World
        {
            IsProcedural = true,
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "RpgTestWorld",
                SpawnLatitude = 35.0,
                SpawnLongitude = 139.0,
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
                    SagaArcs = new[] { merchantSaga },
                    Characters = new[] { merchant },
                    Equipment = new[] { ironSword, leatherArmor },
                    DialogueTrees = new[] { dialogueTree },
                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    Consumables = Array.Empty<Consumable>()
                },
                //Simulation = new SimulationComponents(),
                //Presentation = new PresentationComponents()
            }
        };

        // Populate lookups
        world.SagaArcLookup[merchantSaga.RefName] = merchantSaga;
        world.SagaTriggersLookup[merchantSaga.RefName] = new List<SagaTrigger> { merchantTrigger };
        world.CharactersLookup[merchant.RefName] = merchant;
        world.EquipmentLookup[ironSword.RefName] = ironSword;
        world.EquipmentLookup[leatherArmor.RefName] = leatherArmor;
        world.DialogueTreesLookup[dialogueTree.RefName] = dialogueTree;

        return world;
    }

    private AvatarEntity CreateTestAvatar()
    {
        return new AvatarEntity
        {
            Id = _testAvatarId,
            AvatarId = _testAvatarId,
            DisplayName = "Test Hero",
            Stats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f
            },
            Capabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            }
        };
    }

    #endregion
}
