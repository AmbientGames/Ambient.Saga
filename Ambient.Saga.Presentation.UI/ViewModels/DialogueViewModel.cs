using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Ambient.SagaEngine.Application.Results.Saga;

namespace Ambient.Saga.Presentation.UI.ViewModels;

/// <summary>
/// ViewModel for character dialogue using CQRS pattern.
/// Displays current dialogue state and handles player choices.
/// </summary>
public partial class DialogueViewModel : ObservableObject
{
    private readonly Func<DialogueChoiceOption, Task> _onChoiceSelected;
    private readonly Func<Task> _onContinue;

    [ObservableProperty]
    private ObservableCollection<string> _dialogueText = new();

    [ObservableProperty]
    private ObservableCollection<DialogueChoiceOption> _choices = new();

    [ObservableProperty]
    private bool _canContinue;

    public DialogueViewModel(
        Func<DialogueChoiceOption, Task> onChoiceSelected,
        Func<Task> onContinue)
    {
        _onChoiceSelected = onChoiceSelected;
        _onContinue = onContinue;
    }

    /// <summary>
    /// Updates the dialogue state from CQRS query result.
    /// </summary>
    public void UpdateState(DialogueStateResult state)
    {
        DialogueText.Clear();
        foreach (var text in state.DialogueText)
        {
            DialogueText.Add(text);
        }

        Choices.Clear();
        foreach (var choice in state.Choices)
        {
            Choices.Add(choice);
        }

        CanContinue = state.CanContinue;
    }

    [RelayCommand]
    private async Task SelectChoice(DialogueChoiceOption choice)
    {
        await _onChoiceSelected(choice);
    }

    [RelayCommand]
    private async Task Continue()
    {
        await _onContinue();
    }
}
