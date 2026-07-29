namespace Ambient.Rpg.Engine.Application.Results.Arcs;

/// <summary>
/// The single highest-priority interaction that wants to engage with the avatar.
/// Returns null/empty if nothing nearby wants to interact.
/// </summary>
public class InitiatedInteractionResult
{
    public bool HasInteraction { get; set; }
    public string ArcRef { get; set; } = string.Empty;
    public InteractableCharacter? Character { get; set; }
    public double Distance { get; set; }
    public int Priority { get; set; }

    /// <summary>
    /// The winning character initiates battle (proximity assault): effective traits
    /// include Hostile with no truce trait (Disengaged/Spared). Mirrors
    /// Character.Options.IsAssault so view models don't reach into trait logic.
    /// </summary>
    public bool IsAssault { get; set; }
}
