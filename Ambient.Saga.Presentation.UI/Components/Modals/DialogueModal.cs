using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal for character dialogue using CQRS pattern
/// </summary>
public class DialogueModal
{
    private DialogueStateResult? _currentState;
    private bool _isInitialized = false;
    private Guid _lastCharacterInstanceId;
    private bool _isLoading = false;
    private string? _errorMessage = null;

    public void Render(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, ref bool isOpen)
    {
        if (!isOpen)
        {
            _isInitialized = false;
            return;
        }

        // Initialize dialogue on first render for this character
        if (!_isInitialized || _lastCharacterInstanceId != character.CharacterInstanceId)
        {
            _isInitialized = true;
            _lastCharacterInstanceId = character.CharacterInstanceId;
            _errorMessage = null;
            _isLoading = true;
            Task.Run(async () =>
            {
                await InitializeDialogueAsync(viewModel, character);
                _isLoading = false;
            });
        }

        ImGui.SetNextWindowSize(new Vector2(700, 500), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"Talk to {character.DisplayName}", ref isOpen))
        {
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), character.DisplayName);
            ImGui.Separator();

            // Show error if any
            if (_errorMessage != null)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"Error: {_errorMessage}");
                ImGui.Spacing();
                if (ImGui.Button("Close", new Vector2(120, 0)))
                {
                    isOpen = false;
                    _isInitialized = false;
                    _errorMessage = null;
                }
            }
            else if (_isLoading || _currentState == null)
            {
                ImGui.Text("Loading dialogue...");
            }
            else if (_currentState.HasEnded)
            {
                ImGui.TextWrapped("Conversation ended.");
                ImGui.Spacing();
                if (ImGui.Button("Close", new Vector2(120, 0)))
                {
                    isOpen = false;
                    _isInitialized = false;
                }
            }
            else
            {
                // Display dialogue text
                ImGui.BeginChild("DialogueText", new Vector2(0, 250), ImGuiChildFlags.Borders);
                foreach (var text in _currentState.DialogueText)
                {
                    ImGui.TextWrapped(text);
                    ImGui.Spacing();
                }
                ImGui.EndChild();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // Display choices
                if (_currentState.Choices.Count > 0)
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "Your responses:");
                    ImGui.Spacing();

                    var choiceIndex = 1;
                    foreach (var choice in _currentState.Choices)
                    {
                        var choiceText = $"{choiceIndex}. {choice.Text}";
                        if (choice.Cost > 0)
                        {
                            choiceText += $" (Cost: {choice.Cost} tokens)";
                        }

                        var canSelect = choice.IsAvailable;
                        if (!canSelect)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1));
                        }

                        if (ImGui.Selectable(choiceText, false) && canSelect && !_isLoading)
                        {
                            _isLoading = true;
                            Task.Run(async () =>
                            {
                                await SelectChoiceAsync(viewModel, character, modalManager, choice.ChoiceId);
                                _isLoading = false;
                            });
                        }

                        if (!canSelect)
                        {
                            ImGui.PopStyleColor();
                            if (!string.IsNullOrEmpty(choice.BlockedReason))
                            {
                                ImGui.Indent(20);
                                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"→ {choice.BlockedReason}");
                                ImGui.Unindent(20);
                            }
                        }

                        choiceIndex++;
                    }
                }
                else if (_currentState.CanContinue)
                {
                    if (ImGui.Button("Continue...", new Vector2(200, 30)) && !_isLoading)
                    {
                        // Advance to next dialogue node
                        _isLoading = true;
                        Task.Run(async () =>
                        {
                            await AdvanceDialogueAsync(viewModel, character, modalManager);
                            _isLoading = false;
                        });
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();

                if (ImGui.Button("End Conversation", new Vector2(150, 0)))
                {
                    isOpen = false;
                    _isInitialized = false;
                }
            }

            ImGui.End();
        }
    }

    private async Task InitializeDialogueAsync(MainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.CurrentWorld == null || viewModel.PlayerAvatar == null)
            return;

        try
        {
            // Start dialogue
            var startCommand = new StartDialogueCommand
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaArcRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                Avatar = viewModel.PlayerAvatar
            };

            await viewModel.Mediator.Send(startCommand);

            // Get initial state
            await RefreshDialogueStateAsync(viewModel, character);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing dialogue: {ex.Message}");
            _errorMessage = ex.Message;
            _currentState = null;
        }
    }

    private async Task SelectChoiceAsync(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, string choiceId)
    {
        if (viewModel.CurrentWorld == null || viewModel.PlayerAvatar == null)
            return;

        try
        {
            var command = new SelectDialogueChoiceCommand
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaArcRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                ChoiceId = choiceId,
                Avatar = viewModel.PlayerAvatar
            };

            var result = await viewModel.Mediator.Send(command);

            // Check for pending system events (battle, trade transitions)
            if (result.Data.TryGetValue("PendingEvents", out var eventsObj) && eventsObj is List<object> events && events.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Processing {events.Count} pending events");

                // Process the first event (dialogue choices should only have one transition event)
                var firstEvent = events[0];
                var eventType = firstEvent.GetType().Name;

                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Event type: {eventType}");

                // Close dialogue and open appropriate modal
                modalManager.ShowDialogue = false;

                if (eventType.Contains("OpenMerchantTrade"))
                {
                    modalManager.ShowMerchantTrade = true;
                }
                else if (eventType.Contains("StartBossBattle") || eventType.Contains("StartCombat"))
                {
                    modalManager.ShowBossBattle = true;
                }

                return; // Don't refresh dialogue state - we've transitioned to a different modal
            }

            // No transition events - refresh dialogue state normally
            await RefreshDialogueStateAsync(viewModel, character);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error selecting choice: {ex.Message}");
            _errorMessage = ex.Message;
        }
    }

    private async Task AdvanceDialogueAsync(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager)
    {
        if (viewModel.CurrentWorld == null || viewModel.PlayerAvatar == null)
            return;

        try
        {
            var command = new AdvanceDialogueCommand
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaArcRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                Avatar = viewModel.PlayerAvatar
            };

            var result = await viewModel.Mediator.Send(command);

            if (!result.Successful)
            {
                _errorMessage = result.ErrorMessage;
                return;
            }

            // Check for pending system events (battle, trade transitions)
            if (result.Data.TryGetValue("PendingEvents", out var eventsObj) && eventsObj is List<object> events && events.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Processing {events.Count} pending events from advance");

                var firstEvent = events[0];
                var eventType = firstEvent.GetType().Name;

                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Event type: {eventType}");

                // Close dialogue and open appropriate modal
                modalManager.ShowDialogue = false;

                if (eventType.Contains("OpenMerchantTrade"))
                {
                    modalManager.ShowMerchantTrade = true;
                }
                else if (eventType.Contains("StartBossBattle") || eventType.Contains("StartCombat"))
                {
                    modalManager.ShowBossBattle = true;
                }

                return;
            }

            // Refresh dialogue state to show next node
            await RefreshDialogueStateAsync(viewModel, character);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error advancing dialogue: {ex.Message}");
            _errorMessage = ex.Message;
        }
    }

    private async Task RefreshDialogueStateAsync(MainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.CurrentWorld == null || viewModel.PlayerAvatar == null)
            return;

        try
        {
            var query = new GetDialogueStateQuery
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                Avatar = viewModel.PlayerAvatar
            };

            _currentState = await viewModel.Mediator.Send(query);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refreshing dialogue state: {ex.Message}");
        }
    }
}
