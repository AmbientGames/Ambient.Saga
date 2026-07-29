using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Domain.Dialogue;

/// <summary>
/// Arc context for dialogue system transaction creation.
/// When provided, DialogueEngine will create Arc transactions for dialogue events.
/// </summary>
public class ArcDialogueContext
{
    /// <summary>
    /// Arc instance where the dialogue is taking place.
    /// Used to commit transactions.
    /// </summary>
    public ArcInstance ArcInstance { get; }

    /// <summary>
    /// Reference to the character being talked to.
    /// </summary>
    public string CharacterRef { get; }

    /// <summary>
    /// Avatar ID of the avatar having the conversation.
    /// </summary>
    public string AvatarId { get; }

    public ArcDialogueContext(ArcInstance arcInstance, string characterRef, string avatarId)
    {
        ArcInstance = arcInstance ?? throw new ArgumentNullException(nameof(arcInstance));
        CharacterRef = characterRef ?? throw new ArgumentNullException(nameof(characterRef));
        AvatarId = avatarId ?? throw new ArgumentNullException(nameof(avatarId));
    }
}
