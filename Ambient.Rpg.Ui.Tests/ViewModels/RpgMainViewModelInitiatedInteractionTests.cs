using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.Partials;
using Ambient.Infrastructure.GameLogic;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Presentation.UI.ViewModels;
using Ambient.Rpg.Ui.Services;
using Ambient.Rpg.Ui.ViewModels;
using MediatR;

namespace Ambient.Rpg.Ui.Tests.ViewModels;

/// <summary>
/// Proximity-initiated (walk-up) dialogue is the sandbox's interaction mechanism:
/// the sandbox subscribes to RpgMainViewModel.DialogueRequested and opens the
/// dialogue modal when the arbiter picks a character. The 3D host games interact
/// by clicking the character's 3D model and do NOT subscribe. The view model must
/// therefore skip the arbiter query entirely when neither DialogueRequested nor
/// AssaultRequested has subscribers — otherwise StartDialogueCommand fires with no
/// UI, leaving the engine dialogue session dangling and _isInDialogue wedged.
///
/// Proximity ASSAULT (AssaultRequested) works in ALL hosts: when the arbiter's
/// winner is effectively Hostile and not Disengaged/Spared (engine-computed
/// IsAssault), the view model raises AssaultRequested and must NOT start a
/// dialogue session — assault goes straight into battle.
/// </summary>
public class RpgMainViewModelInitiatedInteractionTests
{
    #region Test doubles

    /// <summary>
    /// Mediator that records every request. Returns a configurable result for the
    /// arbiter query (default "no interaction"); a failed command result for
    /// StartDialogueCommand (recording is what the tests assert); default for
    /// everything else.
    /// </summary>
    private sealed class RecordingMediator : IMediator
    {
        public List<object> Requests { get; } = new();

        public InitiatedInteractionResult ArbiterResult { get; set; } = new() { HasInteraction = false };

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (request is GetInitiatedInteractionQuery)
            {
                return Task.FromResult((TResponse)(object)ArbiterResult);
            }

            if (request is StartDialogueCommand)
            {
                return Task.FromResult((TResponse)(object)new ArcCommandResult
                {
                    Successful = false,
                    ErrorMessage = "Stubbed by RecordingMediator"
                });
            }

            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        {
            Requests.Add(request!);
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult<object?>(null);
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }

    private sealed class StubWorldRepositoryFactory : IWorldRepositoryFactory
    {
        public WorldRepositories CreateRepositories(string worldRefName, IWorld world, bool isSteamAvailable)
            => throw new NotSupportedException("Not used by these tests");
    }

    private sealed class StubContentPathResolver : IContentPathResolver
    {
        public string? ResolveTexturePath(string library, string ns, string textureName) => null;
        public string? ResolveGeographicDataPath(string library, string ns, string fileName) => null;
        public string? ResolveModelPath(string library, string ns, string modelName) => null;
        public string? ResolveModelPathByCategoryKind(string library, string ns, string category, string? kind, Random? random = null) => null;
        public string? ResolveModelRefByCategoryKind(string library, string ns, string category, string? kind, Random? random = null) => null;
        public string? ResolveXmlPath(string worldRef, string library, string ns, params string[] relativePath) => null;
    }

    private sealed class StubWorldContentGenerator : IWorldContentGenerator
    {
        public bool IsAvailable => false;
        public string StatusMessage => "Stub generator";
        public Task<List<string>> GenerateWorldContentAsync(IWorldConfiguration worldConfig, string outputDirectory)
            => Task.FromResult(new List<string>());
    }

    private sealed class StubAvatarCreationService : IAvatarCreationService
    {
        public Task<AvatarEntity> CreateAvatarAsync(Guid avatarId, AvatarArchetype archetype, IWorld world)
            => Task.FromResult(new AvatarEntity { Id = avatarId, AvatarId = avatarId });
    }

    private sealed class StubArchetypeSelector : IArchetypeSelector
    {
        public Task<AvatarArchetype?> SelectArchetypeAsync(IEnumerable<AvatarArchetype> archetypes, string? currencyName)
            => Task.FromResult<AvatarArchetype?>(null);
    }

    #endregion

