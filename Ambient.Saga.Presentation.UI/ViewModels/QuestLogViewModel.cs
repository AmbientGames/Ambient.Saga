using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using CommunityToolkit.Mvvm.ComponentModel;
using MediatR;
using System.Collections.ObjectModel;

namespace Ambient.Presentation.WindowsUI.RpgControls.ViewModels;

/// <summary>
/// Represents a single quest entry in the quest log UI.
/// </summary>
public class QuestDisplayItem
{
    public string RefName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int CurrentValue { get; set; }
    public int TargetValue { get; set; }
    public bool IsCompleted { get; set; }
    public float ProgressPercentage => TargetValue > 0 ? (float)CurrentValue / TargetValue * 100f : 0f;
    public string ProgressText => $"{CurrentValue}/{TargetValue}";
    public string StatusText => IsCompleted ? "Completed" : "In Progress";
}

/// <summary>
/// ViewModel for quest log display.
/// Handles quest progress tracking and display logic.
/// Event-sourced: Reads from SagaState (replayed from transaction log).
/// </summary>
public partial class QuestLogViewModel : ObservableObject
{
    private SagaInteractionContext _context;
    private readonly IMediator _mediator;

    [ObservableProperty]
    private ObservableCollection<QuestDisplayItem> _activeQuests = new();

    [ObservableProperty]
    private ObservableCollection<QuestDisplayItem> _completedQuests = new();

    [ObservableProperty]
    private QuestDisplayItem? _selectedQuest;

    [ObservableProperty]
    private bool _showCompleted = false;

    public bool HasActiveQuests => ActiveQuests.Count > 0;
    public bool HasCompletedQuests => CompletedQuests.Count > 0;
    public bool HasNoQuests => !HasActiveQuests && !HasCompletedQuests;

    public QuestLogViewModel(SagaInteractionContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _ = RefreshQuestsAsync(); // Fire and forget initial load
    }

    /// <summary>
    /// Refreshes the quest lists from SagaState (event-sourced).
    /// Call this when quest progress changes or when switching avatars.
    /// </summary>
    public async Task RefreshQuestsAsync()
    {
        ActiveQuests.Clear();
        CompletedQuests.Clear();

        if (_context.AvatarEntity == null || _context.World?.Gameplay?.SagaArcs == null)
        {
            OnPropertyChanged(nameof(HasActiveQuests));
            OnPropertyChanged(nameof(HasCompletedQuests));
            OnPropertyChanged(nameof(HasNoQuests));
            return;
        }

        // Query all Sagas for quest state
        foreach (var saga in _context.World.Gameplay.SagaArcs)
        {
            try
            {
                // Get Saga state from transaction log
                var sagaState = await _mediator.Send(new GetSagaStateQuery
                {
                    AvatarId = _context.AvatarEntity.Id,
                    SagaRef = saga.RefName
                });

                if (sagaState == null) continue;

                // Extract active quests
                foreach (var (questRef, questState) in sagaState.ActiveQuests)
                {
                    var quest = _context.World.TryGetQuestByRefName(questRef);
                    if (quest == null) continue;

                    ActiveQuests.Add(new QuestDisplayItem
                    {
                        RefName = quest.RefName,
                        DisplayName = quest.DisplayName ?? quest.RefName,
                        Description = quest.Description ?? string.Empty,
                        CurrentValue = questState.ObjectiveProgress.Values.Sum(), // Sum of all objective progress
                        TargetValue = 1, // TODO: Compute from quest template stages/objectives
                        IsCompleted = false
                    });
                }

                // Extract completed quests
                foreach (var questRef in sagaState.CompletedQuests)
                {
                    var quest = _context.World.TryGetQuestByRefName(questRef);
                    if (quest == null) continue;

                    CompletedQuests.Add(new QuestDisplayItem
                    {
                        RefName = quest.RefName,
                        DisplayName = quest.DisplayName ?? quest.RefName,
                        Description = quest.Description ?? string.Empty,
                        CurrentValue = 1, // Completed
                        TargetValue = 1, // Completed
                        IsCompleted = true
                    });
                }
            }
            catch (Exception)
            {
                // Skip this Saga if state query fails
                continue;
            }
        }

        OnPropertyChanged(nameof(HasActiveQuests));
        OnPropertyChanged(nameof(HasCompletedQuests));
        OnPropertyChanged(nameof(HasNoQuests));
    }

    /// <summary>
    /// Synchronous wrapper for backward compatibility.
    /// </summary>
    [Obsolete("Use RefreshQuestsAsync instead")]
    public void RefreshQuests()
    {
        _ = RefreshQuestsAsync();
    }

    /// <summary>
    /// OBSOLETE: Quest progress is now managed via CQRS commands (AcceptQuestCommand, CompleteQuestCommand).
    /// This method is kept for backward compatibility but does nothing.
    /// </summary>
    [Obsolete("Use AcceptQuestCommand and CompleteQuestCommand via MediatR instead")]
    public bool IncrementQuestProgress(string questRef, int amount = 1)
    {
        // Quest progress is managed by transaction log, not directly on AvatarEntity
        // Call RefreshQuestsAsync to update UI from latest transaction log state
        _ = RefreshQuestsAsync();
        return false;
    }

    /// <summary>
    /// OBSOLETE: Quest progress is now managed via CQRS commands (AcceptQuestCommand, CompleteQuestCommand).
    /// This method is kept for backward compatibility but does nothing.
    /// </summary>
    [Obsolete("Use AcceptQuestCommand and CompleteQuestCommand via MediatR instead")]
    public bool SetQuestProgress(string questRef, int currentValue)
    {
        // Quest progress is managed by transaction log, not directly on AvatarEntity
        // Call RefreshQuestsAsync to update UI from latest transaction log state
        _ = RefreshQuestsAsync();
        return false;
    }

    /// <summary>
    /// OBSOLETE: Use CompleteQuestCommand via MediatR instead.
    /// This method is kept for backward compatibility but does nothing.
    /// </summary>
    [Obsolete("Use CompleteQuestCommand via MediatR instead")]
    public bool CompleteQuest(string questRef)
    {
        // Quest completion is managed by CompleteQuestCommand, not directly on AvatarEntity
        // Call RefreshQuestsAsync to update UI from latest transaction log state
        _ = RefreshQuestsAsync();
        return false;
    }

    partial void OnShowCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasActiveQuests));
        OnPropertyChanged(nameof(HasCompletedQuests));
        OnPropertyChanged(nameof(HasNoQuests));
    }
}
