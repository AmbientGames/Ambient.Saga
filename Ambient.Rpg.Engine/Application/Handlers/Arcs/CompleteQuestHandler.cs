﻿using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain.Quests;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for CompleteQuestCommand.
/// Creates QuestCompleted transaction, awards rewards, and updates avatar's quest log.
/// </summary>
internal sealed class CompleteQuestHandler : IRequestHandler<CompleteQuestCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public CompleteQuestHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarUpdateService = avatarUpdateService;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(CompleteQuestCommand command, CancellationToken ct)
    {
        try
        {
            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(command.QuestRef);
            if (quest == null)
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Quest '{command.QuestRef}' not found");
            }

            // A CompleteQuest dialogue action can fire from an NPC whose Arc did not issue
            // the quest. Resolve the arc instance that actually holds the active quest so
            // the QuestCompleted transaction lands on the log that records its lifecycle.
            var instance = await QuestInstanceLocator.ResolveActiveQuestInstanceAsync(
                command.AvatarId, command.QuestRef, command.ArcRef, _instanceRepository, _world, ct);

            if (instance == null)
            {
                // Not active anywhere — disambiguate "already done" vs. "never accepted" using
                // the caller-specified arc, since that's the best contextual guess available.
                // Template lookups use the stripped ref (dev instances are "Real__DEV__id").
                var hintedLookupRef = QuestInstanceLocator.StripDevSuffix(command.ArcRef);
                var hinted = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);
                if (_world.ArcLookup.TryGetValue(hintedLookupRef, out var hintedTemplate)
                    && _world.ArcTriggersLookup.TryGetValue(hintedLookupRef, out var hintedTriggers))
                {
                    var hintedState = new ArcStateMachine(hintedTemplate, hintedTriggers, _world).ReplayToNow(hinted);
                    if (hintedState.CompletedQuests.Contains(command.QuestRef))
                    {
                        return ArcCommandResult.Failure(hinted.InstanceId, $"Quest '{quest.DisplayName}' already completed");
                    }
                }
                return ArcCommandResult.Failure(hinted.InstanceId, $"Quest '{quest.DisplayName}' not accepted");
            }

            var resolvedArcRef = instance.ArcRef;
            var resolvedLookupRef = QuestInstanceLocator.StripDevSuffix(resolvedArcRef);

            // Verify Arc exists (for the resolved instance; stripped ref for template lookup)
            if (!_world.ArcLookup.TryGetValue(resolvedLookupRef, out var arcTemplate))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Arc '{resolvedLookupRef}' not found");
            }

            // Get expanded triggers for state machine
            if (!_world.ArcTriggersLookup.TryGetValue(resolvedLookupRef, out var expandedTriggers))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Triggers not found for Arc '{resolvedLookupRef}'");
            }

            // Replay to get current state
            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Resolver guaranteed ActiveQuests contains the quest — safe lookup.
            var questState = currentState.ActiveQuests[command.QuestRef];

            // NEW: Check if quest is ready for completion (all stages done)
            // In the new multi-stage system, CurrentStage will be empty when all stages are complete
            if (!command.DialogueDriven && !string.IsNullOrEmpty(questState.CurrentStage))
            {
                return ArcCommandResult.Failure(
                    instance.InstanceId,
                    $"Quest '{quest.DisplayName}' not complete - still on stage '{questState.CurrentStage}'");
            }

            // Create QuestCompleted transaction
            var transactionData = new Dictionary<string, string>
            {
                [TransactionDataKeys.QuestRef] = command.QuestRef,
                [TransactionDataKeys.QuestDisplayName] = quest.DisplayName,
                [TransactionDataKeys.QuestReceiverRef] = command.QuestReceiverRef,
                [TransactionDataKeys.ArcRef] = resolvedArcRef,
                [TransactionDataKeys.CompletedAt] = DateTime.UtcNow.ToString("O")
            };

            // NEW: Include branch choice if quest had branches
            if (!string.IsNullOrEmpty(questState.ChosenBranch))
            {
                transactionData[TransactionDataKeys.ChosenBranch] = questState.ChosenBranch;
            }

            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.QuestCompleted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = transactionData
            };

            // Reputation rewards are transaction-driven (replayed arc state +
            // cross-arc projection, like the dialogue ChangeReputation action) —
            // stage them so they commit atomically with QuestCompleted
            var transactionsToCommit = new List<ArcTransaction> { transaction };
            transactionsToCommit.AddRange(QuestRewardDistributor.CollectRewardTransactions(
                quest.Rewards,
                QuestRewardCondition.OnSuccess,
                command.AvatarId.ToString(),
                instance.InstanceId,
                _world));
            if (!string.IsNullOrEmpty(questState.ChosenBranch))
            {
                transactionsToCommit.AddRange(QuestRewardDistributor.CollectRewardTransactions(
                    quest.Rewards,
                    QuestRewardCondition.OnBranch,
                    command.AvatarId.ToString(),
                    instance.InstanceId,
                    _world,
                    branchRef: questState.ChosenBranch));
            }

            foreach (var tx in transactionsToCommit)
            {
                instance.AddTransaction(tx);
            }

            // Persist and commit transactions atomically
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                transactionsToCommit,
                ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache for the arc that actually owns the quest
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, resolvedArcRef, ct);

            // Distribute quest completion rewards
            if (quest.Rewards != null && quest.Rewards.Length > 0)
            {
                // Snapshot before distributing: if persistence fails, the in-memory
                // avatar must be restored, or a later periodic save would persist
                // rewards the ledger says were reversed (audit M1 — the
                // reverse-on-persist-failure compensation, same pattern as trade).
                var rewardSnapshot = QuestRewardDistributor.CaptureRewardSnapshot(command.Avatar);

                // Award OnSuccess rewards (quest completed successfully)
                QuestRewardDistributor.DistributeRewards(
                    quest.Rewards,
                    QuestRewardCondition.OnSuccess,
                    command.Avatar,
                    _world);

                // If quest had branches, also award OnBranch rewards for the chosen branch
                if (!string.IsNullOrEmpty(questState.ChosenBranch))
                {
                    QuestRewardDistributor.DistributeRewards(
                        quest.Rewards,
                        QuestRewardCondition.OnBranch,
                        command.Avatar,
                        _world,
                        branchRef: questState.ChosenBranch);
                }

                // Persist avatar with new rewards; on failure, compensate: restore the
                // in-memory avatar and record the reversal against the completion.
                try
                {
                    await _avatarUpdateService.PersistAvatarAsync(command.Avatar, ct);
                }
                catch (Exception persistEx)
                {
                    QuestRewardDistributor.RestoreRewardSnapshot(command.Avatar, rewardSnapshot);

                    var reversalTransaction = new ArcTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        Type = ArcTransactionType.TransactionReversed,
                        AvatarId = command.AvatarId.ToString(),
                        Status = TransactionStatus.Pending,
                        LocalTimestamp = DateTime.UtcNow,
                        Data = new Dictionary<string, string>
                        {
                            [TransactionDataKeys.ReversedTransactionId] = transaction.TransactionId.ToString(),
                            [TransactionDataKeys.Reason] = $"Avatar persistence failed: {persistEx.Message}",
                            [TransactionDataKeys.OriginalType] = transaction.Type.ToString()
                        }
                    };

                    instance.AddTransaction(reversalTransaction);
                    await _instanceRepository.AddAndCommitTransactionsAsync(
                        instance.InstanceId,
                        new List<ArcTransaction> { reversalTransaction },
                        ct);

                    await _readModelRepository.InvalidateCacheAsync(command.AvatarId, resolvedArcRef, ct);

                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Quest completed but reward persistence failed: {persistEx.Message}");
                }
            }

            // Check if this quest completion ends the game
            var completionRef = _world.WorldConfiguration?.CompletionQuestRef;
            Dictionary<string, object>? resultData = null;
            if (!string.IsNullOrEmpty(completionRef) && command.QuestRef == completionRef)
            {
                resultData = new Dictionary<string, object> { [TransactionDataKeys.GameComplete] = true, [TransactionDataKeys.CompletionQuestRef] = completionRef };
            }

            return ArcCommandResult.Success(
                instance.InstanceId,
                transactionsToCommit.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error completing quest: {ex.Message}");
        }
    }
}
