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
    public string CharacterArchetypeRef { get; init; } = string.Empty;
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
