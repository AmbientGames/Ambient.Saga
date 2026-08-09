using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain.AvatarProgress;
using Ambient.Rpg.Engine.Domain.Quests;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for AcceptQuestCommand.
/// Creates QuestAccepted transaction and adds quest to avatar's active quest log.
/// </summary>
internal sealed class AcceptQuestHandler : IRequestHandler<AcceptQuestCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarProgressRepository _avatarProgressRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public AcceptQuestHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
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

    public async Task<ArcCommandResult> Handle(AcceptQuestCommand command, CancellationToken ct)
    {
        try
        {
            // Handle dev arc refs (format: "RealArcRef__DEV__uniqueid")
            var arcRefForLookup = command.ArcRef;
            var devSuffix = "__DEV__";
            if (command.ArcRef.Contains(devSuffix))
            {
                arcRefForLookup = command.ArcRef.Substring(0, command.ArcRef.IndexOf(devSuffix));
            }

            // Get Arc instance (use full ref with DEV suffix for unique instance)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Verify Arc exists (use stripped ref for template lookup)
            if (!_world.ArcLookup.TryGetValue(arcRefForLookup, out var arcTemplate))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Arc '{arcRefForLookup}' not found");
            }

            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(command.QuestRef);
            if (quest == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Quest '{command.QuestRef}' not found");
            }

            // Get expanded triggers for state machine (use stripped ref for lookup)
            if (!_world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var expandedTriggers))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Triggers not found for Arc '{arcRefForLookup}'");
            }

            // Replay to get current state
            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Check if quest already accepted
            if (currentState.ActiveQuests.ContainsKey(command.QuestRef))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Quest '{quest.DisplayName}' already accepted");
            }

            // Check if quest already completed
            if (currentState.CompletedQuests.Contains(command.QuestRef))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Quest '{quest.DisplayName}' already completed");
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
                return ArcCommandResult.Failure(instance.InstanceId, $"Cannot accept quest: {prerequisiteReason}");
            }

            // Create QuestAccepted transaction
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.QuestAccepted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.QuestRef] = command.QuestRef,
                    [TransactionDataKeys.QuestDisplayName] = quest.DisplayName,
                    [TransactionDataKeys.QuestGiverRef] = command.QuestGiverRef,
                    [TransactionDataKeys.ArcRef] = command.ArcRef
                }
            };

            instance.AddTransaction(transaction);

            // Persist and commit transaction atomically
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<ArcTransaction> { transaction },
                ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            // Quest progress is event-sourced from ArcState - no avatar entity update needed
            // Avatar queries active quests via GetActiveQuestsQuery

            // Return success (pure CQRS - no state data)
            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error accepting quest: {ex.Message}");
        }
    }
}
