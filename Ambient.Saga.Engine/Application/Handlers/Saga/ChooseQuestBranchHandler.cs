using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Quests;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for ChooseQuestBranchCommand.
/// Validates branch choice and enforces exclusivity for exclusive branch stages.
/// </summary>
internal sealed class ChooseQuestBranchHandler : IRequestHandler<ChooseQuestBranchCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IMediator _mediator;
    private readonly IWorld _world;

    public ChooseQuestBranchHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IMediator mediator,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _mediator = mediator;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(ChooseQuestBranchCommand command, CancellationToken ct)
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

            // Check if quest is active
            if (!currentState.ActiveQuests.TryGetValue(command.QuestRef, out var questState))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Quest '{quest.DisplayName}' is not active");
            }

            // Find the stage
            var stage = quest.Stages?.Stage?.FirstOrDefault(s => s.RefName == command.StageRef);
            if (stage == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Stage '{command.StageRef}' not found in quest");
            }

            // Verify stage has branches
            if (stage.Branches == null || stage.Branches.Branch == null || stage.Branches.Branch.Length == 0)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Stage '{stage.DisplayName}' does not have branches");
            }

            // Find the branch being chosen
            var chosenBranch = stage.Branches.Branch.FirstOrDefault(b => b.RefName == command.BranchRef);
            if (chosenBranch == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Branch '{command.BranchRef}' not found in stage");
            }

            // Check exclusivity - if Exclusive is true (default), only one branch can be chosen
            if (stage.Branches.Exclusive)
            {
                // Check if a branch has already been chosen for this stage.
                // Scoped to the current acceptance: a branch chosen before an
                // abandon + re-accept belongs to the previous run and must not
                // lock this one out.
                var transactions = QuestProgressEvaluator.ScopeToCurrentAcceptance(
                    quest, instance.GetCommittedTransactions());
                var existingBranchChoice = transactions.FirstOrDefault(t =>
                    t.Type == SagaTransactionType.QuestBranchChosen &&
                    t.GetData<string>(TransactionDataKeys.QuestRef) == command.QuestRef &&
                    t.GetData<string>(TransactionDataKeys.StageRef) == command.StageRef);

                if (existingBranchChoice != null)
                {
                    var alreadyChosenBranch = existingBranchChoice.GetData<string>(TransactionDataKeys.BranchRef);
                    return SagaCommandResult.Failure(
                        instance.InstanceId,
                        $"A branch has already been chosen for this stage: '{alreadyChosenBranch}'. " +
                        "This stage has exclusive branches - only one choice is allowed.");
                }
            }

            // Verify we're on the correct stage
            if (questState.CurrentStage != command.StageRef)
            {
                return SagaCommandResult.Failure(
                    instance.InstanceId,
                    $"Cannot choose branch for stage '{command.StageRef}' - current stage is '{questState.CurrentStage}'");
            }

            // Create QuestBranchChosen transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestBranchChosen,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.QuestRef] = command.QuestRef,
                    [TransactionDataKeys.StageRef] = command.StageRef,
                    [TransactionDataKeys.BranchRef] = command.BranchRef,
                    [TransactionDataKeys.DisplayName] = chosenBranch.DisplayName ?? chosenBranch.RefName,
                    [TransactionDataKeys.NextStage] = chosenBranch.NextStage ?? string.Empty
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

            // Automatically advance the stage now that a branch has been chosen
            var advanceResult = await _mediator.Send(new AdvanceQuestStageCommand
            {
                AvatarId = command.AvatarId,
                SagaArcRef = command.SagaArcRef,
                QuestRef = command.QuestRef,
                Avatar = command.Avatar
            }, ct);

            Dictionary<string, object>? resultData = null;
            if (!advanceResult.Successful)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ChooseQuestBranch] AdvanceQuestStage '{command.QuestRef}' failed: {advanceResult.ErrorMessage}");
            }
            // Propagate the game-complete signal from the nested advance (which itself
            // propagates it from CompleteQuest) — the caller only sees this result
            else if (advanceResult.Data.ContainsKey(TransactionDataKeys.GameComplete))
            {
                resultData = new Dictionary<string, object>
                {
                    [TransactionDataKeys.GameComplete] = advanceResult.Data[TransactionDataKeys.GameComplete]
                };
                if (advanceResult.Data.TryGetValue(TransactionDataKeys.CompletionQuestRef, out var completionQuestRef))
                {
                    resultData[TransactionDataKeys.CompletionQuestRef] = completionQuestRef;
                }
            }

            // Return success
            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error choosing quest branch: {ex.Message}");
        }
    }
}
