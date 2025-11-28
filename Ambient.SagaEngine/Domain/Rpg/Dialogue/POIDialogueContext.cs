using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Domain.Rpg.Dialogue;

/// <summary>
/// Saga context for dialogue system transaction creation.
/// When provided, DialogueEngine will create Saga transactions for dialogue events.
/// </summary>
public class SagaDialogueContext
{
    /// <summary>
    /// Saga instance where the dialogue is taking place.
    /// Used to commit transactions.
    /// </summary>
    public SagaInstance SagaInstance { get; }

    /// <summary>
    /// Reference to the character being talked to.
    /// </summary>
    public string CharacterRef { get; }

    /// <summary>
    /// Avatar ID of the player having the conversation.
    /// </summary>
    public string AvatarId { get; }

    public SagaDialogueContext(SagaInstance sagaInstance, string characterRef, string avatarId)
    {
        SagaInstance = sagaInstance ?? throw new ArgumentNullException(nameof(sagaInstance));
        CharacterRef = characterRef ?? throw new ArgumentNullException(nameof(characterRef));
        AvatarId = avatarId ?? throw new ArgumentNullException(nameof(avatarId));
    }
}
