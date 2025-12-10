using Ambient.Domain;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using ImGuiNET;
using MediatR;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal for quest signpost interactions.
/// Displays quest details and allows player to accept quests.
/// </summary>
public class QuestModal
{
    private readonly IMediator _mediator;
    private string? _currentQuestRef;
    private string? _currentSagaRef;
    private string? _currentSignpostRef;
    private Quest? _currentQuest;
    private SagaFeature? _currentSignpost;
    private bool _isAlreadyActive;
    private bool _isAlreadyCompleted;
    private bool _isAccepting;

    public QuestModal(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public void Open(string questRef, string sagaRef, string signpostRef, MainViewModel viewModel)
    {
        _currentQuestRef = questRef;
        _currentSagaRef = sagaRef;
        _currentSignpostRef = signpostRef;
        _isAccepting = false;
        _isAlreadyActive = false;
        _isAlreadyCompleted = false;

        // Look up quest template from world
        _currentQuest = viewModel.CurrentWorld?.Gameplay?.Quests?.FirstOrDefault(q => q.RefName == questRef);

        // Look up quest feature for difficulty/duration metadata
        foreach (var saga in viewModel.CurrentWorld?.Gameplay?.SagaArcs ?? Array.Empty<SagaArc>())
        {
            if (!string.IsNullOrEmpty(saga.SagaFeatureRef))
            {
                var feature = viewModel.CurrentWorld?.TryGetSagaFeatureByRefName(saga.SagaFeatureRef);
                if (feature != null && feature.Type == SagaFeatureType.Quest && feature.QuestRef == questRef)
                {
                    _currentSignpost = feature;
                    break;
                }
            }
        }

        // Check if quest is already active or completed (event-sourced from transaction log)
        // Fire-and-forget but capture errors
        _ = LoadQuestStateAsync(questRef, sagaRef, viewModel);
    }

    private async Task LoadQuestStateAsync(string questRef, string sagaRef, MainViewModel viewModel)
    {
        try
        {
            _isAlreadyActive = await IsQuestActiveAsync(questRef, sagaRef, viewModel);
            _isAlreadyCompleted = await IsQuestCompletedAsync(questRef, sagaRef, viewModel);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading quest state: {ex.Message}");
        }
    }

    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen || _currentQuest == null) return;

