namespace Ambient.Saga.Engine.Domain.Rpg.Dialogue.Events;

/// <summary>
/// Base class for all dialogue system events.
/// These events are raised when dialogue actions require system transitions.
/// </summary>
public abstract class DialogueSystemEvent
{
    public string DialogueTreeRef { get; init; } = string.Empty;
    public string NodeId { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests opening merchant trade UI.
/// </summary>
public class OpenMerchantTradeEvent : DialogueSystemEvent
{
    public string CharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests starting a boss battle encounter.
/// Boss battles are special arena combat with unique mechanics.
/// </summary>
public class StartBossBattleEvent : DialogueSystemEvent
{
    public string CharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests starting general combat.
/// </summary>
public class StartCombatEvent : DialogueSystemEvent
{
    public string CharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests spawning characters in the world.
/// </summary>
public class SpawnCharactersEvent : DialogueSystemEvent
{
    public string CharacterRef { get; init; } = string.Empty;
    public int Amount { get; init; }
}

/// <summary>
/// Raised when dialogue requests accepting a quest.
/// Handler should call AcceptQuestCommand via CQRS.
/// </summary>
public class AcceptQuestEvent : DialogueSystemEvent
{
    public string QuestRef { get; init; } = string.Empty;
    public string SagaRef { get; init; } = string.Empty;
    public string QuestGiverRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests completing a quest.
/// Handler should call CompleteQuestCommand via CQRS.
/// </summary>
public class CompleteQuestEvent : DialogueSystemEvent
{
    public string QuestRef { get; init; } = string.Empty;
    public string SagaRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests abandoning a quest.
/// Handler should call AbandonQuestCommand via CQRS (when implemented).
/// </summary>
public class AbandonQuestEvent : DialogueSystemEvent
{
    public string QuestRef { get; init; } = string.Empty;
    public string SagaRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when a character joins the player's party via dialogue.
/// </summary>
public class PartyMemberJoinedEvent : DialogueSystemEvent
{
    public string CharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when a character leaves the player's party via dialogue.
/// </summary>
public class PartyMemberLeftEvent : DialogueSystemEvent
{
    public string CharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests changing faction reputation.
/// </summary>
public class ReputationChangedEvent : DialogueSystemEvent
{
    public string FactionRef { get; init; } = string.Empty;
    public int Amount { get; init; }
}

/// <summary>
/// Raised when dialogue requests changing combat stance mid-battle.
/// </summary>
public class ChangeStanceEvent : DialogueSystemEvent
{
    public string StanceRef { get; init; } = string.Empty;
    public string CharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests changing elemental affinity mid-battle.
/// </summary>
public class ChangeAffinityEvent : DialogueSystemEvent
{
    public string AffinityRef { get; init; } = string.Empty;
    public string CharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests casting a spell mid-battle.
/// </summary>
public class CastSpellEvent : DialogueSystemEvent
{
    public string SpellRef { get; init; } = string.Empty;
    public string CharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests summoning an ally to battle.
/// </summary>
public class SummonAllyEvent : DialogueSystemEvent
{
    public string CharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests ending the current battle.
/// </summary>
public class EndBattleEvent : DialogueSystemEvent
{
    /// <summary>
    /// Result of the battle: "Victory", "Defeat", "Flee", "Draw"
    /// </summary>
    public string Result { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue requests healing a character.
/// </summary>
public class HealSelfEvent : DialogueSystemEvent
{
    public string CharacterRef { get; init; } = string.Empty;
    /// <summary>
    /// Heal amount as a percentage (0-100) or absolute value depending on context.
    /// </summary>
    public int Amount { get; init; }
}

/// <summary>
/// Raised when dialogue requests applying a status effect.
/// </summary>
public class ApplyStatusEffectEvent : DialogueSystemEvent
{
    public string StatusEffectRef { get; init; } = string.Empty;
    public string TargetCharacterRef { get; init; } = string.Empty;
}

/// <summary>
/// Raised when dialogue grants a character affinity to the avatar.
/// </summary>
public class AffinityGrantedEvent : DialogueSystemEvent
{
    public string AffinityRef { get; init; } = string.Empty;
    public string CapturedFromCharacterRef { get; init; } = string.Empty;
}
