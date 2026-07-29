using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Quests;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for GetQuestProgressQuery.
/// Evaluates quest progress by querying transaction logs.
/// </summary>
internal sealed class GetQuestProgressHandler : IRequestHandler<GetQuestProgressQuery, QuestProgressSnapshot?>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public GetQuestProgressHandler(
        IArcInstanceRepository instanceRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<QuestProgressSnapshot?> Handle(GetQuestProgressQuery query, CancellationToken ct)
    {
        try
        {
            // Get Arc instance (full ref — dev instances "Real__DEV__id" are distinct)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.ArcRef, ct);

            // Template lookups use the stripped ref: dev arc instances replay against
            // the real template, and without stripping every quest accepted in a dev
            // instance was invisible to this query (returned null)
            var arcRefForLookup = QuestInstanceLocator.StripDevSuffix(query.ArcRef);

            // Verify Arc exists
            if (!_world.ArcLookup.TryGetValue(arcRefForLookup, out var arcTemplate))
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
            if (!_world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var expandedTriggers))
            {
                return null;
            }

            // Replay to get current state
            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
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
                    OverallProgress = 1.0f
                };
            }
            else
            {
                // Quest not started
                return null;
            }

            // Build progress snapshot from quest state. Objectives can be satisfied
            // cross-arc (the satisfying transaction may live in a different arc's
            // instance than the quest's owner), so evaluate against the avatar's whole
            // cross-arc committed log (see CrossArcQuestTransactionLog).
            var transactions = await CrossArcQuestTransactionLog.BuildAsync(query.AvatarId, _instanceRepository, ct);
            var snapshot = BuildProgressSnapshot(quest, questState, transactions);

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
        List<ArcTransaction> transactions)
    {
        var snapshot = new QuestProgressSnapshot
        {
            QuestRef = questState.QuestRef,
            DisplayName = questState.DisplayName,
            IsComplete = questState.IsComplete,
            IsSuccess = questState.IsSuccess
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

        // Calculate overall progress (% of stages complete). Counting the current
        // acceptance's QuestStageAdvanced transactions is order-proof: the raw array
        // index of CurrentStage misreports whenever the authored stage array order
        // differs from the actual chain order (branch alternates, out-of-order
        // authoring), and a branch route may legitimately skip array entries.
        if (quest.Stages?.Stage is { Length: > 0 })
        {
            if (string.IsNullOrEmpty(questState.CurrentStage))
            {
                snapshot.OverallProgress = 1.0f;
            }
            else
            {
                var stagesAdvanced = QuestProgressEvaluator.ScopeToCurrentAcceptance(quest, transactions)
                    .Count(t => t.Type == ArcTransactionType.QuestStageAdvanced &&
                                t.GetData<string>(TransactionDataKeys.QuestRef) == quest.RefName);
                snapshot.OverallProgress = Math.Clamp((float)stagesAdvanced / quest.Stages.Stage.Length, 0f, 1f);
            }
        }

        return snapshot;
    }
}

/// <summary>
/// Handler for GetActiveQuestsQuery.
/// Returns all active quests for an avatar across all Arcs.
/// </summary>
internal sealed class GetActiveQuestsHandler : IRequestHandler<GetActiveQuestsQuery, List<QuestProgressSnapshot>>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IMediator _mediator;
    private readonly IWorld _world;

    public GetActiveQuestsHandler(
        IArcInstanceRepository instanceRepository,
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
            // Get all arc instances for this avatar
            var allInstances = await _instanceRepository.GetAllInstancesForAvatarAsync(query.AvatarId, ct);

            var results = new List<QuestProgressSnapshot>();

            foreach (var instance in allInstances)
            {
                // Dev arc instances ("Real__DEV__id") replay against the real
                // template — strip for lookups, keep the full ref for the instance
                // and the nested per-quest query
                var arcRefForLookup = QuestInstanceLocator.StripDevSuffix(instance.ArcRef);

                // Verify Arc exists
                if (!_world.ArcLookup.TryGetValue(arcRefForLookup, out var arcTemplate))
                    continue;

                // Get expanded triggers for state machine
                if (!_world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var expandedTriggers))
                    continue;

                // Replay to get current state
                var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
                var currentState = stateMachine.ReplayToNow(instance);

                // Get progress for each active quest
                foreach (var (questRef, questState) in currentState.ActiveQuests)
                {
                    var progressQuery = new GetQuestProgressQuery
                    {
                        AvatarId = query.AvatarId,
                        ArcRef = instance.ArcRef,
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
