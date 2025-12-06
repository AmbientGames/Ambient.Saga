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

        ImGui.SetNextWindowSize(new Vector2(700, 600), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.Begin($"Quest: {_currentQuest.DisplayName ?? _currentQuest.RefName}", ref isOpen))
        {
            // Quest header with icon/difficulty
            RenderQuestHeader();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Quest description
            if (!string.IsNullOrEmpty(_currentQuest.Description))
            {
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1), "Description:");
                ImGui.Indent(10);
                ImGui.TextWrapped(_currentQuest.Description);
                ImGui.Unindent(10);
                ImGui.Spacing();
            }

            // Quest objectives
            ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Objectives:");
            ImGui.Indent(10);
            ImGui.BulletText("See quest log for detailed objectives and progress");
            // TODO: Display actual quest stages and objectives from multi-stage system
            ImGui.Unindent(10);
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

            ImGui.End();
        }
    }

    private void RenderQuestHeader()
    {
        // Difficulty badge
        if (_currentSignpost != null)
        {
            var difficultyColor = _currentSignpost.Difficulty switch
            {
                QuestDifficulty.Easy => new Vector4(0, 1, 0, 1),      // Green
                QuestDifficulty.Normal => new Vector4(1, 1, 0, 1),    // Yellow
                QuestDifficulty.Hard => new Vector4(1, 0.5f, 0, 1),   // Orange
                QuestDifficulty.Epic => new Vector4(1, 0, 0, 1),      // Red
                _ => new Vector4(1, 1, 1, 1)
            };

            ImGui.TextColored(difficultyColor, $"[{_currentSignpost.Difficulty}]");
            ImGui.SameLine();
        }

        // Quest name
        ImGui.TextColored(new Vector4(1, 1, 0.7f, 1), _currentQuest!.DisplayName ?? _currentQuest.RefName);

        // Status badge
        if (_isAlreadyCompleted)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "[COMPLETED]");
        }
        else if (_isAlreadyActive)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0, 1, 1, 1), "[ACTIVE]");
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
        if (_isAlreadyCompleted)
        {
            // Quest already completed
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "You have already completed this quest.");
            ImGui.Spacing();

            if (ImGui.Button("Close", new Vector2(150, 35)))
            {
                isOpen = false;
            }
        }
        else if (_isAlreadyActive)
        {
            // Quest already active
            ImGui.TextColored(new Vector4(0, 1, 1, 1), "This quest is already in your quest log.");
            ImGui.Spacing();

            if (ImGui.Button("View Quest Log", new Vector2(150, 35)))
            {
                // Future: Open quest log modal
                isOpen = false;
            }

            ImGui.SameLine();
            if (ImGui.Button("Close", new Vector2(100, 35)))
            {
                isOpen = false;
            }
        }
        else
        {
            // Quest available - show accept/decline buttons
            if (_isAccepting)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Accept Quest", new Vector2(150, 35)))
            {
                AcceptQuest(viewModel);
            }

            if (_isAccepting)
            {
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Accepting...");
            }

            ImGui.SameLine();
            if (ImGui.Button("Decline", new Vector2(100, 35)))
            {
                isOpen = false;
            }
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
