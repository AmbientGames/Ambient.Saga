using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Handlers.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Quests;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Ambient.Saga.Engine.Application.Behaviors;

/// <summary>
/// Pipeline behavior that auto-advances quest stages after Saga commands: when a
/// command commits transactions that complete the current stage's objectives, the
/// stage advances without the game client having to drive it.
///
/// This is the production driver for the stage/objective machinery — before it,
/// nothing ever sent AdvanceQuestStageCommand, so CurrentStage never moved and quest
/// progress sat at 0% forever. Branch stages still require an explicit
/// ChooseQuestBranchCommand (a player decision); IsStageComplete blocks the
/// auto-advance until the branch is chosen.
///
/// COMPLETION is character-driven: in this game things happen through characters,
/// not "magically" in the field. The FINAL stage only advances (cascading into
/// CompleteQuest with rewards) when the executed command is an interaction with the
/// quest giver — walk back and talk to them and the quest turns in automatically,
/// no authored turn-in dialogue required. Quests with no recorded giver (accepted
/// outside dialogue) complete immediately, and authored dialogue CompleteQuest
/// actions keep working anywhere (e.g. turn-in at a different NPC than the giver).
///
/// Uses IServiceProvider to resolve dependencies at runtime rather than constructor
/// injection, matching AchievementEvaluationBehavior (behaviors are constructed during
/// MediatR initialization before repositories are configured).
/// </summary>
public class QuestStageProgressionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IServiceProvider _serviceProvider;

    public QuestStageProgressionBehavior(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        // Only evaluate after successful commands that can affect quest progress.
        // AdvanceQuestStage is excluded: its own nested sends already re-enter this
        // behavior, which cascades consecutive completed stages without a second
        // evaluation of the same advance.
        if (response is SagaCommandResult commandResult && commandResult.Successful &&
            request is not AdvanceQuestStageCommand &&
            request is not CompleteQuestCommand)
        {
            try
            {
                await AdvanceCompletedStagesAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                // Never fail the originating command over progression evaluation
                Debug.WriteLine($"[QuestStageProgression] Error evaluating stages: {ex.Message}");
            }
        }

        return response;
    }

    private async Task AdvanceCompletedStagesAsync(TRequest request, CancellationToken ct)
    {
        // The evaluation needs the avatar, its id, and the arc — commands that lack
        // any of them (queries, non-saga commands) are skipped
        var avatarId = GetProperty<Guid>(request, "AvatarId");
        var sagaArcRef = GetProperty<string>(request, "SagaArcRef") ?? GetProperty<string>(request, "SagaRef");
        // AdvanceQuestStageCommand requires the full entity (reward persistence)
        var avatar = GetProperty<AvatarBase>(request, "Avatar") as AvatarEntity;
        if (avatarId == Guid.Empty || string.IsNullOrEmpty(sagaArcRef) || avatar == null)
            return;

        var world = _serviceProvider.GetRequiredService<IWorld>();
        var instanceRepository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
        var mediator = _serviceProvider.GetRequiredService<IMediator>();

        // Handle dev saga refs (format: "RealSagaRef__DEV__uniqueid")
        var sagaRefForLookup = sagaArcRef;
        const string devSuffix = "__DEV__";
        if (sagaArcRef.Contains(devSuffix))
        {
            sagaRefForLookup = sagaArcRef.Substring(0, sagaArcRef.IndexOf(devSuffix, StringComparison.Ordinal));
        }

        if (!world.SagaArcLookup.TryGetValue(sagaRefForLookup, out var sagaTemplate) ||
            !world.SagaTriggersLookup.TryGetValue(sagaRefForLookup, out var expandedTriggers))
        {
            return;
        }

        var instance = await instanceRepository.GetOrCreateInstanceAsync(avatarId, sagaArcRef, ct);
        var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, world);
        var currentState = stateMachine.ReplayToNow(instance);

        if (currentState.ActiveQuests.Count == 0)
            return;

        // Objectives are intentionally cross-arc: a quest given in this arc may be
        // satisfied by triggers/tokens/dialogue/defeats that landed in another arc's
        // instance. Evaluate stage completion against the avatar's whole cross-arc
        // committed log, not just this arc's instance (see CrossArcQuestTransactionLog).
        var transactions = await CrossArcQuestTransactionLog.BuildAsync(avatarId, instanceRepository, ct);

        // Which spawned character (if any) the executed command interacted with —
        // used to gate final-stage turn-in on meeting the quest giver
        var interactedCharacterRef = ResolveInteractedCharacterRef(request, currentState);

        foreach (var (questRef, questState) in currentState.ActiveQuests.ToList())
        {
            var quest = world.TryGetQuestByRefName(questRef);
            var currentStage = quest?.Stages?.Stage?.FirstOrDefault(s => s.RefName == questState.CurrentStage);
            if (quest == null || currentStage == null)
                continue;

            if (!QuestProgressEvaluator.IsStageComplete(quest, currentStage, transactions, world))
                continue;

            // Final stage: advancing it cascades into quest completion + rewards.
            // That moment belongs to the quest giver — hold it until the player
            // interacts with them (unless no giver was recorded).
            var isFinalStage = QuestProgressEvaluator.GetNextStage(quest, currentStage, transactions) == null;
            if (isFinalStage &&
                !string.IsNullOrEmpty(questState.QuestGiverRef) &&
                !string.Equals(interactedCharacterRef, questState.QuestGiverRef, StringComparison.Ordinal))
            {
                Debug.WriteLine($"[QuestStageProgression] Quest '{questRef}' ready to turn in — awaiting interaction with giver '{questState.QuestGiverRef}'");
                continue;
            }

            Debug.WriteLine($"[QuestStageProgression] Stage '{currentStage.RefName}' of quest '{questRef}' complete — advancing");

            var advanceResult = await mediator.Send(new AdvanceQuestStageCommand
            {
                AvatarId = avatarId,
                SagaArcRef = sagaArcRef,
                QuestRef = questRef,
                Avatar = avatar
            }, ct);

            if (!advanceResult.Successful)
            {
                Debug.WriteLine($"[QuestStageProgression] Auto-advance of '{questRef}' failed: {advanceResult.ErrorMessage}");
            }
        }
    }

    /// <summary>
    /// The character ref of the spawned character this command interacted with
    /// (StartDialogue/SelectDialogueChoice/TradeItem/... carry CharacterInstanceId),
    /// or null when the command wasn't a character interaction.
    /// </summary>
    private static string? ResolveInteractedCharacterRef(TRequest request, SagaState currentState)
    {
        var characterInstanceId = GetProperty<Guid>(request, "CharacterInstanceId");
        if (characterInstanceId == Guid.Empty)
            return null;

        var characterState = currentState.Characters.Values
            .FirstOrDefault(c => c.CharacterInstanceId == characterInstanceId);
        return characterState?.CharacterRef;
    }

    private static T? GetProperty<T>(TRequest request, string name)
    {
        var property = typeof(TRequest).GetProperty(name);
        if (property != null && typeof(T).IsAssignableFrom(property.PropertyType))
        {
            return (T?)property.GetValue(request);
        }
        return default;
    }
}
