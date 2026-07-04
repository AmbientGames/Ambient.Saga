using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue.Events;
using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal for character dialogue using CQRS pattern.
/// Displays NPC dialogue text and avatar choices in a polished conversation UI.
/// </summary>
public class DialogueModal
{
    private DialogueStateResult? _currentState;
    private bool _isInitialized = false;
    private Guid _lastCharacterInstanceId;

    // Tree/node requested by a battle dialogue trigger (one-shot, from ModalManager)
    private (string TreeRef, string? NodeId)? _startOverride;
    private bool _isLoading = false;
    private string? _errorMessage = null;

    /// <summary>
    /// Resets all dialogue state. Called by DialogueModalAdapter.OnClosed so reopening
    /// a conversation doesn't render the previous session's stale tree (the registry
    /// never renders with isOpen=false, so the old reset-on-close path never ran).
    /// </summary>
    public void Reset()
    {
        _isInitialized = false;
        _lastCharacterInstanceId = Guid.Empty;
        _currentState = null;
        _errorMessage = null;
        _isLoading = false;
        _startOverride = null;
    }

    public void Render(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, ref bool isOpen)
    {
        if (!isOpen)
        {
            Reset();
            return;
        }

        // Initialize dialogue on first render for this character. A pending battle
        // trigger override always forces a fresh session at the requested node.
        if (!_isInitialized || _lastCharacterInstanceId != character.CharacterInstanceId ||
            modalManager.DialogueStartOverride != null)
        {
            _isInitialized = true;
            _lastCharacterInstanceId = character.CharacterInstanceId;
            _errorMessage = null;
            _isLoading = true;

            // Battle dialogue triggers request a specific tree/node (one-shot hand-off)
            _startOverride = modalManager.DialogueStartOverride;
            modalManager.DialogueStartOverride = null;

            _ = InitializeDialogueAndSetLoadingAsync(viewModel, character);
        }

        // Center the window using helper
        ImGuiHelpers.SetupModalWindow(750, 550);

        // Style the window (DPI-scaled)
        var scale = UIConstants.DpiScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20 * scale, 20 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f * scale);

        var windowFlags = ImGuiWindowFlags.NoCollapse;

        if (ImGui.Begin($"Conversation with {character.DisplayName}###DialogueModal", ref isOpen, windowFlags))
        {
            RenderDialogueContent(viewModel, character, modalManager, ref isOpen);
        }
        ImGui.End();

