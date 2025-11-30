using Ambient.Saga.Engine.Domain.Rpg.Dialogue;

namespace Ambient.Saga.Engine.Tests.Rpg.Dialogue;

/// <summary>
/// Mock state provider for testing dialogue system.
/// Tracks all state in memory with simple dictionaries.
/// </summary>
public class MockDialogueStateProvider : IDialogueStateProvider
{
    private readonly HashSet<string> _questTokens = new();
    private readonly Dictionary<string, int> _consumables = new();
    private readonly Dictionary<string, int> _materials = new();
    private readonly HashSet<string> _equipment = new();
    private readonly HashSet<string> _tools = new();
    private readonly HashSet<string> _spells = new();
    private readonly HashSet<string> _achievements = new();
    private readonly Dictionary<string, HashSet<string>> _visitedNodes = new();
    private readonly Dictionary<string, int> _visitCounts = new();
    private readonly Dictionary<string, int> _bossDefeatedCounts = new();
    private readonly Dictionary<string, int?> _traits = new();

    public int Credits { get; set; }
    public int Health { get; set; } = 100;

    // Quest tokens
    public bool HasQuestToken(string questTokenRef) => _questTokens.Contains(questTokenRef);
    public void AddQuestToken(string questTokenRef) => _questTokens.Add(questTokenRef);
    public void RemoveQuestToken(string questTokenRef) => _questTokens.Remove(questTokenRef);

    // Stackable items
    public int GetConsumableQuantity(string consumableRef) => _consumables.GetValueOrDefault(consumableRef, 0);
    public void AddConsumable(string consumableRef, int amount) => _consumables[consumableRef] = GetConsumableQuantity(consumableRef) + amount;
    public void RemoveConsumable(string consumableRef, int amount) => _consumables[consumableRef] = Math.Max(0, GetConsumableQuantity(consumableRef) - amount);

    public int GetMaterialQuantity(string materialRef) => _materials.GetValueOrDefault(materialRef, 0);
    public void AddMaterial(string materialRef, int amount) => _materials[materialRef] = GetMaterialQuantity(materialRef) + amount;
    public void RemoveMaterial(string materialRef, int amount) => _materials[materialRef] = Math.Max(0, GetMaterialQuantity(materialRef) - amount);

    // Blocks (same as materials in mock)
    public void AddBlock(string blockRef, int amount) => _materials[blockRef] = GetMaterialQuantity(blockRef) + amount;
    public void RemoveBlock(string blockRef, int amount) => _materials[blockRef] = Math.Max(0, GetMaterialQuantity(blockRef) - amount);

    // Degradable items
    public bool HasEquipment(string equipmentRef) => _equipment.Contains(equipmentRef);
    public void AddEquipment(string equipmentRef) => _equipment.Add(equipmentRef);
    public void RemoveEquipment(string equipmentRef) => _equipment.Remove(equipmentRef);

    public bool HasTool(string toolRef) => _tools.Contains(toolRef);
    public void AddTool(string toolRef) => _tools.Add(toolRef);
    public void RemoveTool(string toolRef) => _tools.Remove(toolRef);

    public bool HasSpell(string spellRef) => _spells.Contains(spellRef);
    public void AddSpell(string spellRef) => _spells.Add(spellRef);
    public void RemoveSpell(string spellRef) => _spells.Remove(spellRef);

    // Player state
    public bool HasAchievement(string achievementRef) => _achievements.Contains(achievementRef);
    public void UnlockAchievement(string achievementRef) => _achievements.Add(achievementRef);
    public float GetCredits() => Credits;
    public float GetHealth() => Health;
    public void TransferCurrency(int amount) => Credits += amount;

    // Dialogue history
    public int GetPlayerVisitCount(string dialogueTreeRef) => _visitCounts.GetValueOrDefault(dialogueTreeRef, 0);

    public bool WasNodeVisited(string dialogueTreeRef, string nodeId)
    {
        return _visitedNodes.TryGetValue(dialogueTreeRef, out var nodes) && nodes.Contains(nodeId);
    }

    public void RecordNodeVisit(string dialogueTreeRef, string nodeId)
    {
        // Increment visit count for tree
        _visitCounts[dialogueTreeRef] = GetPlayerVisitCount(dialogueTreeRef) + 1;

        // Track specific node visit
        if (!_visitedNodes.ContainsKey(dialogueTreeRef))
            _visitedNodes[dialogueTreeRef] = new HashSet<string>();

        _visitedNodes[dialogueTreeRef].Add(nodeId);
    }

    // Idempotency checking - uses visited nodes tracking
    private readonly HashSet<string> _nodesWithRewardsAwarded = new();

    public bool ShouldAwardNodeRewards(string characterRef, string nodeId)
    {
        var key = $"{characterRef}_{nodeId}";
        if (_nodesWithRewardsAwarded.Contains(key))
            return false; // Already awarded

        // Mark as awarded for next time
        _nodesWithRewardsAwarded.Add(key);
        return true; // First visit, award rewards
    }

    // World state
    public int GetBossDefeatedCount(string bossRef) => _bossDefeatedCounts.GetValueOrDefault(bossRef, 0);

    public void SetBossDefeatedCount(string bossRef, int count) => _bossDefeatedCounts[bossRef] = count;

    // Character traits
    public void AssignTrait(string trait, int? traitValue) => _traits[trait] = traitValue;

    public void RemoveTrait(string trait) => _traits.Remove(trait);

    public bool HasTrait(string trait) => _traits.ContainsKey(trait);

    public int? GetTraitValue(string trait) => _traits.GetValueOrDefault(trait);

    // Character state (stored as special trait)
    public void SetCharacterState(string characterState) => AssignTrait(characterState, null);

    public bool IsQuestActive(string questRef)
    {
        throw new NotImplementedException();
    }

    public bool IsQuestCompleted(string questRef)
    {
        throw new NotImplementedException();
    }

    public bool IsQuestNotStarted(string questRef)
    {
        throw new NotImplementedException();
    }

    public int GetFactionReputation(string factionRef)
    {
        throw new NotImplementedException();
    }

    public string GetFactionReputationLevel(string factionRef)
    {
        throw new NotImplementedException();
    }

    public void ChangeReputation(string factionRef, int amount)
    {
        throw new NotImplementedException();
    }

    // Party management
    private readonly List<string> _partyMembers = new();
    public int MaxPartySlots { get; set; } = 1;

    public int GetPartySize() => _partyMembers.Count;

    public bool HasAvailablePartySlot() => _partyMembers.Count < MaxPartySlots;

    public bool IsInParty(string? characterRef) =>
        !string.IsNullOrEmpty(characterRef) && _partyMembers.Contains(characterRef);

    public bool AddPartyMember(string characterRef)
    {
        if (string.IsNullOrEmpty(characterRef) || IsInParty(characterRef) || !HasAvailablePartySlot())
            return false;

        _partyMembers.Add(characterRef);
        return true;
    }

    public void RemovePartyMember(string characterRef)
    {
        if (!string.IsNullOrEmpty(characterRef))
            _partyMembers.Remove(characterRef);
    }
}
