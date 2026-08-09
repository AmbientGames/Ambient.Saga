using Ambient.Domain;
using Ambient.Rpg.Presentation.UI.ViewModels;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using ImGuiNET;
using Ambient.Rpg.Ui.Components.Utilities;
using MediatR;
using System.Numerics;
using Ambient.Rpg.Rendering.DirectX;

namespace Ambient.Rpg.Ui.Components.Modals;

/// <summary>
/// Comprehensive quest detail modal showing multi-stage progress, objectives, branches, and rewards.
/// Shows full details for an active or completed quest.
/// </summary>
public class QuestDetailModal
{
    // Pure yellow: loading/abandoning status text and the current stage name.
    private static readonly Vector4 HighlightYellow = new(1f, 1f, 0f, 1f);

    private readonly IMediator _mediator;
    private string? _currentQuestRef;
    private string? _currentArcRef;
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

    public async Task OpenAsync(string questRef, string arcRef, RpgMainViewModel viewModel)
    {
        _currentQuestRef = questRef;
        _currentArcRef = arcRef;
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
                AvatarId = viewModel.Avatar!.AvatarId,
                ArcRef = arcRef,
                QuestRef = questRef
            });

            // Also load full quest state for more details
            var arcState = await _mediator.Send(new GetArcStateQuery
            {
                AvatarId = viewModel.Avatar!.AvatarId,
                ArcRef = arcRef
            });

            if (arcState?.ActiveQuests.TryGetValue(questRef, out var questState) == true)
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

    public void Render(RpgMainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen || _questTemplate == null) return;

        ImGuiHelpers.SetupModalWindow(900, 700);

        // Stable title with ### id — status ([ACTIVE]/[COMPLETED]) is rendered
        // as text inside the window (RenderHeader) so the ImGui window identity doesn't
        // change mid-life.
        var title = _questTemplate.DisplayName ?? _questTemplate.RefName;

        if (ImGui.Begin($"Quest: {title}###QuestDetailModal", ref isOpen, ImGuiWindowFlags.None))
        {
            if (_isLoading)
            {
                ImGui.TextColored(HighlightYellow, "Loading quest details...");
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
        }
        // End() must be called unconditionally (outside the Begin() if) — a collapsed
        // window returns false from Begin() and skipping End() imbalances ImGui.
        ImGui.End();
    }

    private void RenderQuestContent(RpgMainViewModel viewModel, ref bool isOpen)
    {
        // Reserve space for action buttons at bottom
        var footerHeight = ImGui.GetFrameHeightWithSpacing() * 2.5f;
        ImGui.BeginChild("QuestDetailScroll", new Vector2(ImGuiSizes.Fill, -footerHeight), ImGuiChildFlags.None);

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
            RenderRewards(viewModel);
        }

        // Prerequisites (if not met, would be grayed out)
        if (_questTemplate.Prerequisites != null && _questTemplate.Prerequisites.Length > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            RenderPrerequisites(viewModel);
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
                ImGui.TextColored(UIColors.TextSuccessBright, "[COMPLETED]");
            }
            else
            {
                ImGui.TextColored(new Vector4(0, 1, 1, 1), "[ACTIVE]");

                // Overall progress
                ImGui.SameLine();
                ImGui.TextColored(UIColors.TextMuted, $"({_questProgress.OverallProgress:P0})");
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
        ImGui.TextColored(UIColors.TextDim, $"Quest ID: {_questTemplate.RefName}");
        if (_questState != null)
        {
            ImGui.TextColored(UIColors.TextDim, $"Accepted: {_questState.AcceptedAt:g}");
            if (_questState.CompletedAt.HasValue)
            {
                ImGui.TextColored(UIColors.TextDim, $"Completed: {_questState.CompletedAt:g}");
            }
        }
    }

    private void RenderCurrentStage()
    {
        ImGui.TextColored(UIColors.TextSuccess, "Current Stage:");
        ImGui.Spacing();

        ImGui.Indent(10 * UIConstants.DpiScale);

        // Stage name
        ImGui.TextColored(HighlightYellow, _questProgress!.CurrentStageDisplayName);
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
            ImGui.TextColored(UIColors.TextHighlight, "Objectives:");
            ImGui.Spacing();

            foreach (var objective in _questProgress.Objectives)
            {
                RenderObjective(objective);
            }
        }

        ImGui.Unindent(10 * UIConstants.DpiScale);
    }

    private void RenderObjective(ObjectiveProgress objective)
    {
        var isComplete = objective.IsComplete;
        var color = isComplete ? UIColors.TextSuccessBright : new Vector4(1, 1, 1, 1);

        // Checkbox or bullet
        if (isComplete)
        {
            ImGui.TextColored(color, "[x]");
        }
        else
        {
            ImGui.TextColored(UIColors.TextDisabled, "[ ]");
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
            ImGui.Indent(20 * UIConstants.DpiScale);
            var progress = (float)objective.CurrentValue / objective.TargetValue;
            var progressText = $"{objective.CurrentValue} / {objective.TargetValue}";
            ImGui.ProgressBar(progress, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight()), progressText);
            ImGui.Unindent(20 * UIConstants.DpiScale);
        }

        ImGui.Spacing();
    }

    private void RenderBranchChoices(QuestStage stage)
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0.5f, 1), "Choose Your Path:");
        ImGui.Spacing();

        if (stage.Branches!.Exclusive)
        {
            ImGui.TextColored(UIColors.TextMuted, "You must choose one of the following:");
        }
        ImGui.Spacing();

        foreach (var branch in stage.Branches.Branch ?? Array.Empty<QuestBranch>())
        {
            var branchName = branch.DisplayName ?? branch.RefName;
            var wasChosen = _questState?.ChosenBranch == branch.RefName;

            if (wasChosen)
            {
                ImGui.TextColored(UIColors.TextSuccessBright, $"→ {branchName} [CHOSEN]");
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
                ImGui.Indent(20 * UIConstants.DpiScale);
                ImGui.TextColored(UIColors.TextMuted, $"Requires: {branch.Objective.DisplayName}");
                ImGui.Unindent(20 * UIConstants.DpiScale);
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
            ImGui.Indent(10 * UIConstants.DpiScale);

            foreach (var (stageRef, completedObjs) in _questState.CompletedObjectives)
            {
                var stage = _questTemplate!.Stages?.Stage?.FirstOrDefault(s => s.RefName == stageRef);
                var stageName = stage?.DisplayName ?? stageRef;

                if (ImGui.TreeNode($"[x] {stageName}###hist_{stageRef}"))
                {
                    foreach (var objRef in completedObjs)
                    {
                        var objective = stage?.Objectives?.Objective?.FirstOrDefault(o => o.RefName == objRef);
                        var objName = objective?.DisplayName ?? objRef;
                        ImGui.TextColored(UIColors.TextSuccessBright, $"  [x] {objName}");
                    }
                    ImGui.TreePop();
                }
            }

            ImGui.Unindent(10 * UIConstants.DpiScale);
        }
    }

    private void RenderRewards(RpgMainViewModel viewModel)
    {
        ImGui.TextColored(UIColors.Gold, "Rewards:");
        ImGui.Spacing();

        ImGui.Indent(10 * UIConstants.DpiScale);

        foreach (var reward in _questTemplate!.Rewards!)
        {
            var condition = reward.Condition == QuestRewardCondition.OnSuccess ? "On Success" : "Always";

            ImGui.TextColored(UIColors.TextMuted, $"[{condition}]");

            if (reward.Currency != null)
            {
                ImGui.BulletText($"{reward.Currency.Amount} Credits");
            }

            if (reward.Equipment != null && reward.Equipment.Length > 0)
            {
                foreach (var equip in reward.Equipment)
                {
                    var equipDef = viewModel.CurrentWorld?.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == equip.EquipmentRef);
                    var equipName = equipDef?.DisplayName ?? equip.EquipmentRef;
                    ImGui.BulletText($"{equipName} x{equip.Quantity}");
                }
            }

            if (reward.Consumable != null && reward.Consumable.Length > 0)
            {
                foreach (var consumable in reward.Consumable)
                {
                    var consumableDef = viewModel.CurrentWorld?.Gameplay?.Consumables?.FirstOrDefault(c => c.RefName == consumable.ConsumableRef);
                    var consumableName = consumableDef?.DisplayName ?? consumable.ConsumableRef;
                    ImGui.BulletText($"{consumableName} x{consumable.Quantity}");
                }
            }

        }

        ImGui.Unindent(10 * UIConstants.DpiScale);
    }

    private void RenderPrerequisites(RpgMainViewModel viewModel)
    {
        ImGui.TextColored(UIColors.TextHighlight, "Prerequisites:");
        ImGui.Spacing();

        ImGui.Indent(10 * UIConstants.DpiScale);

        foreach (var prereq in _questTemplate!.Prerequisites!)
        {
            string prereqText;

            if (!string.IsNullOrEmpty(prereq.QuestRef))
            {
                var questDef = viewModel.CurrentWorld?.TryGetQuestByRefName(prereq.QuestRef);
                var questName = questDef?.DisplayName ?? prereq.QuestRef;
                prereqText = $"Complete quest: {questName}";
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
                var achievementDef = viewModel.CurrentWorld?.Gameplay?.Achievements?.FirstOrDefault(a => a.RefName == prereq.RequiredAchievementRef);
                var achievementName = achievementDef?.DisplayName ?? prereq.RequiredAchievementRef;
                prereqText = $"Require achievement: {achievementName}";
            }
            else
            {
                prereqText = "Unknown prerequisite";
            }

            ImGui.BulletText(prereqText);
        }

        ImGui.Unindent(10 * UIConstants.DpiScale);
    }

    private void RenderActionButtons(RpgMainViewModel viewModel, ref bool isOpen)
    {
        ImGui.Spacing();

        var buttonHeight = ImGui.GetFrameHeight() * 1.2f;

        var wideButtonWidth = 150f * UIConstants.DpiScale;
        var narrowButtonWidth = 100f * UIConstants.DpiScale;

        if (_questProgress?.IsComplete == true)
        {
            // Completed quest - just close
            if (ImGui.Button("Close", new Vector2(wideButtonWidth, buttonHeight)))
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

            ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonDanger);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonDangerHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonDangerActive);
            if (ImGui.Button("Abandon Quest", new Vector2(wideButtonWidth, buttonHeight)))
            {
                AbandonQuest(viewModel);
            }
            ImGui.PopStyleColor(3);

            if (_isAbandoning)
            {
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextColored(HighlightYellow, "Abandoning...");
            }

            ImGui.SameLine();
            if (ImGui.Button("Close", new Vector2(narrowButtonWidth, buttonHeight)))
            {
                isOpen = false;
            }
        }
    }

    private void AbandonQuest(RpgMainViewModel viewModel)
    {
        if (_isAbandoning || viewModel.Avatar == null) return;

        _isAbandoning = true;
        _ = AbandonQuestAsync(viewModel);
    }

    private async Task AbandonQuestAsync(RpgMainViewModel viewModel)
    {
        try
        {
            var command = new AbandonQuestCommand
            {
                AvatarId = viewModel.Avatar!.AvatarId,
                QuestRef = _currentQuestRef!,
                ArcRef = _currentArcRef!,
                Avatar = viewModel.Avatar
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
