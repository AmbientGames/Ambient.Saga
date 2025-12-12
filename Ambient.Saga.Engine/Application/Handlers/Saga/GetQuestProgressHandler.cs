using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Quests;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetQuestProgressQuery.
/// Evaluates quest progress by querying transaction logs.
/// </summary>
internal sealed class GetQuestProgressHandler : IRequestHandler<GetQuestProgressQuery, QuestProgressSnapshot?>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public GetQuestProgressHandler(
        ISagaInstanceRepository instanceRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<QuestProgressSnapshot?> Handle(GetQuestProgressQuery query, CancellationToken ct)
    {
        try
        {
            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Verify Saga exists
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return null;
            }

            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(query.QuestRef);
            if (quest == null)
            {
                return null;
            }

            // Get expanded triggers for state machine
            if (!_world.SagaTriggersLookup.TryGetValue(query.SagaRef, out var expandedTriggers))
            {
                return null;
            }

            // Replay to get current state
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Check if quest is active or completed
            QuestState? questState = null;
            var isCompleted = false;

            if (currentState.ActiveQuests.TryGetValue(query.QuestRef, out questState))
            {
                isCompleted = false;
            }
            else if (currentState.CompletedQuests.Contains(query.QuestRef))
            {
                // Quest completed - return snapshot showing completion
                return new QuestProgressSnapshot
                {
                    QuestRef = query.QuestRef,
                    DisplayName = quest.DisplayName,
                    CurrentStageDisplayName = "Complete",
                    Objectives = new List<ObjectiveProgress>(),
                    IsComplete = true,
                    IsSuccess = true,
                    IsFailed = false,
                    OverallProgress = 1.0f
                };
            }
            else
            {
                // Quest not started
                return null;
            }

            // Build progress snapshot from quest state
            var snapshot = BuildProgressSnapshot(quest, questState, instance.GetCommittedTransactions());

            return snapshot;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private QuestProgressSnapshot BuildProgressSnapshot(
        Quest quest,
        QuestState questState,
        List<SagaTransaction> transactions)
    {
        var snapshot = new QuestProgressSnapshot
        {
            QuestRef = questState.QuestRef,
            DisplayName = questState.DisplayName,
            IsComplete = questState.IsComplete,
            IsSuccess = questState.IsSuccess,
            IsFailed = questState.IsFailed,
            FailureReason = questState.FailureReason
        };

        // Find current stage
        var currentStage = quest.Stages?.Stage?.FirstOrDefault(s => s.RefName == questState.CurrentStage);
        if (currentStage != null)
        {
            snapshot.CurrentStageDisplayName = currentStage.DisplayName;

            // Build objective progress list
            if (currentStage.Objectives?.Objective != null)
            {
                foreach (var objective in currentStage.Objectives.Objective)
                {
                    // Skip hidden objectives unless completed
                    if (objective.Hidden)
                    {
                        var isCompleted = questState.CompletedObjectives.TryGetValue(questState.CurrentStage, out var completedObjs) &&
                                        completedObjs.Contains(objective.RefName);
                        if (!isCompleted)
                            continue;
                    }

                    // Evaluate current progress
                    var currentValue = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, currentStage, objective, transactions, _world);
                    var isComplete = currentValue >= objective.Threshold;

                    snapshot.Objectives.Add(new ObjectiveProgress
                    {
                        ObjectiveRef = objective.RefName,
                        DisplayName = objective.DisplayName ?? objective.RefName,
                        CurrentValue = currentValue,
                        TargetValue = objective.Threshold,
                        IsComplete = isComplete,
                        IsOptional = objective.Optional,
                        IsHidden = objective.Hidden
                    });
                }
            }
        }
        else if (string.IsNullOrEmpty(questState.CurrentStage))
        {
            // All stages complete
            snapshot.CurrentStageDisplayName = "Complete";
        }
        else
        {
            snapshot.CurrentStageDisplayName = questState.CurrentStage;
        }

        // Calculate overall progress (% of stages complete)
        if (quest.Stages?.Stage != null)
        {
            var totalStages = quest.Stages.Stage.Length;
            var currentStageIndex = Array.FindIndex(quest.Stages.Stage, s => s.RefName == questState.CurrentStage);

            if (currentStageIndex >= 0)
            {
                snapshot.OverallProgress = (float)currentStageIndex / totalStages;
            }
            else if (string.IsNullOrEmpty(questState.CurrentStage))
            {
                snapshot.OverallProgress = 1.0f;
            }
        }

        return snapshot;
    }
}

/// <summary>
/// Handler for GetActiveQuestsQuery.
/// Returns all active quests for an avatar across all Sagas.
/// </summary>
internal sealed class GetActiveQuestsHandler : IRequestHandler<GetActiveQuestsQuery, List<QuestProgressSnapshot>>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly IMediator _mediator;
    private readonly IWorld _world;

    public GetActiveQuestsHandler(
        ISagaInstanceRepository instanceRepository,
        IMediator mediator,
        IWorld _world)
    {
        _instanceRepository = instanceRepository;
        _mediator = mediator;
        this._world = _world;
    }

    public async Task<List<QuestProgressSnapshot>> Handle(GetActiveQuestsQuery query, CancellationToken ct)
    {
        try
        {
            // Get all Saga instances for this avatar
            var allInstances = await _instanceRepository.GetAllInstancesForAvatarAsync(query.AvatarId, ct);

            var results = new List<QuestProgressSnapshot>();

            foreach (var instance in allInstances)
            {
                // Verify Saga exists
                if (!_world.SagaArcLookup.TryGetValue(instance.SagaRef, out var sagaTemplate))
                    continue;

                // Get expanded triggers for state machine
                if (!_world.SagaTriggersLookup.TryGetValue(instance.SagaRef, out var expandedTriggers))
                    continue;

                // Replay to get current state
                var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
                var currentState = stateMachine.ReplayToNow(instance);

                // Get progress for each active quest
                foreach (var (questRef, questState) in currentState.ActiveQuests)
                {
                    var progressQuery = new GetQuestProgressQuery
                    {
                        AvatarId = query.AvatarId,
                        SagaRef = instance.SagaRef,
                        QuestRef = questRef
                    };

                    var progress = await _mediator.Send(progressQuery, ct);
                    if (progress != null)
                    {
                        results.Add(progress);
                    }
                }
            }

            return results;
        }
        catch (Exception)
        {
            return new List<QuestProgressSnapshot>();
        }
    }
}
