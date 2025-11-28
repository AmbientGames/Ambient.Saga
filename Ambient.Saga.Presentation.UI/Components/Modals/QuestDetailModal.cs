using Ambient.Domain;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.SagaEngine.Application.Commands.Saga;
using Ambient.SagaEngine.Application.Queries.Saga;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using ImGuiNET;
using MediatR;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Comprehensive quest detail modal showing multi-stage progress, objectives, branches, and rewards.
/// Shows full details for an active or completed quest.
/// </summary>
public class QuestDetailModal
{
    private readonly IMediator _mediator;
    private string? _currentQuestRef;
    private string? _currentSagaRef;
    private Quest? _questTemplate;
    private QuestProgressSnapshot? _questProgress;
    private QuestState? _questState;
    private bool _isLoading;
    private string? _errorMessage;
    private bool _isAbandoning;

    public QuestDetailModal(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public async Task OpenAsync(string questRef, string sagaRef, MainViewModel viewModel)
    {
        _currentQuestRef = questRef;
        _currentSagaRef = sagaRef;
        _isLoading = true;
        _errorMessage = null;

        // Load quest template
        _questTemplate = viewModel.CurrentWorld?.TryGetQuestByRefName(questRef);

        if (_questTemplate == null)
        {
            _errorMessage = "Quest template not found";
            _isLoading = false;
            return;
        }

        try
        {
            // Load quest progress from CQRS query
            _questProgress = await _mediator.Send(new GetQuestProgressQuery
            {
                AvatarId = viewModel.PlayerAvatar!.Id,
                SagaRef = sagaRef,
                QuestRef = questRef
            });

            // Also load full quest state for more details
            var sagaState = await _mediator.Send(new GetSagaStateQuery
            {
                AvatarId = viewModel.PlayerAvatar!.Id,
                SagaRef = sagaRef
            });

            if (sagaState?.ActiveQuests.TryGetValue(questRef, out var questState) == true)
            {
                _questState = questState;
            }

            _isLoading = false;
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error loading quest: {ex.Message}";
            _isLoading = false;
        }
    }

    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen || _questTemplate == null) return;

        ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        var title = _questTemplate.DisplayName ?? _questTemplate.RefName;
        if (_questProgress?.IsComplete == true)
        {
            title += _questProgress.IsSuccess ? " [COMPLETED]" : " [FAILED]";
        }
        else if (_questState != null)
        {
            title += " [ACTIVE]";
        }