    private static RpgMainViewModel CreateReadyViewModel(RecordingMediator mediator)
    {
        var viewModel = new RpgMainViewModel(
            new WorldProvider(),
            new ArcInstanceRepositoryProvider(),
            new AvatarProgressRepositoryProvider(),
            new GameAvatarRepositoryProvider(),
            new WorldStateRepositoryProvider(),
            new StubWorldRepositoryFactory(),
            new StubContentPathResolver(),
            mediator,
            new StubWorldContentGenerator(),
            new StubAvatarCreationService(),
            new StubArchetypeSelector());

        // World + avatar + position available: the only remaining gate on the
        // arbiter check is the DialogueRequested subscription.
        viewModel.CurrentWorld = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents()
            }
        };
        var id = Guid.NewGuid();
        viewModel.Avatar = new AvatarEntity { Id = id, AvatarId = id };
        viewModel.HasAvatarPosition = true;

        return viewModel;
    }

    [Fact]
    public async Task NoDialogueRequestedSubscriber_SkipsArbiterQuery()
    {
        // 3D-game-style host: interaction is clicking the character's model;
        // the host never subscribes DialogueRequested.
        var mediator = new RecordingMediator();
        var viewModel = CreateReadyViewModel(mediator);

        await viewModel.CheckForInitiatedInteractionsAsync();

        Assert.DoesNotContain(mediator.Requests, r => r is GetInitiatedInteractionQuery);
    }

    [Fact]
    public async Task DialogueRequestedSubscriber_EvaluatesArbiter()
    {
        // Sandbox-style host: subscribes DialogueRequested, so proximity arrival
        // must still run the arbiter query.
        var mediator = new RecordingMediator();
        var viewModel = CreateReadyViewModel(mediator);
        viewModel.DialogueRequested += _ => { };

        await viewModel.CheckForInitiatedInteractionsAsync();

        var query = mediator.Requests.OfType<GetInitiatedInteractionQuery>().FirstOrDefault();
        Assert.NotNull(query);
        Assert.Equal(viewModel.Avatar!.AvatarId, query!.AvatarId);
    }

    [Fact]
    public async Task SubscriberPresent_ButNoAvatarPosition_SkipsArbiterQuery()
    {
        // Guard order sanity: even with a subscriber, the existing readiness gates
        // still apply.
        var mediator = new RecordingMediator();
        var viewModel = CreateReadyViewModel(mediator);
        viewModel.DialogueRequested += _ => { };
        viewModel.HasAvatarPosition = false;

        await viewModel.CheckForInitiatedInteractionsAsync();

        Assert.DoesNotContain(mediator.Requests, r => r is GetInitiatedInteractionQuery);
    }

    #region Proximity assault

    /// <summary>
    /// Configures the mediator to return an arbiter winner and publishes the
    /// matching CharacterViewModel on the view model (as the interaction check
    /// loop would have). Returns the character's instance id.
    /// </summary>
    private static Guid SetUpArbiterWinner(
        RecordingMediator mediator,
        RpgMainViewModel viewModel,
        bool isAssault,
        bool canDialogue)
    {
        var instanceId = Guid.NewGuid();
        const string arcRef = "TestArc";

        mediator.ArbiterResult = new InitiatedInteractionResult
        {
            HasInteraction = true,
            ArcRef = arcRef,
            IsAssault = isAssault,
            Character = new InteractableCharacter
            {
                CharacterInstanceId = instanceId,
                CharacterRef = "Raider",
                DisplayName = "Raider",
                Options = new CharacterInteractionOptions
                {
                    IsAssault = isAssault,
                    CanDialogue = canDialogue,
                    CanAttack = isAssault
                }
            }
        };

        viewModel.Characters = new System.Collections.ObjectModel.ObservableCollection<CharacterViewModel>
        {
            new CharacterViewModel
            {
                CharacterInstanceId = instanceId,
                CharacterRef = "Raider",
                DisplayName = "Raider",
                ArcRef = arcRef,
                IsAlive = true
            }
        };

        return instanceId;
    }

    [Fact]
    public async Task AssaultOnlySubscriber_EvaluatesArbiter()
    {
        // 3D-game-style host after the assault feature: subscribes ONLY
        // AssaultRequested (dialogue stays click-only). The arbiter must run.
        var mediator = new RecordingMediator();
        var viewModel = CreateReadyViewModel(mediator);
        viewModel.AssaultRequested += _ => { };

        await viewModel.CheckForInitiatedInteractionsAsync();

        Assert.Contains(mediator.Requests, r => r is GetInitiatedInteractionQuery);
    }

    [Fact]
    public async Task AssaultWinner_RaisesAssaultRequested_WithoutStartingDialogue()
    {
        // Hostile-and-not-Disengaged winner (engine-computed IsAssault): assault goes
        // STRAIGHT into battle — AssaultRequested fires, and no dialogue session is
        // opened even though the character could talk and a dialogue host is present
        // (menace speech is the battle_opening dialogue trigger, not a conversation).
        var mediator = new RecordingMediator();
        var viewModel = CreateReadyViewModel(mediator);
        var instanceId = SetUpArbiterWinner(mediator, viewModel, isAssault: true, canDialogue: true);

        CharacterViewModel? assaulted = null;
        var dialogueRequested = false;
        viewModel.AssaultRequested += c => assaulted = c;
        viewModel.DialogueRequested += _ => dialogueRequested = true;

        await viewModel.CheckForInitiatedInteractionsAsync();

        Assert.NotNull(assaulted);
        Assert.Equal(instanceId, assaulted!.CharacterInstanceId);
        Assert.Equal("TestArc", assaulted.ArcRef);
        Assert.False(dialogueRequested);
        Assert.DoesNotContain(mediator.Requests, r => r is StartDialogueCommand);
    }

    [Fact]
    public async Task DisengagedHostileWinner_NoAssault_DialogueProceedsAsBefore()
    {
        // A fled-from enemy keeps Hostile but gains Disengaged, so the engine reports
        // IsAssault = false. No assault fires; the normal walk-up dialogue path runs
        // for hosts that subscribe DialogueRequested.
        var mediator = new RecordingMediator();
        var viewModel = CreateReadyViewModel(mediator);
        SetUpArbiterWinner(mediator, viewModel, isAssault: false, canDialogue: true);

        var assaultRequested = false;
        viewModel.AssaultRequested += _ => assaultRequested = true;
        viewModel.DialogueRequested += _ => { };

        await viewModel.CheckForInitiatedInteractionsAsync();

        Assert.False(assaultRequested);
        Assert.Contains(mediator.Requests, r => r is StartDialogueCommand);
    }

    [Fact]
    public async Task FriendlyWinner_DialogueSubscriber_StartsDialogueAsBefore()
    {
        // Regression guard: the pre-assault behavior is unchanged for friendly
        // characters in dialogue-subscribing hosts.
        var mediator = new RecordingMediator();
        var viewModel = CreateReadyViewModel(mediator);
        SetUpArbiterWinner(mediator, viewModel, isAssault: false, canDialogue: true);

        viewModel.DialogueRequested += _ => { };

        await viewModel.CheckForInitiatedInteractionsAsync();

        Assert.Contains(mediator.Requests, r => r is StartDialogueCommand);
    }

    [Fact]
    public async Task FriendlyWinner_AssaultOnlySubscriber_DoesNotStartDialogue()
    {
        // A 3D-game host subscribes only AssaultRequested. A non-assault winner must
        // NOT open a dialogue session nobody will show (dialogue is click-only there).
        var mediator = new RecordingMediator();
        var viewModel = CreateReadyViewModel(mediator);
        SetUpArbiterWinner(mediator, viewModel, isAssault: false, canDialogue: true);

        var assaultRequested = false;
        viewModel.AssaultRequested += _ => assaultRequested = true;

        await viewModel.CheckForInitiatedInteractionsAsync();

        Assert.False(assaultRequested);
        Assert.DoesNotContain(mediator.Requests, r => r is StartDialogueCommand);
    }

    [Fact]
    public async Task AssaultWinner_NoAssaultSubscriber_FallsBackToDialoguePath()
    {
        // A host that only shows walk-up dialogue (upstream sandbox before it wires
        // AssaultRequested) keeps its pre-assault behavior: the hostile winner
        // starts dialogue as before.
        var mediator = new RecordingMediator();
        var viewModel = CreateReadyViewModel(mediator);
        SetUpArbiterWinner(mediator, viewModel, isAssault: true, canDialogue: true);

        viewModel.DialogueRequested += _ => { };

        await viewModel.CheckForInitiatedInteractionsAsync();

        Assert.Contains(mediator.Requests, r => r is StartDialogueCommand);
    }

    #endregion
}
