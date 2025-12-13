using Ambient.Domain;
using Ambient.Domain.Entities;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Engine;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal for interactive turn-by-turn battles using CQRS pattern.
/// Creates transactions for every turn, enabling achievement tracking and battle replay.
/// </summary>
public class BattleModal
{
    private BattleStateResult? _currentState;
    private bool _isInitialized = false;
    private Guid _lastCharacterInstanceId;
    private Guid _battleInstanceId = Guid.Empty;
    private float _enemyTurnDelay = 0f;
    private const float ENEMY_TURN_DELAY_TIME = 0.5f;  // Half second delay for enemy turn
    private bool _waitingForEnemyTurn = false;

    // Reaction phase state (Expedition 33-inspired defense mechanics)
    private bool _inReactionPhase = false;
    private string? _currentTellText = null;
    private float _reactionTimeRemaining = 0f;
    private float _reactionTimeTotal = 0f;  // Total window time (from tell or default)
    private const float DEFAULT_REACTION_WINDOW_SECONDS = 5.0f;  // Fallback if tell doesn't specify
    private PlayerDefenseType? _selectedReaction = null;
    private bool _reactionResolved = false;

    // Selection modal instances
    private SpellSelectionModal? _spellSelectionModal;
    private ItemSelectionModal? _itemSelectionModal;
    private EquipmentChangeModal? _equipmentChangeModal;

    // Selection modal state
    private bool _showSpellSelection = false;
    private bool _showItemSelection = false;
    private bool _showEquipmentChange = false;

    // Cached references for modal callbacks
    private MainViewModel? _cachedViewModel;
    private CharacterViewModel? _cachedCharacter;
    private ModalManager? _cachedModalManager;

    public void Update(float deltaTime)
    {
        // Handle reaction phase countdown
        if (_inReactionPhase && !_reactionResolved)
        {
            _reactionTimeRemaining -= deltaTime;
            if (_reactionTimeRemaining <= 0f)
            {
                // Time expired - auto-select None (no reaction)
                _reactionTimeRemaining = 0f;
                _selectedReaction = PlayerDefenseType.None;
                _reactionResolved = true;
                System.Diagnostics.Debug.WriteLine("[BattleModal] Reaction window expired - no defense");
            }
        }

        // Handle enemy turn delay
        if (_waitingForEnemyTurn)
        {
            _enemyTurnDelay += deltaTime;
            if (_enemyTurnDelay >= ENEMY_TURN_DELAY_TIME)
            {
                _enemyTurnDelay = 0f;
                _waitingForEnemyTurn = false;
                // State will automatically refresh and show PlayerTurn
            }
        }
    }