        if (ImGui.Begin($"Quest: {title}", ref isOpen, ImGuiWindowFlags.None))
        {
            if (_isLoading)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Loading quest details...");
            }
            else if (_errorMessage != null)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), _errorMessage);
                ImGui.Spacing();
                if (ImGui.Button("Close"))
                {
                    isOpen = false;
                }
            }
            else
            {
                RenderQuestContent(viewModel, ref isOpen);
            }

            ImGui.End();
        }
    }

    private void RenderQuestContent(MainViewModel viewModel, ref bool isOpen)
    {
        ImGui.BeginChild("QuestDetailScroll", new Vector2(0, -60), ImGuiChildFlags.None);

        // Header with quest name and description
        RenderHeader();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Current stage and objectives
        if (_questProgress != null && !_questProgress.IsComplete)
        {
            RenderCurrentStage();
        }

        // Completed stages history
        if (_questState != null)
        {
            RenderQuestHistory();
        }

        // Rewards
        if (_questTemplate!.Rewards != null && _questTemplate.Rewards.Length > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            RenderRewards();
        }

        // Fail conditions
        if (_questTemplate.FailConditions != null && _questTemplate.FailConditions.Length > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            RenderFailConditions();
        }

        // Prerequisites (if not met, would be grayed out)
        if (_questTemplate.Prerequisites != null && _questTemplate.Prerequisites.Length > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            RenderPrerequisites();
        }

        ImGui.EndChild();

        // Action buttons at bottom
        ImGui.Separator();
        RenderActionButtons(viewModel, ref isOpen);
    }

    private void RenderHeader()
    {
        ImGui.TextColored(new Vector4(1, 1, 0.7f, 1), _questTemplate!.DisplayName ?? _questTemplate.RefName);

        // Status badge
        if (_questProgress != null)
        {
            ImGui.SameLine();
            if (_questProgress.IsComplete)
            {
                if (_questProgress.IsSuccess)
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "[COMPLETED]");
                }
                else if (_questProgress.IsFailed)
                {
                    ImGui.TextColored(new Vector4(1, 0, 0, 1), "[FAILED]");
                    if (!string.IsNullOrEmpty(_questProgress.FailureReason))
                    {
                        ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), $"Reason: {_questProgress.FailureReason}");
                    }
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0, 1, 1, 1), "[ACTIVE]");

                // Overall progress
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"({_questProgress.OverallProgress:P0})");
            }
        }

        // Description
        if (!string.IsNullOrEmpty(_questTemplate.Description))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(_questTemplate.Description);
        }

        // Quest metadata
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Quest ID: {_questTemplate.RefName}");
        if (_questState != null)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Accepted: {_questState.AcceptedAt:g}");
            if (_questState.CompletedAt.HasValue)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Completed: {_questState.CompletedAt:g}");
            }
        }
    }

    private void RenderCurrentStage()
    {
        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Current Stage:");
        ImGui.Spacing();

        ImGui.Indent(10);

        // Stage name
        ImGui.TextColored(new Vector4(1, 1, 0, 1), _questProgress!.CurrentStageDisplayName);
        ImGui.Spacing();

        // Get current stage from template
        var currentStage = _questTemplate!.Stages?.Stage?.FirstOrDefault(s => s.RefName == _questState?.CurrentStage);

        // Check if this stage has branches
        if (currentStage?.Branches != null)
        {
            RenderBranchChoices(currentStage);
        }
        // Otherwise show objectives
        else if (_questProgress.Objectives != null && _questProgress.Objectives.Count > 0)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Objectives:");
            ImGui.Spacing();

            foreach (var objective in _questProgress.Objectives)
            {
                RenderObjective(objective);
            }
        }

        ImGui.Unindent(10);
    }

    private void RenderObjective(ObjectiveProgress objective)
    {
        var isComplete = objective.IsComplete;
        var color = isComplete ? new Vector4(0, 1, 0, 1) : new Vector4(1, 1, 1, 1);

        // Checkbox or bullet
        if (isComplete)
        {
            ImGui.TextColored(color, "✓");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "○");
        }

        ImGui.SameLine();

        // Objective name
        var displayName = objective.DisplayName ?? objective.ObjectiveRef;
        if (objective.IsOptional)
        {
            displayName += " (Optional)";
        }

        ImGui.TextColored(color, displayName);

        // Progress bar
        if (!isComplete && objective.TargetValue > 0)
        {
            ImGui.Indent(20);
            var progress = (float)objective.CurrentValue / objective.TargetValue;
            var progressText = $"{objective.CurrentValue} / {objective.TargetValue}";
            ImGui.ProgressBar(progress, new Vector2(-1, 20), progressText);
            ImGui.Unindent(20);
        }

        ImGui.Spacing();
    }

    private void RenderBranchChoices(QuestStage stage)
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0.5f, 1), "Choose Your Path:");
        ImGui.Spacing();

        if (stage.Branches!.Exclusive)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "You must choose one of the following:");
        }
        ImGui.Spacing();

        foreach (var branch in stage.Branches.Branch ?? Array.Empty<QuestBranch>())
        {
            var branchName = branch.DisplayName ?? branch.RefName;
            var wasChosen = _questState?.ChosenBranch == branch.RefName;

            if (wasChosen)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), $"→ {branchName} [CHOSEN]");
            }
            else if (stage.Branches.Exclusive && !string.IsNullOrEmpty(_questState?.ChosenBranch))
            {
                // Other branches locked out
                ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1), $"  {branchName} [LOCKED]");
            }
            else
            {
                ImGui.BulletText(branchName);
            }

            // Show branch objective if it exists
            if (branch.Objective != null)
            {
                ImGui.Indent(20);
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"Requires: {branch.Objective.DisplayName}");
                ImGui.Unindent(20);
            }

            ImGui.Spacing();
        }
    }

    private void RenderQuestHistory()
    {
        if (_questState!.CompletedObjectives.Count == 0) return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Quest History"))
        {
            ImGui.Indent(10);

            foreach (var (stageRef, completedObjs) in _questState.CompletedObjectives)
            {
                var stage = _questTemplate!.Stages?.Stage?.FirstOrDefault(s => s.RefName == stageRef);
                var stageName = stage?.DisplayName ?? stageRef;

                if (ImGui.TreeNode($"✓ {stageName}"))
                {
                    foreach (var objRef in completedObjs)
                    {
                        var objective = stage?.Objectives?.Objective?.FirstOrDefault(o => o.RefName == objRef);
                        var objName = objective?.DisplayName ?? objRef;
                        ImGui.TextColored(new Vector4(0, 1, 0, 1), $"  ✓ {objName}");
                    }
                    ImGui.TreePop();
                }
            }

            ImGui.Unindent(10);
        }
    }

    private void RenderRewards()
    {
        ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "Rewards:");
        ImGui.Spacing();

        ImGui.Indent(10);

        foreach (var reward in _questTemplate!.Rewards!)
        {
            var condition = reward.Condition == QuestRewardCondition.OnSuccess ? "On Success" :
                           reward.Condition == QuestRewardCondition.OnFailure ? "On Failure" : "Always";

            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"[{condition}]");

            if (reward.Currency != null)
            {
                ImGui.BulletText($"{reward.Currency.Amount} Credits");
            }

            if (reward.Equipment != null && reward.Equipment.Length > 0)
            {
                foreach (var equip in reward.Equipment)
                {
                    ImGui.BulletText($"{equip.EquipmentRef} x{equip.Quantity}");
                }
            }

            if (reward.Consumable != null && reward.Consumable.Length > 0)
            {
                foreach (var consumable in reward.Consumable)
                {
                    ImGui.BulletText($"{consumable.ConsumableRef} x{consumable.Quantity}");
                }
            }

            if (reward.QuestToken != null && reward.QuestToken.Length > 0)
            {
                foreach (var token in reward.QuestToken)
                {
                    ImGui.BulletText($"Quest Token: {token.QuestTokenRef} x{token.Quantity}");
                }
            }
        }

        ImGui.Unindent(10);
    }

    private void RenderFailConditions()
    {
        ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Failure Conditions:");
        ImGui.Spacing();

        ImGui.Indent(10);

        foreach (var failCondition in _questTemplate!.FailConditions!)
        {
            var conditionText = failCondition.Type switch
            {
                QuestFailConditionType.CharacterDied => $"If {failCondition.CharacterRef} dies",
                QuestFailConditionType.TimeExpired => $"If time expires ({failCondition.TimeLimit} minutes)",
                QuestFailConditionType.WrongChoiceMade => $"If wrong choice made in dialogue",
                QuestFailConditionType.ItemLost => $"If {failCondition.ItemRef} is lost",
                _ => "Unknown condition"
            };

            ImGui.BulletText(conditionText);
        }

        ImGui.Unindent(10);
    }

    private void RenderPrerequisites()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Prerequisites:");
        ImGui.Spacing();

        ImGui.Indent(10);

        foreach (var prereq in _questTemplate!.Prerequisites!)
        {
            string prereqText;

            if (!string.IsNullOrEmpty(prereq.QuestRef))
            {
                prereqText = $"Complete quest: {prereq.QuestRef}";
            }
            else if (prereq.MinimumLevelSpecified)
            {
                prereqText = $"Reach level {prereq.MinimumLevel}";
            }
            else if (!string.IsNullOrEmpty(prereq.RequiredItemRef))
            {
                prereqText = $"Have item: {prereq.RequiredItemRef}";
            }
            else if (!string.IsNullOrEmpty(prereq.RequiredAchievementRef))
            {
                prereqText = $"Require achievement: {prereq.RequiredAchievementRef}";
            }
            else
            {
                prereqText = "Unknown prerequisite";
            }

            ImGui.BulletText(prereqText);
        }

        ImGui.Unindent(10);
    }

    private void RenderActionButtons(MainViewModel viewModel, ref bool isOpen)
    {
        ImGui.Spacing();

        if (_questProgress?.IsComplete == true)
        {
            // Completed quest - just close
            if (ImGui.Button("Close", new Vector2(150, 35)))
            {
                isOpen = false;
            }
        }
        else
        {
            // Active quest - show actions
            if (_isAbandoning)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Abandon Quest", new Vector2(150, 35)))
            {
                AbandonQuest(viewModel);
            }

            if (_isAbandoning)
            {
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Abandoning...");
            }

            ImGui.SameLine();
            if (ImGui.Button("Close", new Vector2(100, 35)))
            {
                isOpen = false;
            }
        }
    }

    private async void AbandonQuest(MainViewModel viewModel)
    {
        if (_isAbandoning || viewModel.PlayerAvatar == null) return;

        _isAbandoning = true;

        try
        {
            var command = new AbandonQuestCommand
            {
                AvatarId = viewModel.PlayerAvatar.Id,
                QuestRef = _currentQuestRef!,
                SagaArcRef = _currentSagaRef!,
                Avatar = viewModel.PlayerAvatar
            };

            var result = await _mediator.Send(command);

            if (result.Successful)
            {
                // Quest abandoned - refresh UI
                if (viewModel.QuestLog != null)
                {
                    await viewModel.QuestLog.RefreshQuestsAsync();
                }

                _isAbandoning = false;
                // Close the modal
            }
            else
            {
                _errorMessage = result.ErrorMessage;
                _isAbandoning = false;
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"Error abandoning quest: {ex.Message}";
            _isAbandoning = false;
        }
    }
}
