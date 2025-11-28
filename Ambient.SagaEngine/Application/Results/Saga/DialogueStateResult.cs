namespace Ambient.SagaEngine.Application.Results.Saga;

/// <summary>
/// Current state of a dialogue interaction.
/// Derived from transaction log replay.
/// </summary>
public class DialogueStateResult
{
    /// <summary>
    /// Whether dialogue is active
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Current dialogue node ID
    /// </summary>
    public string CurrentNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Text to display (NPC speech)
    /// </summary>
    public List<string> DialogueText { get; set; } = new();

    /// <summary>
    /// Available player choices
    /// </summary>
    public List<DialogueChoiceOption> Choices { get; set; } = new();

    /// <summary>
    /// Whether player can continue (no choices, just advance)
    /// </summary>
    public bool CanContinue { get; set; }

    /// <summary>
    /// Whether dialogue has ended
    /// </summary>
    public bool HasEnded { get; set; }
}

/// <summary>
/// A dialogue choice option available to the player
/// </summary>
public class DialogueChoiceOption
{
    /// <summary>
    /// Unique ID for this choice
    /// </summary>
    public string ChoiceId { get; set; } = string.Empty;

    /// <summary>
    /// Display text for the choice
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Whether this choice is available (meets requirements)
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Why choice is blocked (if not available)
    /// </summary>
    public string? BlockedReason { get; set; }

    /// <summary>
    /// Cost to select this choice (quest tokens, etc.)
    /// </summary>
    public int Cost { get; set; }
}