    public void Render(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager, ref bool isOpen)
    {
        if (!isOpen)
        {
            _isInitialized = false;
            _battleInstanceId = Guid.Empty;
            _showSpellSelection = false;
            _showItemSelection = false;
            _showEquipmentChange = false;
            EndReactionPhase();  // Reset reaction state
            return;
        }

        // Cache references for modal callbacks
        _cachedViewModel = viewModel;
        _cachedCharacter = character;
        _cachedModalManager = modalManager;

        // Initialize battle on first render for this character
        if (!_isInitialized || _lastCharacterInstanceId != character.CharacterInstanceId)
        {
            _isInitialized = true;
            _lastCharacterInstanceId = character.CharacterInstanceId;
            _ = InitializeBattleAsync(viewModel, character);
        }

        // Center the window
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(1100, 750), ImGuiCond.FirstUseEver);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);

        var windowFlags = ImGuiWindowFlags.NoCollapse;

        if (ImGui.Begin($"Battle: {character.DisplayName}###BattleModal", ref isOpen, windowFlags))
        {
            if (_currentState == null)
            {
                RenderLoading();
            }
            else if (_currentState.HasEnded)
            {
                RenderBattleEnded(viewModel, character, modalManager);
            }
            else
            {
                RenderActiveBattle(viewModel, character);
            }
        }
        ImGui.End();

        ImGui.PopStyleVar(2);

        // Render selection modals (as separate windows)
        RenderSelectionModals(viewModel);
    }

    private void RenderLoading()
    {
        ImGui.Spacing();
        ImGui.Spacing();
        var loadingText = "Preparing for battle...";
        var textSize = ImGui.CalcTextSize(loadingText);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), loadingText);
    }

    private void RenderActiveBattle(MainViewModel viewModel, CharacterViewModel character)
    {
        if (_currentState == null) return;

        var player = _currentState.PlayerCombatant;
        var enemy = _currentState.EnemyCombatant;

        if (player == null || enemy == null) return;

        // Action buttons at top
        RenderActionButtons(viewModel, character);
        ImGui.Separator();

        // Three-column layout
        if (ImGui.BeginTable("BattleLayout", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthFixed, 300);
            ImGui.TableSetupColumn("Battle Log", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Opponent", ImGuiTableColumnFlags.WidthFixed, 300);

            ImGui.TableNextRow();

            // Left column: Player stats
            ImGui.TableNextColumn();
            RenderCombatantPanel(player, "Player");

            // Middle column: Battle log
            ImGui.TableNextColumn();
            RenderBattleLog();

            // Right column: Opponent stats
            ImGui.TableNextColumn();
            RenderCombatantPanel(enemy, "Opponent");

            ImGui.EndTable();
        }
    }

    private void RenderActionButtons(MainViewModel viewModel, CharacterViewModel character)
    {
        if (_currentState == null) return;

        var isPlayerTurn = _currentState.BattleState == BattleState.PlayerTurn && !_waitingForEnemyTurn && !_inReactionPhase;

        // Header showing whose turn it is
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.15f, 0.8f));

        // Increase height for reaction phase to fit tell text + timer + buttons
        var headerHeight = _inReactionPhase ? 160f : 90f;
        ImGui.BeginChild("TurnHeader", new Vector2(0, headerHeight), ImGuiChildFlags.Borders);

        // Reaction phase - show tell text, countdown, and reaction buttons
        if (_inReactionPhase)
        {
            RenderReactionPanel(viewModel, character);
        }
        else if (!isPlayerTurn)
        {
            ImGui.Spacing();
            var turnText = _waitingForEnemyTurn ? "Enemy is thinking..." : "Enemy's turn...";
            var textSize = ImGui.CalcTextSize(turnText);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 25);
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), turnText);
        }
        else
        {
            ImGui.Spacing();
            ImGui.SetWindowFontScale(1.1f);
            var turnText = "YOUR TURN - Choose an Action";
            var textSize = ImGui.CalcTextSize(turnText);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.4f, 1.0f), turnText);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.Spacing();

            // Center the action buttons
            var buttonWidth = 110f;
            var buttonSpacing = 8f;
            var totalWidth = buttonWidth * 7 + buttonSpacing * 6 + 20; // 7 buttons + extra for loadout
            var startX = (ImGui.GetWindowWidth() - totalWidth) * 0.5f;
            ImGui.SetCursorPosX(startX);

            // Core combat actions with styled buttons
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.15f, 0.15f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.2f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.3f, 0.3f, 1.0f));
            if (ImGui.Button("Attack", new Vector2(buttonWidth, 35)))
            {
                _ = ExecuteTurnAsync(viewModel, character, new CombatAction { ActionType = ActionType.Attack });
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.25f, 0.35f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.35f, 0.5f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.45f, 0.65f, 1.0f));
            if (ImGui.Button("Defend", new Vector2(buttonWidth, 35)))
            {
                _ = ExecuteTurnAsync(viewModel, character, new CombatAction { ActionType = ActionType.Defend });
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.2f, 0.5f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.3f, 0.65f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.55f, 0.4f, 0.8f, 1.0f));
            if (ImGui.Button("Cast Spell", new Vector2(buttonWidth, 35)))
            {
                OpenSpellSelectionModal(viewModel);
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.35f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.5f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.65f, 0.4f, 1.0f));
            if (ImGui.Button("Use Item", new Vector2(buttonWidth, 35)))
            {
                OpenItemSelectionModal(viewModel);
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.35f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.5f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.65f, 0.65f, 0.4f, 1.0f));
            if (ImGui.Button("Talk", new Vector2(buttonWidth, 35)))
            {
                OpenMidBattleDialogue(viewModel, character);
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
            if (ImGui.Button("Loadout", new Vector2(buttonWidth, 35)))
            {
                OpenEquipmentChangeModal(viewModel);
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.3f, 0.15f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.55f, 0.4f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.5f, 0.25f, 1.0f));
            if (ImGui.Button("Flee", new Vector2(buttonWidth, 35)))
            {
                _ = ExecuteTurnAsync(viewModel, character, new CombatAction { ActionType = ActionType.Flee });
            }
            ImGui.PopStyleColor(3);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Renders the reaction panel during enemy attack phase.
    /// Shows the tell text, countdown timer, and reaction buttons.
    /// Inspired by Expedition 33's active defense mechanics.
    /// </summary>
    private void RenderReactionPanel(MainViewModel viewModel, CharacterViewModel character)
    {
        // Tell text - narrative preview of incoming attack
        ImGui.Spacing();
        ImGui.SetWindowFontScale(1.1f);
        var tellText = _currentTellText ?? "The enemy prepares to strike...";
        var tellSize = ImGui.CalcTextSize(tellText);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - tellSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), tellText);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.Spacing();

        // Countdown timer bar
        var timeRatio = _reactionTimeTotal > 0 ? _reactionTimeRemaining / _reactionTimeTotal : 0f;
        var timerColor = timeRatio > 0.5f ? new Vector4(0.3f, 0.8f, 0.3f, 1.0f) :
                         timeRatio > 0.25f ? new Vector4(0.9f, 0.8f, 0.2f, 1.0f) :
                         new Vector4(0.9f, 0.3f, 0.2f, 1.0f);

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, timerColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.15f, 0.2f, 1.0f));

        var timerText = $"REACT! {_reactionTimeRemaining:F1}s";
        var barWidth = ImGui.GetWindowWidth() * 0.6f;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - barWidth) * 0.5f);
        ImGui.ProgressBar(timeRatio, new Vector2(barWidth, 24), timerText);

        ImGui.PopStyleColor(2);
        ImGui.Spacing();

        // Reaction buttons - Dodge, Block, Parry, Brace
        var buttonWidth = 120f;
        var buttonSpacing = 15f;
        var totalWidth = buttonWidth * 4 + buttonSpacing * 3;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - totalWidth) * 0.5f);

        // Dodge button (cyan/teal - evasion)
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.1f, 0.4f, 0.4f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.15f, 0.55f, 0.55f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.2f, 0.7f, 0.7f, 1.0f));
        if (ImGui.Button("Dodge", new Vector2(buttonWidth, 40)))
        {
            SelectReaction(PlayerDefenseType.Dodge, viewModel, character);
        }
        ImGui.PopStyleColor(3);

        ImGui.SameLine();

        // Block button (blue - defense)
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.25f, 0.45f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.35f, 0.6f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.25f, 0.45f, 0.75f, 1.0f));
        if (ImGui.Button("Block", new Vector2(buttonWidth, 40)))
        {
            SelectReaction(PlayerDefenseType.Block, viewModel, character);
        }
        ImGui.PopStyleColor(3);

        ImGui.SameLine();

        // Parry button (gold - counter)
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.45f, 0.35f, 0.1f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.5f, 0.15f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.75f, 0.65f, 0.2f, 1.0f));
        if (ImGui.Button("Parry", new Vector2(buttonWidth, 40)))
        {
            SelectReaction(PlayerDefenseType.Parry, viewModel, character);
        }
        ImGui.PopStyleColor(3);

        ImGui.SameLine();

        // Brace button (purple - tank)
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.2f, 0.45f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.3f, 0.6f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.65f, 0.4f, 0.75f, 1.0f));
        if (ImGui.Button("Brace", new Vector2(buttonWidth, 40)))
        {
            SelectReaction(PlayerDefenseType.Brace, viewModel, character);
        }
        ImGui.PopStyleColor(3);

        // Process resolved reaction
        if (_reactionResolved && _selectedReaction.HasValue)
        {
            _ = ResolveReactionAsync(viewModel, character, _selectedReaction.Value);
            _reactionResolved = false;  // Prevent multiple calls
        }
    }

    private void SelectReaction(PlayerDefenseType reaction, MainViewModel viewModel, CharacterViewModel character)
    {
        _selectedReaction = reaction;
        _reactionResolved = true;
        System.Diagnostics.Debug.WriteLine($"[BattleModal] Player selected reaction: {reaction}");
    }

    /// <summary>
    /// Starts the reaction phase with a given tell text and timing.
    /// Call this when enemy attack is telegraphed.
    /// </summary>
    public void StartReactionPhase(string tellText, int reactionWindowMs = 0)
    {
        _inReactionPhase = true;
        _currentTellText = tellText;
        _reactionTimeTotal = reactionWindowMs > 0 ? reactionWindowMs / 1000f : DEFAULT_REACTION_WINDOW_SECONDS;
        _reactionTimeRemaining = _reactionTimeTotal;
        _selectedReaction = null;
        _reactionResolved = false;
        System.Diagnostics.Debug.WriteLine($"[BattleModal] Starting reaction phase: '{tellText}' ({_reactionTimeTotal}s)");
    }

    /// <summary>
    /// Ends the reaction phase and resets state.
    /// </summary>
    private void EndReactionPhase()
    {
        _inReactionPhase = false;
        _currentTellText = null;
        _reactionTimeRemaining = 0f;
        _reactionTimeTotal = 0f;
        _selectedReaction = null;
        _reactionResolved = false;
    }

    // Default tells for when enemy doesn't have specific tells defined
    private static readonly string[] DefaultTellTemplates = new[]
    {
        "{0} lunges forward with weapon raised!",
        "{0} draws back for a powerful strike!",
        "{0} crouches low, preparing to spring!",
        "{0} snarls and raises a claw!",
        "{0} takes a deep breath, energy gathering!",
        "{0} shifts stance and locks eyes with you!",
        "{0} winds up for a sweeping attack!",
        "{0} feints left, then commits to an attack!",
        "{0} lets out a battle cry and charges!",
        "{0} coils back, muscles tensing!"
    };

    private static readonly Random _tellRandom = new();

    /// <summary>
    /// Generates a random default tell narrative for an enemy.
    /// Used when character doesn't have specific AttackTells defined.
    /// </summary>
    private static string GetRandomDefaultTell(string enemyName)
    {
        var template = DefaultTellTemplates[_tellRandom.Next(DefaultTellTemplates.Length)];
        return string.Format(template, enemyName);
    }

    private async Task ResolveReactionAsync(MainViewModel viewModel, CharacterViewModel character, PlayerDefenseType reaction)
    {
        if (viewModel.PlayerAvatar == null || _battleInstanceId == Guid.Empty)
            return;

        System.Diagnostics.Debug.WriteLine($"[BattleModal] Resolving reaction: {reaction}");

        try
        {
            // Send reaction to backend via command
            var command = new SubmitReactionCommand
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaArcRef = character.SagaRef,
                BattleInstanceId = _battleInstanceId,
                Reaction = reaction,
                Avatar = viewModel.PlayerAvatar
            };

            var result = await viewModel.Mediator.Send(command);

            if (result.Successful)
            {
                System.Diagnostics.Debug.WriteLine($"[BattleModal] Reaction {reaction} submitted successfully");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[BattleModal] Failed to submit reaction: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BattleModal] Error submitting reaction: {ex.Message}");
        }

        // End reaction phase regardless of result
        EndReactionPhase();

        // Resume normal enemy turn flow
        _waitingForEnemyTurn = true;
        _enemyTurnDelay = 0f;

        // Refresh battle state to get updated results
        await RefreshBattleStateAsync(viewModel, character);
    }

    private void RenderCombatantPanel(Combatant combatant, string title)
    {
        var isPlayer = title == "Player";
        var titleColor = isPlayer ? new Vector4(0.4f, 0.9f, 0.4f, 1.0f) : new Vector4(1.0f, 0.4f, 0.4f, 1.0f);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.1f, 0.9f));
        ImGui.BeginChild($"{title}Panel", new Vector2(0, 0), ImGuiChildFlags.Borders);

        // Combatant name header
        ImGui.Spacing();
        ImGui.SetWindowFontScale(1.1f);
        ImGui.TextColored(titleColor, combatant.DisplayName);
        ImGui.SetWindowFontScale(1.0f);

        // Show affinity if available
        if (!string.IsNullOrEmpty(combatant.AffinityRef))
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.7f, 1.0f, 1.0f), $"({combatant.AffinityRef})");
        }

        ImGui.Separator();
        ImGui.Spacing();

        // Vital stats with colored progress bars
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.6f, 1.0f), "Vitals:");
        RenderStatBar("Health", combatant.Health, Combatant.MAX_STAT, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        RenderStatBar("Energy", combatant.Energy, Combatant.MAX_STAT, new Vector4(0.2f, 0.5f, 0.8f, 1.0f));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Combat stats
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.6f, 1.0f), "Combat Stats:");
        RenderStatBar("Strength", combatant.Strength, Combatant.MAX_STAT, new Vector4(0.8f, 0.5f, 0.3f, 1.0f));
        RenderStatBar("Defense", combatant.Defense, Combatant.MAX_STAT, new Vector4(0.4f, 0.6f, 0.8f, 1.0f));
        RenderStatBar("Speed", combatant.Speed, Combatant.MAX_STAT, new Vector4(0.3f, 0.8f, 0.5f, 1.0f));
        RenderStatBar("Magic", combatant.Magic, Combatant.MAX_STAT, new Vector4(0.6f, 0.3f, 0.8f, 1.0f));

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void RenderStatBar(string name, float value, float maxValue, Vector4 color)
    {
        ImGui.Text($"{name}:");
        ImGui.SameLine(100);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(value / maxValue, new Vector2(-1, 20), $"{value:F2} / {maxValue:F2}");
        ImGui.PopStyleColor();
    }

    private void RenderBattleLog()
    {
        if (_currentState == null) return;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.08f, 0.9f));
        ImGui.BeginChild("BattleLogContainer", new Vector2(0, 0), ImGuiChildFlags.Borders);

        // Header
        ImGui.Spacing();
        var headerText = "Battle Log";
        var headerSize = ImGui.CalcTextSize(headerText);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - headerSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.5f, 1.0f), headerText);

        // Turn counter
        var turnText = $"Turn {_currentState.TurnNumber}";
        var turnSize = ImGui.CalcTextSize(turnText);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - turnSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), turnText);

        ImGui.Separator();
        ImGui.Spacing();

        // Log entries
        ImGui.BeginChild("BattleLogScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        foreach (var line in _currentState.BattleLog)
        {
            // Color-code log entries based on content
            var color = new Vector4(0.85f, 0.85f, 0.85f, 1.0f);
            if (line.Contains("damage") || line.Contains("hit"))
            {
                color = new Vector4(1.0f, 0.6f, 0.4f, 1.0f);
            }
            else if (line.Contains("healed") || line.Contains("restored"))
            {
                color = new Vector4(0.4f, 1.0f, 0.6f, 1.0f);
            }
            else if (line.Contains("defended") || line.Contains("blocked"))
            {
                color = new Vector4(0.4f, 0.7f, 1.0f, 1.0f);
            }
            else if (line.Contains("fled") || line.Contains("escaped"))
            {
                color = new Vector4(1.0f, 0.9f, 0.4f, 1.0f);
            }
            else if (line.Contains("defeated") || line.Contains("victory") || line.Contains("Victory"))
            {
                color = new Vector4(0.3f, 1.0f, 0.3f, 1.0f);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(line);
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        // Auto-scroll to bottom
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 10)
            ImGui.SetScrollHereY(1.0f);

        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void RenderBattleEnded(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager)
    {
        if (_currentState == null) return;

        // Large centered victory/defeat message
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        if (_currentState.PlayerVictory == true)
        {
            ImGui.SetWindowFontScale(1.5f);
            var text = "VICTORY!";
            var textSize = ImGui.CalcTextSize(text);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f), text);
            ImGui.SetWindowFontScale(1.0f);

            ImGui.Spacing();
            var subText = "You have defeated your opponent!";
            var subSize = ImGui.CalcTextSize(subText);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - subSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), subText);
        }
        else if (_currentState.PlayerVictory == false)
        {
            ImGui.SetWindowFontScale(1.5f);
            var text = "DEFEAT";
            var textSize = ImGui.CalcTextSize(text);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), text);
            ImGui.SetWindowFontScale(1.0f);

            ImGui.Spacing();
            var subText = "You have been defeated...";
            var subSize = ImGui.CalcTextSize(subText);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - subSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.7f, 1.0f), subText);
        }

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Battle log header
        var logHeader = "Battle Summary";
        var logHeaderSize = ImGui.CalcTextSize(logHeader);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - logHeaderSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.5f, 1.0f), logHeader);
        ImGui.Spacing();

        // Show battle log with styled background
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.08f, 0.9f));
        ImGui.BeginChild("FinalBattleLog", new Vector2(0, -60), ImGuiChildFlags.Borders);

        foreach (var line in _currentState.BattleLog)
        {
            // Color-code log entries
            var color = new Vector4(0.85f, 0.85f, 0.85f, 1.0f);
            if (line.Contains("damage") || line.Contains("hit"))
            {
                color = new Vector4(1.0f, 0.6f, 0.4f, 1.0f);
            }
            else if (line.Contains("healed") || line.Contains("restored"))
            {
                color = new Vector4(0.4f, 1.0f, 0.6f, 1.0f);
            }
            else if (line.Contains("defeated") || line.Contains("victory") || line.Contains("Victory"))
            {
                color = new Vector4(0.3f, 1.0f, 0.3f, 1.0f);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextWrapped(line);
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.Spacing();

        // Center the action buttons
        var buttonWidth = 150f;
        var totalButtonWidth = _currentState.PlayerVictory == true ? buttonWidth * 2 + 20 : buttonWidth;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - totalButtonWidth) * 0.5f);

        // Show loot button if player won
        if (_currentState.PlayerVictory == true)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1.0f));
            if (ImGui.Button("Collect Loot", new Vector2(buttonWidth, 40)))
            {
                // Transition to loot modal
                modalManager.ShowBossBattle = false;
                modalManager.ShowLoot = true;
            }
            ImGui.PopStyleColor(3);
            ImGui.SameLine();
        }

        if (ImGui.Button("Close", new Vector2(buttonWidth, 40)))
        {
            modalManager.ShowBossBattle = false;
        }
    }

    private async Task InitializeBattleAsync(MainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.CurrentWorld == null || viewModel.PlayerAvatar == null)
            return;

        try
        {
            var characterTemplate = viewModel.CurrentWorld.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == character.CharacterRef);
            if (characterTemplate == null) return;

            var archetypeRef = viewModel.PlayerAvatar.ArchetypeRef;
            var archetype = viewModel.CurrentWorld.Gameplay?.AvatarArchetypes?.FirstOrDefault(a => a.RefName == archetypeRef);
            if (archetype == null) return;

            // Create combatants
            var battleSetup = new BattleSetup();
            battleSetup.SetupFromWorld(viewModel.CurrentWorld);
            battleSetup.SelectedAvatarArchetype = archetype;
            battleSetup.AvatarCapabilities = viewModel.PlayerAvatar.Capabilities ?? new ItemCollection();
            // Get available affinities from world configuration (fallback to archetype affinity if defined)
            var availableAffinities = viewModel.CurrentWorld.Gameplay?.CharacterAffinities?
                .Select(a => a.RefName).ToList() ?? new List<string>();
            if (availableAffinities.Count == 0 && !string.IsNullOrEmpty(archetype.AffinityRef))
            {
                availableAffinities.Add(archetype.AffinityRef);
            }
            battleSetup.AvatarAffinityRefs = availableAffinities;
            battleSetup.SelectedOpponentCharacter = characterTemplate;
            battleSetup.OpponentCapabilities = characterTemplate.Capabilities ?? new ItemCollection();

            var battleEngine = battleSetup.CreateBattleEngine();

            // Get combatants from engine (they're configured by BattleSetup)
            var playerCombatant = battleEngine.GetPlayer();
            var enemyCombatant = battleEngine.GetEnemy();

            // Send StartBattleCommand
            var startCommand = new StartBattleCommand
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaArcRef = character.SagaRef,
                EnemyCharacterInstanceId = character.CharacterInstanceId,
                PlayerCombatant = playerCombatant,
                EnemyCombatant = enemyCombatant,
                PlayerAffinityRefs = availableAffinities,
                EnemyMind = new CombatAI(viewModel.CurrentWorld),
                RandomSeed = new Random().Next(),
                Avatar = viewModel.PlayerAvatar
            };

            var result = await viewModel.Mediator.Send(startCommand);

            if (result.Successful && result.Data.TryGetValue("BattleInstanceId", out var battleIdObj))
            {
                _battleInstanceId = (Guid)battleIdObj;
                System.Diagnostics.Debug.WriteLine($"[BattleModal] Battle started with ID {_battleInstanceId}");

                // Get initial battle state
                await RefreshBattleStateAsync(viewModel, character);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[BattleModal] Failed to start battle: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BattleModal] Error initializing battle: {ex.Message}");
        }
    }

    private async Task ExecuteTurnAsync(MainViewModel viewModel, CharacterViewModel character, CombatAction action)
    {
        if (viewModel.PlayerAvatar == null || _battleInstanceId == Guid.Empty)
            return;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[BattleModal] Executing turn: {action.ActionType}");

            var command = new ExecuteBattleTurnCommand
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaArcRef = character.SagaRef,
                BattleInstanceId = _battleInstanceId,
                PlayerAction = action,
                Avatar = viewModel.PlayerAvatar
            };

            var result = await viewModel.Mediator.Send(command);

            if (result.Successful)
            {
                // Refresh battle state
                await RefreshBattleStateAsync(viewModel, character);

                // If battle is still ongoing and it's now enemy turn, trigger reaction phase
                // NOTE: Currently the backend executes enemy turn immediately, so this is UI-only simulation
                // For full integration, the backend would need to pause before enemy turn
                if (_currentState != null && !_currentState.HasEnded && _currentState.BattleState == BattleState.EnemyTurn)
                {
                    // Check if backend provided a tell (from IsAwaitingReaction state)
                    if (_currentState.IsAwaitingReaction && !string.IsNullOrEmpty(_currentState.PendingTellText))
                    {
                        StartReactionPhase(_currentState.PendingTellText, _currentState.PendingReactionWindowMs);
                    }
                    else
                    {
                        // Generate a default tell for the enemy based on their character
                        var enemyName = _currentState.EnemyCombatant?.DisplayName ?? "The enemy";
                        var defaultTell = GetRandomDefaultTell(enemyName);
                        StartReactionPhase(defaultTell, 5000);  // 5 second default
                    }
                }

                // Update avatar from result if battle ended
                if (result.UpdatedAvatar != null && viewModel.PlayerAvatar is AvatarEntity)
                {
                    // Avatar was updated - refresh UI
                    System.Diagnostics.Debug.WriteLine("[BattleModal] Battle ended, avatar updated");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[BattleModal] Failed to execute turn: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BattleModal] Error executing turn: {ex.Message}");
        }
    }

    private async Task RefreshBattleStateAsync(MainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.PlayerAvatar == null || _battleInstanceId == Guid.Empty)
            return;

        try
        {
            var query = new GetBattleStateQuery
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaRef = character.SagaRef,
                BattleInstanceId = _battleInstanceId,
                Avatar = viewModel.PlayerAvatar
            };

            _currentState = await viewModel.Mediator.Send(query);

            System.Diagnostics.Debug.WriteLine($"[BattleModal] Battle state: {_currentState.BattleState}, Turn: {_currentState.TurnNumber}, AwaitingReaction: {_currentState.IsAwaitingReaction}");

            // Check if we need to enter reaction phase
            if (_currentState.IsAwaitingReaction && !_inReactionPhase && !string.IsNullOrEmpty(_currentState.PendingTellText))
            {
                StartReactionPhase(_currentState.PendingTellText, _currentState.PendingReactionWindowMs);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BattleModal] Error refreshing battle state: {ex.Message}");
        }
    }

    // Modal opening methods
    private void OpenSpellSelectionModal(MainViewModel viewModel)
    {
        if (_currentState?.PlayerCombatant == null || viewModel.CurrentWorld == null) return;

        // Clean up previous instance to avoid memory leaks
        CleanupSpellSelectionModal();

        _spellSelectionModal = new SpellSelectionModal(_currentState.PlayerCombatant, viewModel.CurrentWorld);
        _spellSelectionModal.SpellSelected += OnSpellSelected;
        _spellSelectionModal.Cancelled += OnModalCancelled;
        _showSpellSelection = true;
    }

    private void OpenItemSelectionModal(MainViewModel viewModel)
    {
        if (_currentState?.PlayerCombatant == null || viewModel.CurrentWorld == null) return;

        // Clean up previous instance to avoid memory leaks
        CleanupItemSelectionModal();

        _itemSelectionModal = new ItemSelectionModal(_currentState.PlayerCombatant, viewModel.CurrentWorld);
        _itemSelectionModal.ItemSelected += OnItemSelected;
        _itemSelectionModal.Cancelled += OnModalCancelled;
        _showItemSelection = true;
    }

    private void OpenEquipmentChangeModal(MainViewModel viewModel)
    {
        if (_currentState?.PlayerCombatant == null || viewModel.CurrentWorld == null) return;

        // Clean up previous instance to avoid memory leaks
        CleanupEquipmentChangeModal();

        // Get available affinities from world configuration
        var playerAffinityRefs = viewModel.CurrentWorld.Gameplay?.CharacterAffinities?
            .Select(a => a.RefName).ToList() ?? new List<string>();
        _equipmentChangeModal = new EquipmentChangeModal(_currentState.PlayerCombatant, viewModel.CurrentWorld, playerAffinityRefs);
        _equipmentChangeModal.EquipmentChanged += OnEquipmentChanged;
        _equipmentChangeModal.Cancelled += OnModalCancelled;
        _showEquipmentChange = true;
    }

    // Cleanup methods to prevent memory leaks from event handlers
    private void CleanupSpellSelectionModal()
    {
        if (_spellSelectionModal != null)
        {
            _spellSelectionModal.SpellSelected -= OnSpellSelected;
            _spellSelectionModal.Cancelled -= OnModalCancelled;
            _spellSelectionModal = null;
        }
    }

    private void CleanupItemSelectionModal()
    {
        if (_itemSelectionModal != null)
        {
            _itemSelectionModal.ItemSelected -= OnItemSelected;
            _itemSelectionModal.Cancelled -= OnModalCancelled;
            _itemSelectionModal = null;
        }
    }

    private void CleanupEquipmentChangeModal()
    {
        if (_equipmentChangeModal != null)
        {
            _equipmentChangeModal.EquipmentChanged -= OnEquipmentChanged;
            _equipmentChangeModal.Cancelled -= OnModalCancelled;
            _equipmentChangeModal = null;
        }
    }

    private void OpenMidBattleDialogue(MainViewModel viewModel, CharacterViewModel character)
    {
        if (_cachedModalManager == null) return;

        // Open dialogue modal while keeping battle active
        // Battle stays in background, dialogue appears on top
        _cachedModalManager.ShowDialogue = true;
        _cachedModalManager.SelectedCharacter = character;

        System.Diagnostics.Debug.WriteLine($"[BattleModal] Opening mid-battle dialogue with {character.DisplayName}");
    }

    // Modal event handlers
    private void OnSpellSelected(string spellRef)
    {
        _showSpellSelection = false;
        if (_cachedViewModel != null && _cachedCharacter != null)
        {
            _ = ExecuteTurnAsync(_cachedViewModel, _cachedCharacter, new CombatAction
            {
                ActionType = ActionType.CastSpell,
                Parameter = spellRef
            });
        }
    }

    private void OnItemSelected(string itemRef)
    {
        _showItemSelection = false;
        if (_cachedViewModel != null && _cachedCharacter != null)
        {
            _ = ExecuteTurnAsync(_cachedViewModel, _cachedCharacter, new CombatAction
            {
                ActionType = ActionType.UseConsumable,
                Parameter = itemRef
            });
        }
    }

    private void OnEquipmentChanged(string changeParameter)
    {
        _showEquipmentChange = false;
        if (_cachedViewModel != null && _cachedCharacter != null)
        {
            _ = ExecuteTurnAsync(_cachedViewModel, _cachedCharacter, new CombatAction
            {
                ActionType = ActionType.ChangeLoadout,
                Parameter = changeParameter
            });
        }
    }

    private void OnModalCancelled()
    {
        _showSpellSelection = false;
        _showItemSelection = false;
        _showEquipmentChange = false;
    }

    // Render selection modals
    private void RenderSelectionModals(MainViewModel viewModel)
    {
        // Spell selection modal
        if (_showSpellSelection && _spellSelectionModal != null)
        {
            ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Select Spell", ref _showSpellSelection, ImGuiWindowFlags.Modal))
            {
                _spellSelectionModal.Render();
                ImGui.End();
            }

            if (!_showSpellSelection)
            {
                _spellSelectionModal = null;
            }
        }

        // Item selection modal
        if (_showItemSelection && _itemSelectionModal != null)
        {
            ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Select Item", ref _showItemSelection, ImGuiWindowFlags.Modal))
            {
                _itemSelectionModal.Render();
                ImGui.End();
            }

            if (!_showItemSelection)
            {
                _itemSelectionModal = null;
            }
        }

        // Equipment change modal
        if (_showEquipmentChange && _equipmentChangeModal != null)
        {
            ImGui.SetNextWindowSize(new Vector2(500, 700), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Change Loadout", ref _showEquipmentChange, ImGuiWindowFlags.Modal))
            {
                _equipmentChangeModal.Render();
                ImGui.End();
            }

            if (!_showEquipmentChange)
            {
                _equipmentChangeModal = null;
            }
        }
    }
}
