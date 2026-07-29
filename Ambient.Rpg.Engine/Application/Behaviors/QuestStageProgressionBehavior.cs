using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Handlers.Arcs;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Quests;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Ambient.Rpg.Engine.Application.Behaviors;

/// <summary>
/// Pipeline behavior that auto-advances quest stages after Arc commands: when a
/// command commits transactions that complete the current stage's objectives, the
/// stage advances without the game client having to drive it. Advancing CASCADES:
/// if the newly-current stage's objectives are already complete too, it advances in
/// the same command (see the pass loop in AdvanceCompletedStagesAsync). The behavior
/// also recovers quests stranded by a failed nested completion (active with no
/// current stage — R4-30) by re-driving AdvanceQuestStageCommand's recovery path.
///
/// This is the production driver for the stage/objective machinery — before it,
/// nothing ever sent AdvanceQuestStageCommand, so CurrentStage never moved and quest
/// progress sat at 0% forever. Branch stages are driven the same way: no UI or
/// dialogue action sends ChooseQuestBranchCommand, so the player's decision IS
/// completing one branch's objective — when exactly that happens, this behavior
/// derives the ChooseQuestBranchCommand (whose handler enforces exclusivity and
/// advances along the branch's route).
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
        // AdvanceQuestStage and CompleteQuest are excluded as a re-entrancy guard:
        // this behavior sends them itself, and cascading is handled explicitly by
        // the advance loop in AdvanceCompletedStagesAsync — not by pipeline
        // re-entry on the nested sends.
        if (response is ArcCommandResult commandResult && commandResult.Successful &&
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
        // any of them (queries, non-arc commands) are skipped
        var avatarId = GetProperty<Guid>(request, "AvatarId");
        // Was a two-name probe (SagaArcRef on commands, SagaRef on queries) before both
        // collapsed to ArcRef in the 2026-07-29 rename — one lookup now covers both.
        var arcRef = GetProperty<string>(request, "ArcRef");
        // AdvanceQuestStageCommand requires the full entity (reward persistence)
        var avatar = GetProperty<AvatarBase>(request, "Avatar") as AvatarEntity;
        if (avatarId == Guid.Empty || string.IsNullOrEmpty(arcRef) || avatar == null)
            return;

        var world = _serviceProvider.GetRequiredService<IWorld>();
        var instanceRepository = _serviceProvider.GetRequiredService<IArcInstanceRepository>();
        var mediator = _serviceProvider.GetRequiredService<IMediator>();

        // Handle dev arc refs (format: "RealArcRef__DEV__uniqueid")
        var arcRefForLookup = arcRef;
        const string devSuffix = "__DEV__";
        if (arcRef.Contains(devSuffix))
        {
            arcRefForLookup = arcRef.Substring(0, arcRef.IndexOf(devSuffix, StringComparison.Ordinal));
        }

        if (!world.ArcLookup.TryGetValue(arcRefForLookup, out var arcTemplate) ||
            !world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var expandedTriggers))
        {
            return;
        }

        // CASCADE: one player command can complete several consecutive stages at
        // once (the same trigger/defeat/dialogue may satisfy stage N and stage N+1's
        // objectives). Each pass replays state, advances every quest whose current
        // stage is complete, and re-evaluates — until a full pass advances nothing.
        // Each productive pass commits at least one stage advance, so the loop is
        // bounded by the content's total stage count; the hard cap is a defensive
        // backstop only. The final-stage character-driven completion rule applies
        // inside every pass, so a cascade still parks in front of the quest giver.
        const int maxCascadePasses = 100;
        for (var pass = 0; pass < maxCascadePasses; pass++)
        {
            var instance = await instanceRepository.GetOrCreateInstanceAsync(avatarId, arcRef, ct);
            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, world);
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

            var advancedAny = false;
            foreach (var (questRef, questState) in currentState.ActiveQuests.ToList())
            {
                var quest = world.TryGetQuestByRefName(questRef);
                if (quest == null)
                    continue;

                // STRANDED-QUEST RECOVERY (R4-30): the final stage-advance sets
                // CurrentStage to "" and hands off to CompleteQuest; if that nested
                // completion failed, the quest is stuck active with no stage. The
                // recovery path lives in AdvanceQuestStageHandler but needs an
                // AdvanceQuestStageCommand no client sends — so drive it from here,
                // or the quest stays stranded forever.
                if (string.IsNullOrEmpty(questState.CurrentStage))
                {
                    Debug.WriteLine($"[QuestStageProgression] Quest '{questRef}' is stranded (no current stage) — re-attempting completion");

                    var recoverResult = await mediator.Send(new AdvanceQuestStageCommand
                    {
                        AvatarId = avatarId,
                        ArcRef = arcRef,
                        QuestRef = questRef,
                        Avatar = avatar
                    }, ct);

                    if (recoverResult.Successful)
                        advancedAny = true;
                    else
                        Debug.WriteLine($"[QuestStageProgression] Stranded-quest recovery of '{questRef}' failed: {recoverResult.ErrorMessage}");
                    continue;
                }

                var currentStage = quest.Stages?.Stage?.FirstOrDefault(s => s.RefName == questState.CurrentStage);
                if (currentStage == null)
                    continue;

                // Branch stages: IsStageComplete is true only once a QuestBranchChosen
                // exists. Nothing player-facing sends that command, so derive it here —
                // completing a branch's objective IS the choice. On the rare log where
                // several branch objectives are complete, authored branch order decides
                // (deterministic; exclusive content shapes make the tie near-impossible).
                if (currentStage.Branches?.Branch is { Length: > 0 } branches &&
                    !QuestProgressEvaluator.IsStageComplete(quest, currentStage, transactions, world))
                {
                    var completedBranch = branches.FirstOrDefault(b =>
                        b.Objective != null &&
                        QuestProgressEvaluator.IsObjectiveComplete(quest, currentStage, b.Objective, transactions, world));
                    if (completedBranch == null)
                        continue; // genuinely waiting on the player to pursue a branch

                    Debug.WriteLine($"[QuestStageProgression] Branch objective '{completedBranch.Objective!.RefName}' of quest '{questRef}' complete — deriving branch choice '{completedBranch.RefName}'");

                    var chooseResult = await mediator.Send(new ChooseQuestBranchCommand
                    {
                        AvatarId = avatarId,
                        ArcRef = arcRef,
                        QuestRef = questRef,
                        StageRef = currentStage.RefName,
                        BranchRef = completedBranch.RefName,
                        Avatar = avatar
                    }, ct);

                    // The choose handler advances the stage itself; the next pass
                    // evaluates the post-branch stage
                    if (chooseResult.Successful)
                        advancedAny = true;
                    else
                        Debug.WriteLine($"[QuestStageProgression] Derived branch choice for '{questRef}' failed: {chooseResult.ErrorMessage}");
                    continue;
                }

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
                    ArcRef = arcRef,
                    QuestRef = questRef,
                    Avatar = avatar
                }, ct);

                if (advanceResult.Successful)
                    advancedAny = true;
                else
                    Debug.WriteLine($"[QuestStageProgression] Auto-advance of '{questRef}' failed: {advanceResult.ErrorMessage}");
            }

            if (!advancedAny)
                return;
        }
    }

    /// <summary>
    /// The character ref of the spawned character this command interacted with
    /// (StartDialogue/SelectDialogueChoice/TradeItem/... carry CharacterInstanceId),
    /// or null when the command wasn't a character interaction.
    /// </summary>
    private static string? ResolveInteractedCharacterRef(TRequest request, ArcState currentState)
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
