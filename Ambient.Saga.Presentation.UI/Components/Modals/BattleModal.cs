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

namespace Ambient.Saga.Presentation.UI.Components.Modals;

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

        ImGui.SetNextWindowSize(new Vector2(1200, 800), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"Battle: {character.DisplayName}", ref isOpen))
        {
            if (_currentState == null)
            {
                ImGui.Text("Initializing battle...");
            }
            else if (_currentState.HasEnded)
            {
                RenderBattleEnded(viewModel, character, modalManager);
            }
            else
            {
                RenderActiveBattle(viewModel, character);
            }

            ImGui.End();
        }

        // Render selection modals (as separate windows)
        RenderSelectionModals(viewModel);
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

        var isPlayerTurn = _currentState.BattleState == BattleState.PlayerTurn && !_waitingForEnemyTurn;

        if (!isPlayerTurn)
        {
            if (_waitingForEnemyTurn)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Enemy is thinking...");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Enemy's turn...");
            }
            return;
        }

        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), "COMBAT ACTIONS - Your Turn!");
        ImGui.Spacing();

        // Core combat actions
        if (ImGui.Button("⚔️ Attack", new Vector2(120, 40)))
        {
            _ = ExecuteTurnAsync(viewModel, character, new CombatAction { ActionType = ActionType.Attack });
        }
        ImGui.SameLine();
        if (ImGui.Button("🛡️ Defend", new Vector2(120, 40)))
        {
            _ = ExecuteTurnAsync(viewModel, character, new CombatAction { ActionType = ActionType.Defend });
        }
        ImGui.SameLine();
        if (ImGui.Button("🏃 Flee", new Vector2(120, 40)))
        {
            _ = ExecuteTurnAsync(viewModel, character, new CombatAction { ActionType = ActionType.Flee });
        }
        ImGui.SameLine();
        if (ImGui.Button("💬 Talk", new Vector2(120, 40)))
        {
            OpenMidBattleDialogue(viewModel, character);
        }

        ImGui.Spacing();

        // Advanced combat actions - fully functional
        if (ImGui.Button("✨ Cast Spell", new Vector2(120, 40)))
        {
            OpenSpellSelectionModal(viewModel);
        }
        ImGui.SameLine();
        if (ImGui.Button("💊 Use Item", new Vector2(120, 40)))
        {
            OpenItemSelectionModal(viewModel);
        }
        ImGui.SameLine();
        if (ImGui.Button("⚙️ Change Loadout", new Vector2(140, 40)))
        {
            OpenEquipmentChangeModal(viewModel);
        }
    }

    private void RenderCombatantPanel(Combatant combatant, string title)
    {
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), $"{title}: {combatant.DisplayName}");
        ImGui.Separator();

        // Stats with progress bars
        RenderStatBar("Health", combatant.Health, Combatant.MAX_STAT, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        RenderStatBar("Energy", combatant.Energy, Combatant.MAX_STAT, new Vector4(0.2f, 0.5f, 0.8f, 1.0f));
        RenderStatBar("Strength", combatant.Strength, Combatant.MAX_STAT, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
        RenderStatBar("Defense", combatant.Defense, Combatant.MAX_STAT, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
        RenderStatBar("Speed", combatant.Speed, Combatant.MAX_STAT, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
        RenderStatBar("Magic", combatant.Magic, Combatant.MAX_STAT, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));

        ImGui.Spacing();
        ImGui.Text($"Affinity: {combatant.AffinityRef ?? "None"}");
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

        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), "Battle Log");
        ImGui.Separator();

        ImGui.BeginChild("BattleLogScroll", new Vector2(0, 0), ImGuiChildFlags.Borders);
        foreach (var line in _currentState.BattleLog)
        {
            ImGui.TextWrapped(line);
        }

        // Auto-scroll to bottom
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            ImGui.SetScrollHereY(1.0f);

        ImGui.EndChild();
    }

    private void RenderBattleEnded(MainViewModel viewModel, CharacterViewModel character, ModalManager modalManager)
    {
        if (_currentState == null) return;

        ImGui.Spacing();
        ImGui.Spacing();

        if (_currentState.PlayerVictory == true)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 1.0f, 0.2f, 1.0f));
            var text = "=== VICTORY! ===";
            var textSize = ImGui.CalcTextSize(text);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
            ImGui.Text(text);
            ImGui.PopStyleColor();
        }
        else if (_currentState.PlayerVictory == false)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.2f, 0.2f, 1.0f));
            var text = "=== DEFEAT ===";
            var textSize = ImGui.CalcTextSize(text);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
            ImGui.Text(text);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Show battle log
        ImGui.BeginChild("FinalBattleLog", new Vector2(0, 400), ImGuiChildFlags.Borders);
        foreach (var line in _currentState.BattleLog)
        {
            ImGui.TextWrapped(line);
        }
        ImGui.EndChild();

        ImGui.Spacing();

        // Show loot button if player won
        if (_currentState.PlayerVictory == true)
        {
            if (ImGui.Button("Collect Loot", new Vector2(200, 40)))
            {
                // Transition to loot modal
                modalManager.ShowBossBattle = false;
                modalManager.ShowLoot = true;
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(120, 40)))
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
            battleSetup.AvatarAffinityRefs = new List<string> { "Fire", "Water", "Earth", "Wind" };
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
                PlayerAffinityRefs = new List<string> { "Fire", "Water", "Earth", "Wind" },
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

                // If battle is still ongoing and it's now enemy turn, trigger short delay
                if (_currentState != null && !_currentState.HasEnded && _currentState.BattleState == BattleState.EnemyTurn)
                {
                    _waitingForEnemyTurn = true;
                    _enemyTurnDelay = 0f;
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

            System.Diagnostics.Debug.WriteLine($"[BattleModal] Battle state: {_currentState.BattleState}, Turn: {_currentState.TurnNumber}");
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

        _spellSelectionModal = new SpellSelectionModal(_currentState.PlayerCombatant, viewModel.CurrentWorld);
        _spellSelectionModal.SpellSelected += OnSpellSelected;
        _spellSelectionModal.Cancelled += OnModalCancelled;
        _showSpellSelection = true;
    }

    private void OpenItemSelectionModal(MainViewModel viewModel)
    {
        if (_currentState?.PlayerCombatant == null || viewModel.CurrentWorld == null) return;

        _itemSelectionModal = new ItemSelectionModal(_currentState.PlayerCombatant, viewModel.CurrentWorld);
        _itemSelectionModal.ItemSelected += OnItemSelected;
        _itemSelectionModal.Cancelled += OnModalCancelled;
        _showItemSelection = true;
    }

    private void OpenEquipmentChangeModal(MainViewModel viewModel)
    {
        if (_currentState?.PlayerCombatant == null || viewModel.CurrentWorld == null) return;

        var playerAffinityRefs = new List<string> { "Fire", "Water", "Earth", "Wind" }; // TODO: Get from archetype
        _equipmentChangeModal = new EquipmentChangeModal(_currentState.PlayerCombatant, viewModel.CurrentWorld, playerAffinityRefs);
        _equipmentChangeModal.EquipmentChanged += OnEquipmentChanged;
        _equipmentChangeModal.Cancelled += OnModalCancelled;
        _showEquipmentChange = true;
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
