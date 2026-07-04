using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.AvatarProgress;
using Ambient.Saga.Engine.Domain.Rpg.Quests;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for AcceptQuestCommand.
/// Creates QuestAccepted transaction and adds quest to avatar's active quest log.
/// </summary>
internal sealed class AcceptQuestHandler : IRequestHandler<AcceptQuestCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarProgressRepository _avatarProgressRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public AcceptQuestHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IAvatarProgressRepository avatarProgressRepository,
        IAvatarUpdateService avatarUpdateService,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarProgressRepository = avatarProgressRepository;
        _avatarUpdateService = avatarUpdateService;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(AcceptQuestCommand command, CancellationToken ct)
    {
        try
        {
            // Handle dev saga refs (format: "RealSagaRef__DEV__uniqueid")
            var sagaRefForLookup = command.SagaArcRef;
            var devSuffix = "__DEV__";
            if (command.SagaArcRef.Contains(devSuffix))
            {
                sagaRefForLookup = command.SagaArcRef.Substring(0, command.SagaArcRef.IndexOf(devSuffix));
            }

            // Get Saga instance (use full ref with DEV suffix for unique instance)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Verify Saga exists (use stripped ref for template lookup)
            if (!_world.SagaArcLookup.TryGetValue(sagaRefForLookup, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Saga '{sagaRefForLookup}' not found");
            }

            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(command.QuestRef);
            if (quest == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Quest '{command.QuestRef}' not found");
            }

            // Get expanded triggers for state machine (use stripped ref for lookup)
            if (!_world.SagaTriggersLookup.TryGetValue(sagaRefForLookup, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{sagaRefForLookup}'");
            }

            // Replay to get current state
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Check if quest already accepted
            if (currentState.ActiveQuests.ContainsKey(command.QuestRef))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Quest '{quest.DisplayName}' already accepted");
            }

            // Check if quest already completed
            if (currentState.CompletedQuests.Contains(command.QuestRef))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Quest '{quest.DisplayName}' already completed");
            }

            // Check quest prerequisites. The replayed state only sees THIS arc's
            // history, so merge in the cross-arc projection: a quest completed or
            // reputation earned in a different arc must satisfy prerequisites here
            // too (same seam as the cross-arc quest-token fix).
            var completedQuests = new HashSet<string>(currentState.CompletedQuests);
            Dictionary<string, int>? factionReputation = null;
            foreach (var prereq in quest.Prerequisites ?? Array.Empty<QuestPrerequisite>())
            {
                if (!string.IsNullOrEmpty(prereq.QuestRef) &&
                    _avatarProgressRepository.GetQuestStatus(command.AvatarId, prereq.QuestRef) == QuestProgressStatus.Completed)
                {
                    completedQuests.Add(prereq.QuestRef);
                }

                if (!string.IsNullOrEmpty(prereq.FactionRef))
                {
                    // Reputation is the faction's StartingReputation baseline plus earned
                    // deltas (same composition as DirectDialogueStateProvider). Arc-local
                    // and cross-arc totals overlap when this arc's transactions have been
                    // projected, so take the larger earned value rather than summing.
                    var baseline = _world.FactionsLookup.TryGetValue(prereq.FactionRef, out var faction)
                        ? faction.StartingReputation
                        : 0;
                    var earned = Math.Max(
                        currentState.FactionReputation.GetValueOrDefault(prereq.FactionRef, 0),
                        _avatarProgressRepository.GetFactionReputation(command.AvatarId, prereq.FactionRef));
                    factionReputation ??= new Dictionary<string, int>();
                    factionReputation[prereq.FactionRef] = baseline + earned;
                }
            }

            // The avatar's persisted Achievements list is the unlock ledger
            // (see AvatarUpdateService.GetAchievementInstancesAsync)
            var unlockedAchievements = command.Avatar.Achievements?
                .Where(a => !string.IsNullOrEmpty(a.AchievementRef))
                .Select(a => a.AchievementRef)
                .ToHashSet();

            var (canAccept, prerequisiteReason) = QuestRewardDistributor.CheckPrerequisites(
                quest,
                command.Avatar,
                _world,
                completedQuests,
                factionReputation,
                unlockedAchievements,
                awardedQuestTokens: _avatarProgressRepository.GetAllQuestTokens(command.AvatarId));

            if (!canAccept)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Cannot accept quest: {prerequisiteReason}");
            }

            // Create QuestAccepted transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestAccepted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.QuestRef] = command.QuestRef,
                    [TransactionDataKeys.QuestDisplayName] = quest.DisplayName,
                    [TransactionDataKeys.QuestGiverRef] = command.QuestGiverRef,
                    [TransactionDataKeys.SagaArcRef] = command.SagaArcRef
                }
            };

            instance.AddTransaction(transaction);

            // Persist and commit transaction atomically
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { transaction },
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Quest progress is event-sourced from SagaState - no avatar entity update needed
            // Avatar queries active quests via GetActiveQuestsQuery

            // Return success (pure CQRS - no state data)
            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error accepting quest: {ex.Message}");
        }
    }
}
