namespace Ambient.SagaEngine.Domain.Rpg.Dialogue;

/// <summary>
/// Abstraction for querying player and world state during dialogue.
/// Implementations should provide access to inventory, achievements, quest progress, etc.
/// </summary>
public interface IDialogueStateProvider
{
    // ===== QUEST TOKENS =====
    bool HasQuestToken(string questTokenRef);

    // ===== STACKABLE ITEMS (quantity matters) =====
    int GetConsumableQuantity(string consumableRef);
    int GetMaterialQuantity(string materialRef);

    // ===== DEGRADABLE ITEMS (existence matters) =====
    bool HasEquipment(string equipmentRef);
    bool HasTool(string toolRef);
    bool HasSpell(string spellRef);

    // ===== PLAYER STATE =====
    bool HasAchievement(string achievementRef);
    float GetCredits();
    float GetHealth();

    // ===== DIALOGUE HISTORY =====
    int GetPlayerVisitCount(string dialogueTreeRef);
    bool WasNodeVisited(string dialogueTreeRef, string nodeId);

    // ===== WORLD STATE =====
    int GetBossDefeatedCount(string bossRef);

    // ===== QUEST STATE =====
    bool IsQuestActive(string questRef);
    bool IsQuestCompleted(string questRef);
    bool IsQuestNotStarted(string questRef);

    // ===== FACTION REPUTATION =====
    int GetFactionReputation(string factionRef);
    string GetFactionReputationLevel(string factionRef);  // Returns ReputationLevel as string
    void ChangeReputation(string factionRef, int amount);

    // ===== INVENTORY MODIFICATION =====
    void AddConsumable(string consumableRef, int amount);
    void RemoveConsumable(string consumableRef, int amount);
    void AddMaterial(string materialRef, int amount);
    void RemoveMaterial(string materialRef, int amount);
    void AddBlock(string blockRef, int amount);
    void RemoveBlock(string blockRef, int amount);

    void AddEquipment(string equipmentRef);
    void RemoveEquipment(string equipmentRef);
    void AddTool(string toolRef);
    void RemoveTool(string toolRef);
    void AddSpell(string spellRef);
    void RemoveSpell(string spellRef);

    void AddQuestToken(string questTokenRef);
    void RemoveQuestToken(string questTokenRef);

    void TransferCurrency(int amount);
    void UnlockAchievement(string achievementRef);

    // ===== CHARACTER STATE =====
    void SetCharacterState(string characterState);

    // ===== CHARACTER TRAITS =====
    int? GetTraitValue(string trait);
    void AssignTrait(string trait, int? traitValue);
    void RemoveTrait(string trait);

    // ===== DIALOGUE TRACKING =====
    void RecordNodeVisit(string dialogueTreeRef, string nodeId);

    /// <summary>
    /// Checks if rewards should be awarded for this dialogue node.
    /// Returns true if this is the first visit (or if idempotency not supported).
    /// Returns false if already visited and rewards were already given.
    /// </summary>
    /// <param name="characterRef">Character whose dialogue tree is being navigated</param>
    /// <param name="nodeId">Dialogue node being visited</param>
    bool ShouldAwardNodeRewards(string characterRef, string nodeId);
}
