using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal for character dialogue using CQRS pattern.
/// Displays NPC dialogue text and player choices in a polished conversation UI.
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
            _ = InitializeDialogueAndSetLoadingAsync(viewModel, character);
        }

        // Center the window
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(750, 550), ImGuiCond.FirstUseEver);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);

        var windowFlags = ImGuiWindowFlags.NoCollapse;

        if (ImGui.Begin($"Conversation with {character.DisplayName}###DialogueModal", ref isOpen, windowFlags))
        {
            RenderDialogueContent(viewModel, character, modalManager, ref isOpen);
        }
        ImGui.End();

        ImGui.PopStyleVar(2);
    }

    private void RenderDialogueContent(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, ref bool isOpen)
    {
        // Header with character info
        RenderCharacterHeader(viewModel, character);

        ImGui.Separator();
        ImGui.Spacing();

        // Show error if any
        if (_errorMessage != null)
        {
            RenderError(ref isOpen);
            return;
        }

        // Loading state - only show if we're actually loading
        if (_isLoading)
        {
            RenderLoading();
            return;
        }

        // No state and not loading - show a helpful message
        if (_currentState == null)
        {
            RenderNoDialogue(ref isOpen);
            return;
        }

        // Dialogue ended
        if (_currentState.HasEnded)
        {
            RenderDialogueEnded(character, modalManager, ref isOpen);
            return;
        }

        // Active dialogue
        RenderActiveDialogue(viewModel, character, modalManager, ref isOpen);
    }

    private void RenderCharacterHeader(MainViewModel viewModel, CharacterViewModel character)
    {
        // Character name with colored styling
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.9f, 0.4f, 1.0f));
        ImGui.SetWindowFontScale(1.2f);
        ImGui.Text(character.DisplayName);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor();

        // Character type/description if available
        var characterTemplate = viewModel.CurrentWorld?.Gameplay?.Characters?
            .FirstOrDefault(c => c.RefName == character.CharacterRef);

        if (characterTemplate != null)
        {
            // Show affinity on same line as name if present
            if (!string.IsNullOrEmpty(characterTemplate.AffinityRef))
            {
                var affinity = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?
                    .FirstOrDefault(a => a.RefName == characterTemplate.AffinityRef);
                if (affinity != null)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.5f, 0.6f, 0.9f, 1.0f), $"  [{affinity.DisplayName ?? affinity.RefName}]");
                }
            }

            // Description on its own line, wrapped
            if (!string.IsNullOrEmpty(characterTemplate.Description))
            {
                ImGui.PushTextWrapPos(ImGui.GetWindowWidth() - 30);
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), characterTemplate.Description);
                ImGui.PopTextWrapPos();
            }
        }
    }

    private void RenderError(ref bool isOpen)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), "Something went wrong:");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.6f, 0.6f, 1));
        ImGui.TextWrapped(_errorMessage);
        ImGui.PopStyleColor();
        ImGui.Spacing();
        ImGui.Spacing();

        if (ImGui.Button("Close", new Vector2(120, 35)))
        {
            CloseAndReset(ref isOpen);
        }
    }

    private void RenderLoading()
    {
        ImGui.Spacing();
        ImGui.Spacing();
        var loadingText = "Loading dialogue...";
        var textSize = ImGui.CalcTextSize(loadingText);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), loadingText);
    }

    private void RenderNoDialogue(ref bool isOpen)
    {
        ImGui.Spacing();
        ImGui.Spacing();

        var text = "This character has nothing to say.";
        var textSize = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.6f, 1), text);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        // Center the close button
        var buttonWidth = 120f;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);
        if (ImGui.Button("Close", new Vector2(buttonWidth, 35)))
        {
            CloseAndReset(ref isOpen);
        }
    }

    private void RenderDialogueEnded(CharacterViewModel character, ModalManager modalManager, ref bool isOpen)
    {
        ImGui.Spacing();
        ImGui.Spacing();

        var endText = "The conversation has ended.";
        var textSize = ImGui.CalcTextSize(endText);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1), endText);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        // Check if character is lootable (defeated and has loot)
        if (character.CanLoot && !character.IsAlive)
        {
            // Show loot option for defeated characters
            var lootButtonWidth = 150f;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - lootButtonWidth) * 0.5f);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.5f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.6f, 0.25f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.7f, 0.3f, 1));

            if (ImGui.Button("Collect Loot", new Vector2(lootButtonWidth, 35)))
            {
                // Close dialogue and open loot modal
                modalManager.CloseModal("Dialogue");
                modalManager.SelectedCharacter = character;
                modalManager.OpenModal("Loot");
                CloseAndReset(ref isOpen);
                return;
            }

            ImGui.PopStyleColor(3);

            ImGui.Spacing();
            ImGui.Spacing();
        }

        // Center the close button
        var buttonWidth = 150f;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);
        if (ImGui.Button("End Conversation", new Vector2(buttonWidth, 35)))
        {
            CloseAndReset(ref isOpen);
        }
    }

    private void RenderActiveDialogue(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, ref bool isOpen)
    {
        if (_currentState == null) return;

        // Calculate dynamic heights
        var windowHeight = ImGui.GetWindowHeight();
        var availableHeight = windowHeight - 180; // Reserve space for header, buttons, padding
        var dialogueHeight = Math.Max(150, availableHeight * 0.55f);
        var choicesHeight = Math.Max(100, availableHeight * 0.45f);

        // Dialogue text area with styled background
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.08f, 0.9f));
        ImGui.BeginChild("DialogueTextArea", new Vector2(0, dialogueHeight), ImGuiChildFlags.Borders);

        ImGui.Spacing();
        ImGui.Indent(10);

        // Render each line of dialogue text
        foreach (var text in _currentState.DialogueText)
        {
            // Style dialogue text
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.9f, 1.0f));
            ImGui.PushTextWrapPos(ImGui.GetWindowWidth() - 30);
            ImGui.TextWrapped(text);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.Spacing();
        }

        ImGui.Unindent(10);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.Spacing();

        // Choices/Continue section
        if (_currentState.Choices.Count > 0)
        {
            RenderChoices(viewModel, character, modalManager, choicesHeight);
        }
        else if (_currentState.CanContinue)
        {
            RenderContinueButton(viewModel, character, modalManager);
        }

        // Bottom action bar
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        RenderBottomBar(ref isOpen);
    }

    private void RenderChoices(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, float height)
    {
        if (_currentState == null) return;

        ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), "Choose your response:");
        ImGui.Spacing();

        ImGui.BeginChild("ChoicesArea", new Vector2(0, height), ImGuiChildFlags.None);

        var choiceIndex = 0;
        foreach (var choice in _currentState.Choices)
        {
            choiceIndex++;
            RenderChoice(viewModel, character, modalManager, choice, choiceIndex);
        }

        ImGui.EndChild();
    }

    private void RenderChoice(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, DialogueChoiceOption choice, int index)
    {
        var canSelect = choice.IsAvailable && !_isLoading;

        // Build choice text
        var choiceText = $"{index}. {choice.Text}";
        if (choice.Cost > 0)
        {
            choiceText += $" [Cost: {choice.Cost}]";
        }

        // Style based on availability
        if (!canSelect)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.15f, 0.15f, 0.15f, 1));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.15f, 0.15f, 0.15f, 1));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.15f, 0.15f, 0.15f, 1));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0.8f, 1));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.35f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.3f, 0.5f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.25f, 0.45f, 0.25f, 1));
        }

        // Use Selectable for click handling with custom styling
        if (ImGui.Selectable($"  {choiceText}##choice_{index}", false, ImGuiSelectableFlags.None, new Vector2(0, 30)))
        {
            if (canSelect)
            {
                _isLoading = true;
                _ = SelectChoiceAndSetLoadingAsync(viewModel, character, modalManager, choice.ChoiceId);
            }
        }

        ImGui.PopStyleColor(4);

        // Show blocked reason if unavailable
        if (!choice.IsAvailable && !string.IsNullOrEmpty(choice.BlockedReason))
        {
            ImGui.Indent(30);
            ImGui.TextColored(new Vector4(1, 0.5f, 0.3f, 1), $"({choice.BlockedReason})");
            ImGui.Unindent(30);
        }

        ImGui.Spacing();
    }

    private void RenderContinueButton(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager)
    {
        ImGui.Spacing();
        ImGui.Spacing();

        // Center the continue button
        var buttonWidth = 200f;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);

        var canContinue = !_isLoading;
        if (!canContinue)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Continue...", new Vector2(buttonWidth, 40)))
        {
            _isLoading = true;
            _ = AdvanceDialogueAndSetLoadingAsync(viewModel, character, modalManager);
        }

        if (!canContinue)
        {
            ImGui.EndDisabled();
        }
    }

    private void RenderBottomBar(ref bool isOpen)
    {
        // End conversation button on the right
        var buttonWidth = 150f;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - buttonWidth - 20);

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.2f, 0.2f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.25f, 0.25f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.3f, 0.3f, 1));

        if (ImGui.Button("Leave", new Vector2(buttonWidth, 30)))
        {
            CloseAndReset(ref isOpen);
        }

        ImGui.PopStyleColor(3);
    }

    private void CloseAndReset(ref bool isOpen)
    {
        isOpen = false;
        _isInitialized = false;
        _currentState = null;
        _errorMessage = null;
        _isLoading = false;
    }

    #region Async Operations

    private async Task InitializeDialogueAndSetLoadingAsync(MainViewModel viewModel, CharacterViewModel character)
    {
        try
        {
            await InitializeDialogueAsync(viewModel, character);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task SelectChoiceAndSetLoadingAsync(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, string choiceId)
    {
        try
        {
            await SelectChoiceAsync(viewModel, character, modalManager, choiceId);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task AdvanceDialogueAndSetLoadingAsync(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager)
    {
        try
        {
            await AdvanceDialogueAsync(viewModel, character, modalManager);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task InitializeDialogueAsync(MainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.CurrentWorld == null)
        {
            _errorMessage = "No world loaded. Please load a world first.";
            return;
        }

        if (viewModel.PlayerAvatar == null)
        {
            _errorMessage = "No avatar selected. Please select an avatar first.";
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Starting dialogue with character {character.CharacterRef}, SagaRef: {character.SagaRef}");

            // Start dialogue
            var startCommand = new StartDialogueCommand
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaArcRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                Avatar = viewModel.PlayerAvatar
            };

            var startResult = await viewModel.Mediator.Send(startCommand);
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] StartDialogue result: Successful={startResult.Successful}, Error={startResult.ErrorMessage}");

            if (!startResult.Successful)
            {
                _errorMessage = startResult.ErrorMessage ?? "Failed to start dialogue";
                return;
            }

            // Get initial state
            await RefreshDialogueStateAsync(viewModel, character);

            // If state is still null after refresh, show error
            if (_currentState == null)
            {
                _errorMessage = "Failed to load dialogue state. The character may not have dialogue available.";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Error initializing dialogue: {ex.Message}\n{ex.StackTrace}");
            _errorMessage = $"Error: {ex.Message}";
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
                modalManager.CloseModal("Dialogue");

                if (eventType.Contains("OpenMerchantTrade"))
                {
                    modalManager.OpenModal("MerchantTrade");
                }
                else if (eventType.Contains("StartBossBattle") || eventType.Contains("StartCombat"))
                {
                    modalManager.OpenModal("BossBattle");
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
                modalManager.CloseModal("Dialogue");

                if (eventType.Contains("OpenMerchantTrade"))
                {
                    modalManager.OpenModal("MerchantTrade");
                }
                else if (eventType.Contains("StartBossBattle") || eventType.Contains("StartCombat"))
                {
                    modalManager.OpenModal("BossBattle");
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
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] RefreshDialogueState skipped - World or Avatar is null");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Refreshing dialogue state for character {character.CharacterInstanceId}");

            var query = new GetDialogueStateQuery
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                Avatar = viewModel.PlayerAvatar
            };

            _currentState = await viewModel.Mediator.Send(query);

            if (_currentState != null)
            {
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Got state - HasEnded={_currentState.HasEnded}, TextCount={_currentState.DialogueText?.Count ?? 0}, ChoiceCount={_currentState.Choices?.Count ?? 0}, CanContinue={_currentState.CanContinue}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] RefreshDialogueState returned null");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Error refreshing dialogue state: {ex.Message}\n{ex.StackTrace}");
            _errorMessage = $"Error loading dialogue: {ex.Message}";
        }
    }

    #endregion
}
