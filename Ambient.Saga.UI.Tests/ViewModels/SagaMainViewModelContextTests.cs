using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.Partials;
using Ambient.Infrastructure.GameLogic;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI.Services;
using MediatR;

namespace Ambient.Saga.UI.Tests.ViewModels;

/// <summary>
/// Tests that SagaMainViewModel populates the shared SagaInteractionContext when the
/// world and avatar become available, so QuestLogViewModel/AchievementViewModel stop
/// early-returning (the "quest log permanently empty" bug: the context was created
/// with World = null! / AvatarEntity = null! and never updated).
/// </summary>
public class SagaMainViewModelContextTests
{
    #region Test doubles

    /// <summary>
    /// Mediator that records every request it receives.
    /// Returns an empty SagaState for GetSagaStateQuery; default for everything else.
    /// </summary>
    private sealed class RecordingMediator : IMediator
    {
        public List<object> Requests { get; } = new();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (request is GetSagaStateQuery query)
            {
                var state = new SagaState
                {
                    SagaRef = query.SagaRef,
                    Status = SagaStatus.Active
                };
                return Task.FromResult((TResponse)(object)state);
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

    private static SagaMainViewModel CreateViewModel(RecordingMediator mediator)
    {
        return new SagaMainViewModel(
            new WorldProvider(),
            new SagaInstanceRepositoryProvider(),
            new AvatarProgressRepositoryProvider(),
            new GameAvatarRepositoryProvider(),
            new WorldStateRepositoryProvider(),
            new StubWorldRepositoryFactory(),
            new StubContentPathResolver(),
            mediator,
            new StubWorldContentGenerator(),
            new StubAvatarCreationService(),
            new StubArchetypeSelector());
    }

    private static IWorld CreateTestWorld()
    {
        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents()
            }
        };

        world.Gameplay.SagaArcs = new[]
        {
            new SagaArc { RefName = "TestSaga" }
        };

        return world;
    }

    private static AvatarEntity CreateTestAvatar()
    {
        var id = Guid.NewGuid();
        return new AvatarEntity
        {
            Id = id,
            AvatarId = id
        };
    }

    [Fact]
    public async Task RefreshQuests_BeforeWorldAndAvatarAvailable_DoesNotQuerySagaState()
    {
        var mediator = new RecordingMediator();
        var viewModel = CreateViewModel(mediator);

        await viewModel.QuestLog!.RefreshQuestsAsync();

        Assert.DoesNotContain(mediator.Requests, r => r is GetSagaStateQuery);
    }

    [Fact]
    public async Task RefreshQuests_AfterWorldAndAvatarAvailable_ReachesMediator()
    {
        var mediator = new RecordingMediator();
        var viewModel = CreateViewModel(mediator);
        var avatar = CreateTestAvatar();

        // Simulate world + avatar becoming available (InitializeWorldCoreAsync sets
        // CurrentWorld; LoadOrCreateAvatarAsync sets Avatar). The property setters
        // must populate the shared SagaInteractionContext the QuestLog reads.
        viewModel.CurrentWorld = CreateTestWorld();
        viewModel.Avatar = avatar;

        await viewModel.QuestLog!.RefreshQuestsAsync();

        var query = mediator.Requests.OfType<GetSagaStateQuery>().FirstOrDefault();
        Assert.NotNull(query);
        Assert.Equal("TestSaga", query!.SagaRef);
        Assert.Equal(avatar.Id, query.AvatarId);
    }

    [Fact]
    public async Task RefreshQuests_AfterWorldSwitch_QueriesNewWorldsSagas()
    {
        var mediator = new RecordingMediator();
        var viewModel = CreateViewModel(mediator);

        viewModel.CurrentWorld = CreateTestWorld();
        viewModel.Avatar = CreateTestAvatar();

        // Switch to a second world — the context must track the new world
        var secondWorld = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents()
            }
        };
        secondWorld.Gameplay.SagaArcs = new[] { new SagaArc { RefName = "SecondWorldSaga" } };
        viewModel.CurrentWorld = secondWorld;

        mediator.Requests.Clear();
        await viewModel.QuestLog!.RefreshQuestsAsync();

        var query = mediator.Requests.OfType<GetSagaStateQuery>().FirstOrDefault();
        Assert.NotNull(query);
        Assert.Equal("SecondWorldSaga", query!.SagaRef);
    }
}