        // Center the window
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(650, 550), ImGuiCond.FirstUseEver);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);

        var windowFlags = ImGuiWindowFlags.NoCollapse;

        if (ImGui.Begin($"Quest###QuestModal", ref isOpen, windowFlags))
        {
            // Quest header with icon/difficulty
            RenderQuestHeader();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Quest description in styled area
            if (!string.IsNullOrEmpty(_currentQuest.Description))
            {
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.6f, 1), "Description:");
                ImGui.Spacing();

                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.08f, 0.9f));
                ImGui.BeginChild("QuestDescription", new Vector2(0, 100), ImGuiChildFlags.Borders);
                ImGui.Indent(10);
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.85f, 1));
                ImGui.TextWrapped(_currentQuest.Description);
                ImGui.PopStyleColor();
                ImGui.Unindent(10);
                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            // Quest objectives
            ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), "Objectives:");
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.08f, 0.05f, 0.9f));
            ImGui.BeginChild("QuestObjectives", new Vector2(0, 80), ImGuiChildFlags.Borders);
            ImGui.Indent(10);
            ImGui.Spacing();
            ImGui.BulletText("See quest log for detailed objectives and progress");
            ImGui.Unindent(10);
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.Spacing();

            ImGui.Separator();
            ImGui.Spacing();

            // Quest metadata
            RenderQuestMetadata();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Action buttons
            RenderActionButtons(viewModel, ref isOpen);
        }
        ImGui.End();

        ImGui.PopStyleVar(2);
    }

    private void RenderQuestHeader()
    {
        // Quest name with larger text
        ImGui.SetWindowFontScale(1.2f);
        ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), _currentQuest!.DisplayName ?? _currentQuest.RefName);
        ImGui.SetWindowFontScale(1.0f);

        // Status and difficulty badges on same line
        if (_currentSignpost != null || _isAlreadyCompleted || _isAlreadyActive)
        {
            // Difficulty badge
            if (_currentSignpost != null)
            {
                var (difficultyColor, difficultyBgColor) = _currentSignpost.Difficulty switch
                {
                    QuestDifficulty.Easy => (new Vector4(0.2f, 0.8f, 0.2f, 1), new Vector4(0.1f, 0.3f, 0.1f, 0.8f)),
                    QuestDifficulty.Normal => (new Vector4(0.9f, 0.9f, 0.2f, 1), new Vector4(0.3f, 0.3f, 0.1f, 0.8f)),
                    QuestDifficulty.Hard => (new Vector4(1, 0.5f, 0.1f, 1), new Vector4(0.35f, 0.2f, 0.05f, 0.8f)),
                    QuestDifficulty.Epic => (new Vector4(1, 0.2f, 0.2f, 1), new Vector4(0.35f, 0.1f, 0.1f, 0.8f)),
                    _ => (new Vector4(0.8f, 0.8f, 0.8f, 1), new Vector4(0.25f, 0.25f, 0.25f, 0.8f))
                };

                ImGui.TextColored(difficultyColor, $"Difficulty: {_currentSignpost.Difficulty}");
            }

            // Status badge
            if (_isAlreadyCompleted)
            {
                if (_currentSignpost != null) ImGui.SameLine(0, 30);
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Status:");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1), "COMPLETED");
            }
            else if (_isAlreadyActive)
            {
                if (_currentSignpost != null) ImGui.SameLine(0, 30);
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Status:");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.9f, 1), "ACTIVE");
            }
            else
            {
                if (_currentSignpost != null) ImGui.SameLine(0, 30);
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Status:");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.5f, 1), "AVAILABLE");
            }
        }
    }

    private void RenderQuestMetadata()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Quest Details:");
        ImGui.Indent(10);

        if (_currentSignpost != null)
        {
            ImGui.Text($"Difficulty: {_currentSignpost.Difficulty}");

            if (_currentSignpost.EstimatedDurationMinutesSpecified && _currentSignpost.EstimatedDurationMinutes > 0)
            {
                ImGui.Text($"Estimated Duration: {_currentSignpost.EstimatedDurationMinutes} minutes");
            }
        }

        // TODO: Display quest stage/objective metadata for multi-stage quests
        ImGui.Text($"Quest ID: {_currentQuest!.RefName}");

        ImGui.Unindent(10);
    }

    private void RenderActionButtons(MainViewModel viewModel, ref bool isOpen)
    {
        var buttonWidth = 140f;
        var buttonHeight = 38f;

        if (_isAlreadyCompleted)
        {
            // Quest already completed
            var completedText = "You have already completed this quest.";
            var textSize = ImGui.CalcTextSize(completedText);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1), completedText);
            ImGui.Spacing();
            ImGui.Spacing();

            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);
            if (ImGui.Button("Close", new Vector2(buttonWidth, buttonHeight)))
            {
                isOpen = false;
            }
        }
        else if (_isAlreadyActive)
        {
            // Quest already active
            var activeText = "This quest is already in your quest log.";
            var textSize = ImGui.CalcTextSize(activeText);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.9f, 1), activeText);
            ImGui.Spacing();
            ImGui.Spacing();

            var totalWidth = buttonWidth * 2 + 20;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - totalWidth) * 0.5f);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.35f, 0.4f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.45f, 0.55f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.55f, 0.7f, 1));
            if (ImGui.Button("View Quest Log", new Vector2(buttonWidth, buttonHeight)))
            {
                isOpen = false;
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();
            if (ImGui.Button("Close", new Vector2(buttonWidth, buttonHeight)))
            {
                isOpen = false;
            }
        }
        else
        {
            // Quest available - show accept/decline buttons
            var totalWidth = buttonWidth * 2 + 20;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - totalWidth) * 0.5f);

            if (_isAccepting)
            {
                ImGui.BeginDisabled();
            }

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1));
            if (ImGui.Button(_isAccepting ? "Accepting..." : "Accept Quest", new Vector2(buttonWidth, buttonHeight)))
            {
                AcceptQuest(viewModel);
            }
            ImGui.PopStyleColor(3);

            if (_isAccepting)
            {
                ImGui.EndDisabled();
            }

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.25f, 0.25f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.3f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.35f, 0.35f, 1));
            if (ImGui.Button("Decline", new Vector2(buttonWidth, buttonHeight)))
            {
                isOpen = false;
            }
            ImGui.PopStyleColor(3);
        }
    }

    private void AcceptQuest(MainViewModel viewModel)
    {
        if (_isAccepting || viewModel.PlayerAvatar == null) return;

        _isAccepting = true;
        _ = AcceptQuestAsync(viewModel);
    }

    private async Task AcceptQuestAsync(MainViewModel viewModel)
    {
        try
        {
            var command = new AcceptQuestCommand
            {
                AvatarId = viewModel.PlayerAvatar!.Id,
                QuestRef = _currentQuestRef!,
                SagaArcRef = _currentSagaRef!,
                QuestGiverRef = _currentSignpostRef!,
                Avatar = viewModel.PlayerAvatar
            };

            var result = await _mediator.Send(command);

            if (result.Successful)
            {
                // Quest accepted successfully
                _isAlreadyActive = true;
                _isAccepting = false;

                // Refresh quest log to show newly accepted quest
                if (viewModel.QuestLog != null)
                {
                    await viewModel.QuestLog.RefreshQuestsAsync();
                }

                System.Diagnostics.Debug.WriteLine($"Quest accepted: {_currentQuest!.DisplayName}");
            }
            else
            {
                // Failed to accept quest
                _isAccepting = false;
                System.Diagnostics.Debug.WriteLine($"Failed to accept quest: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            _isAccepting = false;
            System.Diagnostics.Debug.WriteLine($"Error accepting quest: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the quest is currently active for the avatar (event-sourced from transaction log).
    /// </summary>
    private async Task<bool> IsQuestActiveAsync(string questRef, string sagaRef, MainViewModel viewModel)
    {
        if (viewModel.PlayerAvatar == null) return false;

        try
        {
            var sagaState = await _mediator.Send(new GetSagaStateQuery
            {
                AvatarId = viewModel.PlayerAvatar.Id,
                SagaRef = sagaRef
            });

            return sagaState?.ActiveQuests.ContainsKey(questRef) ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if the quest has been completed by the avatar (event-sourced from transaction log).
    /// </summary>
    private async Task<bool> IsQuestCompletedAsync(string questRef, string sagaRef, MainViewModel viewModel)
    {
        if (viewModel.PlayerAvatar == null) return false;

        try
        {
            var sagaState = await _mediator.Send(new GetSagaStateQuery
            {
                AvatarId = viewModel.PlayerAvatar.Id,
                SagaRef = sagaRef
            });

            return sagaState?.CompletedQuests.Contains(questRef) ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
