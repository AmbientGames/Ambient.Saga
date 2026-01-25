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
    private readonly Dictionary<string, int> _blocks = new();
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

    // Provider-based item access (unified pattern)
    private readonly Dictionary<string, Dictionary<string, int>> _providerItems = new()
    {
        ["Equipment"] = new(),
        ["Consumables"] = new(),
        ["Spells"] = new(),
        ["QuestTokens"] = new()
    };

    public bool HasItem(string provider, string refName) => GetItemQuantity(provider, refName) > 0;

    public int GetItemQuantity(string provider, string refName)
    {
        if (_providerItems.TryGetValue(provider, out var items))
            return items.GetValueOrDefault(refName, 0);
        return 0;
    }

    public void GiveItem(string provider, string refName, int quantity = 1)
    {
        if (!_providerItems.ContainsKey(provider))
            _providerItems[provider] = new();

        var items = _providerItems[provider];
        items[refName] = items.GetValueOrDefault(refName, 0) + quantity;
    }

    public void TakeItem(string provider, string refName, int quantity = 1)
    {
        if (_providerItems.TryGetValue(provider, out var items))
        {
            var current = items.GetValueOrDefault(refName, 0);
            items[refName] = Math.Max(0, current - quantity);
        }
    }

    // Legacy test helpers - map to provider pattern
    public bool HasQuestToken(string questTokenRef) => HasItem("QuestTokens", questTokenRef);
    public void AddQuestToken(string questTokenRef) => GiveItem("QuestTokens", questTokenRef);
    public void RemoveQuestToken(string questTokenRef) => TakeItem("QuestTokens", questTokenRef);

    public int GetConsumableQuantity(string consumableRef) => GetItemQuantity("Consumables", consumableRef);
    public void AddConsumable(string consumableRef, int amount) => GiveItem("Consumables", consumableRef, amount);
    public void RemoveConsumable(string consumableRef, int amount) => TakeItem("Consumables", consumableRef, amount);

    public bool HasEquipment(string equipmentRef) => HasItem("Equipment", equipmentRef);
    public void AddEquipment(string equipmentRef) => GiveItem("Equipment", equipmentRef);
    public void RemoveEquipment(string equipmentRef) => TakeItem("Equipment", equipmentRef);

    public bool HasSpell(string spellRef) => HasItem("Spells", spellRef);
    public void AddSpell(string spellRef) => GiveItem("Spells", spellRef);
    public void RemoveSpell(string spellRef) => TakeItem("Spells", spellRef);

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

    /// <summary>
    /// Test helper to set a trait value directly.
    /// </summary>
    public void SetTraitValue(string trait, int value) => _traits[trait] = value;

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

    // Faction reputation
    private readonly Dictionary<string, int> _factionReputation = new();

    public int GetFactionReputation(string factionRef) => _factionReputation.GetValueOrDefault(factionRef, 0);

    public string GetFactionReputationLevel(string factionRef)
    {
        var rep = GetFactionReputation(factionRef);
        return rep switch
        {
            < -6000 => "Hated",
            < -3000 => "Hostile",
            < 0 => "Unfriendly",
            < 3000 => "Neutral",
            < 6000 => "Friendly",
            < 12000 => "Honored",
            < 21000 => "Revered",
            _ => "Exalted"
        };
    }

    public void ChangeReputation(string factionRef, int amount)
    {
        _factionReputation[factionRef] = GetFactionReputation(factionRef) + amount;
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

    // Affinity management
    private readonly Dictionary<string, string> _affinities = new(); // AffinityRef -> CapturedFromCharacterRef

    public bool HasAffinity(string affinityRef) => _affinities.ContainsKey(affinityRef);

    public void AddAffinity(string affinityRef, string capturedFromCharacterRef)
    {
        if (!string.IsNullOrEmpty(affinityRef) && !_affinities.ContainsKey(affinityRef))
            _affinities[affinityRef] = capturedFromCharacterRef;
    }

    /// <summary>
    /// Test helper to get the character who granted an affinity.
    /// </summary>
    public string? GetAffinitySource(string affinityRef) =>
        _affinities.TryGetValue(affinityRef, out var source) ? source : null;
}
