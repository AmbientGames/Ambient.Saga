namespace Ambient.Saga.Engine.Application.Results.Saga;

/// <summary>
/// The single highest-priority interaction that wants to engage with the avatar.
/// Returns null/empty if nothing nearby wants to interact.
/// </summary>
public class InitiatedInteractionResult
{
    public bool HasInteraction { get; set; }
    public string SagaRef { get; set; } = string.Empty;
    public InteractableCharacter? Character { get; set; }
    public InteractableFeature? Feature { get; set; }
    public double Distance { get; set; }
    public int Priority { get; set; }
}
