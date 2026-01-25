namespace Ambient.Saga.Engine.Domain.Rpg.Dialogue;

/// <summary>
/// Abstraction for querying player and world state during dialogue.
/// Implementations should provide access to inventory, achievements, quest progress, etc.
/// Uses a provider-based pattern for items - providers can be Saga-owned (Equipment, Consumables,
/// Spells, QuestTokens) or application-defined via IGameplayItemProvider (e.g., "Tools (Core)", "Blocks (Core)").
/// </summary>
public interface IDialogueStateProvider
{
    // ===== PROVIDER-BASED ITEM ACCESS =====
    // These methods handle all item types through a unified provider pattern.
    // Built-in Saga providers: "Equipment", "Consumables", "Spells", "QuestTokens"
    // External providers registered via IGameplayItemProvider: e.g., "Tools (Core)", "Blocks (Core)"

    /// <summary>
    /// Checks if avatar has an item from the specified provider.
    /// For quantity-based items (Consumables), checks quantity > 0.
    /// For presence-based items (Equipment, Spells, QuestTokens), checks existence.
    /// </summary>
    bool HasItem(string provider, string refName);

    /// <summary>
    /// Gets the quantity of an item from the specified provider.
    /// For presence-based items, returns 1 if present, 0 if not.
    /// </summary>
    int GetItemQuantity(string provider, string refName);

    /// <summary>
    /// Gives an item to the avatar from the specified provider.
    /// For quantity-based items, adds to quantity.
    /// For presence-based items, adds if not already present.
    /// </summary>
    void GiveItem(string provider, string refName, int quantity = 1);

    /// <summary>
    /// Takes an item from the avatar for the specified provider.
    /// For quantity-based items, reduces quantity.
    /// For presence-based items, removes if present.
    /// </summary>
    void TakeItem(string provider, string refName, int quantity = 1);

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

    // ===== CURRENCY & ACHIEVEMENTS =====
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

    // ===== PARTY MANAGEMENT =====
    /// <summary>
    /// Gets the current party size.
    /// </summary>
    int GetPartySize();

    /// <summary>
    /// Checks if a party slot is available.
    /// </summary>
    bool HasAvailablePartySlot();

    /// <summary>
    /// Checks if a character is in the party.
    /// </summary>
    /// <param name="characterRef">Character to check (null/empty checks current dialogue character)</param>
    bool IsInParty(string? characterRef);

    /// <summary>
    /// Adds a character to the party.
    /// </summary>
    /// <param name="characterRef">Character to add</param>
    /// <returns>True if successful, false if no slot available or already in party</returns>
    bool AddPartyMember(string characterRef);

    /// <summary>
    /// Removes a character from the party.
    /// </summary>
    /// <param name="characterRef">Character to remove</param>
    void RemovePartyMember(string characterRef);

    // ===== AFFINITY MANAGEMENT =====
    /// <summary>
    /// Checks if the avatar has a specific affinity.
    /// </summary>
    /// <param name="affinityRef">Affinity to check</param>
    bool HasAffinity(string affinityRef);

    /// <summary>
    /// Grants an affinity to the avatar, captured from a character.
    /// </summary>
    /// <param name="affinityRef">Affinity to grant</param>
    /// <param name="capturedFromCharacterRef">Character the affinity was captured from</param>
    void AddAffinity(string affinityRef, string capturedFromCharacterRef);
}