        ImGui.PopStyleVar(2);
    }

    private void RenderDialogueContent(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, ref bool isOpen)
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
            RenderDialogueEnded(ref isOpen);
            return;
        }

        // Active dialogue
        RenderActiveDialogue(viewModel, character, modalManager, ref isOpen);
    }

    private void RenderCharacterHeader(SagaMainViewModel viewModel, CharacterViewModel character)
    {
        // Character name with colored styling
        ImGui.PushFont(UIConstants.FontTitle);
        ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1.0f), character.DisplayName);
        ImGui.PopFont();

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
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
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

        // Use frame height for button
        if (ImGui.Button("Close", new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight())))
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

        // Center text using available width
        var text = "This character has nothing to say.";
        var textSize = ImGui.CalcTextSize(text);
        var avail = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail.X - textSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.6f, 1), text);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        // Full-width close button
        if (ImGui.Button("Close", new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight())))
        {
            CloseAndReset(ref isOpen);
        }
    }

    private void RenderDialogueEnded(ref bool isOpen)
    {
        ImGui.Spacing();
        ImGui.Spacing();

        // Center text using available width
        var endText = "The conversation has ended.";
        var textSize = ImGui.CalcTextSize(endText);
        var avail = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail.X - textSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1), endText);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        var buttonHeight = ImGui.GetFrameHeight();

        // Full-width close button
        if (ImGui.Button("End Conversation", new Vector2(ImGuiSizes.Fill, buttonHeight)))
        {
            CloseAndReset(ref isOpen);
        }
    }

    private void RenderActiveDialogue(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, ref bool isOpen)
    {
        if (_currentState == null) return;

        // Calculate dynamic heights using GetContentRegionAvail
        // Reserve space for bottom bar (separator + button row) AND choices header text
        var footerHeight = ImGui.GetFrameHeightWithSpacing() * 2;
        var choicesHeaderHeight = ImGui.GetTextLineHeightWithSpacing() * 2; // "Choose your response:" + spacing
        var availableHeight = ImGui.GetContentRegionAvail().Y - footerHeight - choicesHeaderHeight;
        var dialogueHeight = Math.Max(ImGui.GetFrameHeightWithSpacing() * 5, availableHeight * 0.55f);
        var choicesHeight = Math.Max(ImGui.GetFrameHeightWithSpacing() * 3, availableHeight * 0.45f);

        System.Diagnostics.Debug.WriteLine($"[DialogueModal] Layout - Available: {ImGui.GetContentRegionAvail().Y}, Footer: {footerHeight}, ChoicesHeader: {choicesHeaderHeight}, DialogueH: {dialogueHeight}, ChoicesH: {choicesHeight}");

        // Dialogue text area with styled background
        // For BeginChild, 0 means "use remaining width", not -1
        ImGui.PushStyleColor(ImGuiCol.ChildBg, UIColors.PanelBgDark);
        ImGui.BeginChild("DialogueTextArea", new Vector2(0, dialogueHeight), ImGuiChildFlags.Borders);

        ImGui.Spacing();
        ImGui.Indent(10 * UIConstants.DpiScale);

        // Render each line of dialogue text
        foreach (var text in _currentState.DialogueText)
        {
            // Style dialogue text
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.95f, 0.9f, 1.0f));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
            ImGui.TextWrapped(text);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.Spacing();
            ImGui.Spacing();
        }

        ImGui.Unindent(10 * UIConstants.DpiScale);
        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.Spacing();

        // Choices/Continue/Farewell section
        if (_currentState.Choices.Count > 0)
        {
            RenderChoices(viewModel, character, modalManager, choicesHeight);
        }
        else if (_currentState.HasEnded)
        {
            RenderFarewellButton(ref isOpen);
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

    private void RenderChoices(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, float height)
    {
        if (_currentState == null) return;

        System.Diagnostics.Debug.WriteLine($"[DialogueModal] RenderChoices called with height={height}, ChoiceCount={_currentState.Choices.Count}");

        ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), "Choose your response:");
        ImGui.Spacing();

        System.Diagnostics.Debug.WriteLine($"[DialogueModal] ChoicesArea position before: {ImGui.GetCursorPosY()}, remaining: {ImGui.GetContentRegionAvail().Y}");
        // For BeginChild, 0 means "use remaining width", not -1
        ImGui.BeginChild("ChoicesArea", new Vector2(0, height), ImGuiChildFlags.None);

        var choiceIndex = 0;
        foreach (var choice in _currentState.Choices)
        {
            choiceIndex++;
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Rendering choice {choiceIndex}: {choice.Text} (Available={choice.IsAvailable})");
            RenderChoice(viewModel, character, modalManager, choice, choiceIndex);
        }

        ImGui.EndChild();
    }

    private void RenderChoice(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, DialogueChoiceOption choice, int index)
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

        // Use Selectable for click handling with custom styling - use frame height for consistent sizing
        // Note: For Selectable size, 0 means "fill remaining width", not -1
        var selectableHeight = ImGui.GetFrameHeight();
        if (ImGui.Selectable($"  {choiceText}##choice_{index}", false, ImGuiSelectableFlags.None, new Vector2(0, selectableHeight)))
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
            ImGui.Indent(30 * UIConstants.DpiScale);
            ImGui.TextColored(new Vector4(1, 0.5f, 0.3f, 1), $"({choice.BlockedReason})");
            ImGui.Unindent(30 * UIConstants.DpiScale);
        }

        ImGui.Spacing();
    }

    private void RenderContinueButton(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager)
    {
        ImGui.Spacing();
        ImGui.Spacing();

        // Full-width continue button using frame height
        var buttonHeight = ImGui.GetFrameHeight() * 1.2f;

        var canContinue = !_isLoading;
        if (!canContinue)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Continue...", new Vector2(ImGuiSizes.Fill, buttonHeight)))
        {
            _isLoading = true;
            _ = AdvanceDialogueAndSetLoadingAsync(viewModel, character, modalManager);
        }

        if (!canContinue)
        {
            ImGui.EndDisabled();
        }
    }

    private void RenderFarewellButton(ref bool isOpen)
    {
        ImGui.Spacing();
        ImGui.Spacing();

        // Full-width farewell button using frame height
        var buttonHeight = ImGui.GetFrameHeight() * 1.2f;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.3f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.5f, 0.4f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.6f, 0.5f, 1));

        if (ImGui.Button("Farewell", new Vector2(ImGuiSizes.Fill, buttonHeight)))
        {
            CloseAndReset(ref isOpen);
        }

        ImGui.PopStyleColor(3);
    }

    private void RenderBottomBar(ref bool isOpen)
    {
        // End conversation button - use available width calculation
        var avail = ImGui.GetContentRegionAvail();
        var buttonWidth = avail.X * 0.3f;
        var buttonHeight = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail.X - buttonWidth);

        ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonDanger);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonDangerHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonDangerActive);

        if (ImGui.Button("Leave", new Vector2(buttonWidth, buttonHeight)))
        {
            CloseAndReset(ref isOpen);
        }

        ImGui.PopStyleColor(3);
    }

    private void CloseAndReset(ref bool isOpen)
    {
        isOpen = false;
        Reset();
    }

    #region Async Operations

    private async Task InitializeDialogueAndSetLoadingAsync(SagaMainViewModel viewModel, CharacterViewModel character)
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

    private async Task SelectChoiceAndSetLoadingAsync(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, string choiceId)
    {
        System.Diagnostics.Debug.WriteLine($"[DialogueModal] === CHOICE CLICKED ===");
        System.Diagnostics.Debug.WriteLine($"[DialogueModal] ChoiceId: {choiceId}");
        System.Diagnostics.Debug.WriteLine($"[DialogueModal] Character: {character.DisplayName} ({character.CharacterRef})");
        System.Diagnostics.Debug.WriteLine($"[DialogueModal] SagaRef: {character.SagaRef}");
        try
        {
            await SelectChoiceAsync(viewModel, character, modalManager, choiceId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] ERROR in SelectChoiceAsync: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Stack: {ex.StackTrace}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task AdvanceDialogueAndSetLoadingAsync(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager)
    {
        System.Diagnostics.Debug.WriteLine($"[DialogueModal] === CONTINUE CLICKED ===");
        System.Diagnostics.Debug.WriteLine($"[DialogueModal] Character: {character.DisplayName} ({character.CharacterRef})");
        System.Diagnostics.Debug.WriteLine($"[DialogueModal] CurrentNode: {_currentState?.CurrentNodeId ?? "null"}");
        try
        {
            await AdvanceDialogueAsync(viewModel, character, modalManager);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] ERROR in AdvanceDialogueAsync: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Stack: {ex.StackTrace}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task InitializeDialogueAsync(SagaMainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.CurrentWorld == null)
        {
            _errorMessage = "No world loaded. Please load a world first.";
            return;
        }

        if (viewModel.Avatar == null)
        {
            _errorMessage = "No avatar selected. Please select an avatar first.";
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Starting dialogue with character {character.CharacterRef}, SagaRef: {character.SagaRef}");

            // Start dialogue (battle triggers may target a specific tree/node)
            var startCommand = new StartDialogueCommand
            {
                AvatarId = viewModel.Avatar.AvatarId,
                SagaArcRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                Avatar = viewModel.Avatar,
                DialogueTreeRefOverride = _startOverride?.TreeRef,
                StartNodeIdOverride = _startOverride?.NodeId
            };
            _startOverride = null;

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

    private async Task SelectChoiceAsync(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, string choiceId)
    {
        if (viewModel.CurrentWorld == null || viewModel.Avatar == null)
            return;

        try
        {
            var command = new SelectDialogueChoiceCommand
            {
                AvatarId = viewModel.Avatar.AvatarId,
                SagaArcRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                ChoiceId = choiceId,
                Avatar = viewModel.Avatar
            };

            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Sending SelectDialogueChoiceCommand...");
            var result = await viewModel.Mediator.Send(command);
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Command result: Successful={result.Successful}, Error={result.ErrorMessage}");
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Result.Data keys: {string.Join(", ", result.Data.Keys)}");

            // Check for game completion
            if (result.Data.ContainsKey(TransactionDataKeys.GameComplete))
            {
                var questRef = result.Data.TryGetValue(TransactionDataKeys.CompletionQuestRef, out var qref) ? qref as string ?? string.Empty : string.Empty;
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Game complete! Quest: {questRef}");
                viewModel.RaiseGameCompleted(questRef);
                return;
            }

            // Route pending system events (battle effects, trade/combat transitions)
            if (result.Data.TryGetValue(TransactionDataKeys.PendingEvents, out var eventsObj) && eventsObj is System.Collections.IList eventsList && eventsList.Count > 0)
            {
                if (ProcessPendingEvents(eventsList, viewModel, character, modalManager))
                {
                    return; // Don't refresh dialogue state - we've transitioned to a different modal
                }
            }

            // No transition events - refresh dialogue state normally
            await RefreshDialogueStateAsync(viewModel, character);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Error selecting choice: {ex.Message}");
            _errorMessage = ex.Message;
        }
    }

    /// <summary>
    /// Routes pending dialogue system events. Battle-affecting events (HealSelf,
    /// ChangeStance, ChangeAffinity, EndBattle — boss taunts and parley outcomes)
    /// are deposited for the BattleModal to apply to the running battle; trade and
    /// combat transition events switch modals as before.
    /// Returns true if the dialogue transitioned to a different modal.
    /// </summary>
    private bool ProcessPendingEvents(System.Collections.IList eventsList, SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager)
    {
        System.Diagnostics.Debug.WriteLine($"[DialogueModal] Processing {eventsList.Count} pending events");

        // Battle-affecting events only matter while a battle is running underneath
        if (modalManager.ShowBossBattle)
        {
            var battleEffects = eventsList.OfType<DialogueSystemEvent>()
                .Where(e => e is HealSelfEvent or ChangeStanceEvent or ChangeAffinityEvent or EndBattleEvent)
                .ToList();

            if (battleEffects.Count > 0)
            {
                modalManager.PendingBattleDialogueEffects ??= new List<DialogueSystemEvent>();
                modalManager.PendingBattleDialogueEffects.AddRange(battleEffects);
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Deposited {battleEffects.Count} battle dialogue effects");
            }
        }

        // Transition events: the first match switches modals (dialogue choices
        // should only carry one transition)
        foreach (var evt in eventsList)
        {
            var eventType = evt!.GetType().Name;

            if (eventType.Contains("OpenMerchantTrade"))
            {
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Opening MerchantTrade modal for {character.DisplayName} ({character.CharacterRef})");
                modalManager.CloseModal("Dialogue");
                modalManager.OpenRegisteredModal("MerchantTrade", new CharacterContext(viewModel, character));
                return true;
            }

            if (eventType.Contains("StartBossBattle") || eventType.Contains("StartCombat"))
            {
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Opening BossBattle modal for {character.DisplayName} ({character.CharacterRef})");
                modalManager.CloseModal("Dialogue");
                modalManager.OpenRegisteredModal("BossBattle", new CharacterContext(viewModel, character));
                return true;
            }
        }

        return false;
    }

    private async Task AdvanceDialogueAsync(SagaMainViewModel viewModel, CharacterViewModel character, ModalManager modalManager)
    {
        if (viewModel.CurrentWorld == null || viewModel.Avatar == null)
            return;

        try
        {
            var command = new AdvanceDialogueCommand
            {
                AvatarId = viewModel.Avatar.AvatarId,
                SagaArcRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                Avatar = viewModel.Avatar
            };

            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Sending AdvanceDialogueCommand...");
            var result = await viewModel.Mediator.Send(command);
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Advance result: Successful={result.Successful}, Error={result.ErrorMessage}");
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Advance Result.Data keys: {string.Join(", ", result.Data.Keys)}");

            if (!result.Successful)
            {
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Advance FAILED: {result.ErrorMessage}");
                _errorMessage = result.ErrorMessage;
                return;
            }

            // Check for game completion
            if (result.Data.ContainsKey(TransactionDataKeys.GameComplete))
            {
                var questRef = result.Data.TryGetValue(TransactionDataKeys.CompletionQuestRef, out var qref) ? qref as string ?? string.Empty : string.Empty;
                System.Diagnostics.Debug.WriteLine($"[DialogueModal] Game complete! Quest: {questRef}");
                viewModel.RaiseGameCompleted(questRef);
                return;
            }

            // Route pending system events (battle effects, trade/combat transitions)
            if (result.Data.TryGetValue(TransactionDataKeys.PendingEvents, out var eventsObj) && eventsObj is System.Collections.IList eventsList && eventsList.Count > 0)
            {
                if (ProcessPendingEvents(eventsList, viewModel, character, modalManager))
                {
                    return;
                }
            }

            // Refresh dialogue state to show next node
            await RefreshDialogueStateAsync(viewModel, character);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Error advancing dialogue: {ex.Message}");
            _errorMessage = ex.Message;
        }
    }

    private async Task RefreshDialogueStateAsync(SagaMainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.CurrentWorld == null || viewModel.Avatar == null)
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] RefreshDialogueState skipped - World or Avatar is null");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[DialogueModal] Refreshing dialogue state for character {character.CharacterInstanceId}");

            var query = new GetDialogueStateQuery
            {
                AvatarId = viewModel.Avatar.AvatarId,
                SagaRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                Avatar = viewModel.Avatar
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
