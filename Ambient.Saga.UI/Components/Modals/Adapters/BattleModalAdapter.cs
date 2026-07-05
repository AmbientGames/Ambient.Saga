using Ambient.Saga.Presentation.UI.ViewModels;

namespace Ambient.Saga.UI.Components.Modals.Adapters;

/// <summary>
/// Adapter for BattleModal to work with the Modal Registry Pattern.
/// </summary>
public class BattleModalAdapter : IModal
{
    private readonly BattleModal _modal = new();
    private readonly ModalManager _modalManager;

    public BattleModalAdapter(ModalManager modalManager)
    {
        _modalManager = modalManager ?? throw new ArgumentNullException(nameof(modalManager));
    }

    /// <summary>
    /// Subscribe to be notified when the avatar is defeated in battle.
    /// </summary>
    public event Action? AvatarDefeated
    {
        add => _modal.AvatarDefeated += value;
        remove => _modal.AvatarDefeated -= value;
    }

    /// <summary>
    /// Fired after the battle modal closes on a VICTORY, carrying the defeated
    /// character. ModalManager subscribes and opens the victory-loot trade modal
    /// (free-take mode) if the character has any remaining Loot.
    /// </summary>
    public event Action<SagaMainViewModel, CharacterViewModel>? VictoryLootAvailable;

    public string Name => "BossBattle";

    /// <summary>
    /// Per-frame tick for the rendered BattleModal instance (reaction countdown,
    /// enemy-turn delay clearing, pending battle dialogue effects).
    /// Called by ModalManager.Update while the battle modal is open.
    /// </summary>
    public void Update(float deltaTime)
    {
        _modal.Update(deltaTime);
    }

    public bool CanOpen(object? context)
    {
        return context is CharacterModalContext or CharacterContext;
    }

    public void OnOpening(object? context)
    {
        if (context is CharacterModalContext ctx)
        {
            System.Diagnostics.Debug.WriteLine($"[BattleModal] Opening battle with: {ctx.Character.DisplayName}");
        }
        else if (context is CharacterContext ctx2)
        {
            System.Diagnostics.Debug.WriteLine($"[BattleModal] Opening battle with: {ctx2.Character.DisplayName}");
        }
    }

    public void Render(object? context, ref bool isOpen)
    {
        CharacterViewModel? character = null;
        SagaMainViewModel? viewModel = null;

        if (context is CharacterModalContext ctx)
        {
            character = ctx.Character;
            viewModel = ctx.ViewModel;
        }
        else if (context is CharacterContext ctx2)
        {
            character = ctx2.Character;
            viewModel = ctx2.ViewModel;
        }

        if (viewModel != null && character != null)
        {
            _modal.Render(viewModel, character, _modalManager, ref isOpen);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[BattleModal] Invalid context, closing");
            isOpen = false;
        }
    }

    public void OnClosed()
    {
        System.Diagnostics.Debug.WriteLine("[BattleModal] Closed");

        // Drain the victory-loot candidate BEFORE Reset clears it: a battle that
        // ended in victory offers the defeated character's remaining Loot.
        var victoryLoot = _modal.TakeVictoryLootCandidate();

        // Reset battle state so re-engaging the same enemy starts a fresh battle
        // instead of showing the previous battle's end screen.
        _modal.Reset();

        if (victoryLoot != null)
        {
            VictoryLootAvailable?.Invoke(victoryLoot.Value.ViewModel, victoryLoot.Value.Character);
        }
    }
}
