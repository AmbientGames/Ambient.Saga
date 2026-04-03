namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Validates incoming transactions against the current derived SagaState.
/// Used server-side to reject fraudulent or inconsistent transactions before persisting.
/// Each rule mirrors the precondition checks from the corresponding client-side handler.
/// </summary>
public static class SagaTransactionValidator
{
    /// <summary>
    /// Validates a transaction against the current state.
    /// Returns (true, null) if valid, (false, reason) if invalid.
    /// Unknown or unvalidated transaction types are allowed by default.
    /// </summary>
    public static (bool IsValid, string? Reason) Validate(SagaState state, SagaTransaction transaction)
    {
        return transaction.Type switch
        {
            // Character lifecycle
            SagaTransactionType.CharacterDamaged => ValidateCharacterDamaged(state, transaction),
            SagaTransactionType.CharacterDefeated => ValidateCharacterDefeated(state, transaction),
            SagaTransactionType.CharacterHealed => ValidateCharacterAlive(state, transaction, "heal"),
            SagaTransactionType.CharacterDespawned => ValidateCharacterExists(state, transaction),
            SagaTransactionType.LootAwarded => ValidateLootAwarded(state, transaction),

            // Quest lifecycle
            SagaTransactionType.QuestAccepted => ValidateQuestAccepted(state, transaction),
            SagaTransactionType.QuestCompleted => ValidateQuestCompleted(state, transaction),
            SagaTransactionType.QuestFailed => ValidateQuestActive(state, transaction),
            SagaTransactionType.QuestAbandoned => ValidateQuestActive(state, transaction),
            SagaTransactionType.QuestStageAdvanced => ValidateQuestActive(state, transaction),
            SagaTransactionType.QuestBranchChosen => ValidateQuestBranchChosen(state, transaction),
            SagaTransactionType.QuestObjectiveCompleted => ValidateQuestActive(state, transaction),

            // Trading
            SagaTransactionType.ItemTraded => ValidateCharacterAlive(state, transaction, "trade with"),

            // Saga lifecycle
            SagaTransactionType.SagaCompleted => ValidateSagaCompleted(state),

            // Everything else: allow by default
            _ => (true, null)
        };
    }

    private static (bool, string?) ValidateCharacterDamaged(SagaState state, SagaTransaction tx)
    {
        var id = tx.GetData<string>("CharacterInstanceId");
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.TryGetValue(id, out var character))
            return (false, $"Character '{id}' not found");

        if (!character.IsAlive)
            return (false, $"Cannot damage dead character '{id}'");

        return (true, null);
    }

    private static (bool, string?) ValidateCharacterDefeated(SagaState state, SagaTransaction tx)
    {
        var id = tx.GetData<string>("CharacterInstanceId");
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.TryGetValue(id, out var character))
            return (false, $"Character '{id}' not found");

        if (!character.IsAlive)
            return (false, $"Character '{id}' already defeated");

        return (true, null);
    }

    private static (bool, string?) ValidateCharacterAlive(SagaState state, SagaTransaction tx, string action)
    {
        var id = tx.GetData<string>("CharacterInstanceId");
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.TryGetValue(id, out var character))
            return (false, $"Character '{id}' not found");

        if (!character.IsAlive)
            return (false, $"Cannot {action} dead character '{id}'");

        return (true, null);
    }

    private static (bool, string?) ValidateCharacterExists(SagaState state, SagaTransaction tx)
    {
        var id = tx.GetData<string>("CharacterInstanceId");
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.ContainsKey(id))
            return (false, $"Character '{id}' not found");

        return (true, null);
    }

    private static (bool, string?) ValidateLootAwarded(SagaState state, SagaTransaction tx)
    {
        var id = tx.GetData<string>("CharacterInstanceId");
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.TryGetValue(id, out var character))
            return (false, $"Character '{id}' not found");

        if (character.IsAlive)
            return (false, $"Cannot loot living character '{id}'");

        if (character.HasBeenLooted)
            return (false, $"Character '{id}' already looted");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestAccepted(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        if (string.IsNullOrEmpty(questRef))
            return (false, "Missing QuestRef");

        if (state.ActiveQuests.ContainsKey(questRef))
            return (false, $"Quest '{questRef}' already accepted");

        if (state.CompletedQuests.Contains(questRef))
            return (false, $"Quest '{questRef}' already completed");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestCompleted(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        if (string.IsNullOrEmpty(questRef))
            return (false, "Missing QuestRef");

        if (state.CompletedQuests.Contains(questRef))
            return (false, $"Quest '{questRef}' already completed");

        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return (false, $"Quest '{questRef}' not active");

        if (questState.IsFailed)
            return (false, $"Quest '{questRef}' has failed — cannot complete");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestActive(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        if (string.IsNullOrEmpty(questRef))
            return (false, "Missing QuestRef");

        if (!state.ActiveQuests.ContainsKey(questRef))
            return (false, $"Quest '{questRef}' not active");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestBranchChosen(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        if (string.IsNullOrEmpty(questRef))
            return (false, "Missing QuestRef");

        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return (false, $"Quest '{questRef}' not active");

        if (!string.IsNullOrEmpty(questState.ChosenBranch))
            return (false, $"Quest '{questRef}' already has branch '{questState.ChosenBranch}' chosen");

        return (true, null);
    }

    private static (bool, string?) ValidateSagaCompleted(SagaState state)
    {
        if (state.Status == SagaStatus.Completed)
            return (false, "Saga already completed");

        if (state.Status == SagaStatus.Undiscovered)
            return (false, "Saga not yet discovered");

        return (true, null);
    }
}
