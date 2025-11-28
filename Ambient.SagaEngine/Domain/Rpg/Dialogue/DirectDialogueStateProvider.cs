using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Domain.Rpg.Reputation;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Domain.Rpg.Dialogue;

/// <summary>
/// Direct implementation of IDialogueStateProvider that manipulates World and Avatar state directly.
/// This provider can be used by any UI framework (WPF, Console, Web, etc.) that has access to domain objects.
/// </summary>
public class DirectDialogueStateProvider : IDialogueStateProvider
{
    private readonly World _w;
    private readonly AvatarBase _a;
    private readonly Dictionary<string, HashSet<string>> _visited = new();
    private readonly Dictionary<string, int?> _traits = new();
    private readonly Func<string, SagaState?>? _getSagaStateFunc;
    private readonly string? _avatarId;
    private string? _currentCharacterRef;

    public DirectDialogueStateProvider(
        World w,
        AvatarBase a,
        Func<string, SagaState?>? getSagaStateFunc = null,
        string? avatarId = null,
        string? characterRef = null)
    {
        _w = w;
        _a = a;
        _getSagaStateFunc = getSagaStateFunc;
        _avatarId = avatarId;
        _currentCharacterRef = characterRef;
    }

    /// <summary>
    /// Sets the character reference for the current dialogue.
    /// This is used for idempotency checking.
    /// </summary>
    public void SetCurrentCharacter(string characterRef)
    {
        _currentCharacterRef = characterRef;
    }

    // Quest Tokens
    public bool HasQuestToken(string r) => _a.Capabilities?.QuestTokens?.Any(e => e.QuestTokenRef == r) ?? false;
    public void AddQuestToken(string r) { if (_a.Capabilities?.QuestTokens != null && !HasQuestToken(r)) { var list = _a.Capabilities.QuestTokens.ToList(); list.Add(new QuestTokenEntry { QuestTokenRef = r }); _a.Capabilities.QuestTokens = list.ToArray(); } }
    public void RemoveQuestToken(string r) { if (_a.Capabilities?.QuestTokens != null) _a.Capabilities.QuestTokens = _a.Capabilities.QuestTokens.Where(e => e.QuestTokenRef != r).ToArray(); }

    // Consumables (stackable)
    public int GetConsumableQuantity(string r) => _a.Capabilities?.Consumables?.FirstOrDefault(e => e.ConsumableRef == r)?.Quantity ?? 0;
    public void AddConsumable(string r, int amt) { if (_a.Capabilities?.Consumables != null && amt > 0) { var e = _a.Capabilities.Consumables.FirstOrDefault(x => x.ConsumableRef == r); if (e != null) e.Quantity += amt; else { var list = _a.Capabilities.Consumables.ToList(); list.Add(new ConsumableEntry { ConsumableRef = r, Quantity = amt }); _a.Capabilities.Consumables = list.ToArray(); } } }
    public void RemoveConsumable(string r, int amt) { if (_a.Capabilities?.Consumables != null && amt > 0) { var e = _a.Capabilities.Consumables.FirstOrDefault(x => x.ConsumableRef == r); if (e != null) { e.Quantity = Math.Max(0, e.Quantity - amt); if (e.Quantity == 0) _a.Capabilities.Consumables = _a.Capabilities.Consumables.Where(x => x.ConsumableRef != r).ToArray(); } } }

    // Materials (stackable)
    public int GetMaterialQuantity(string r) => _a.Capabilities?.BuildingMaterials?.FirstOrDefault(e => e.BuildingMaterialRef == r)?.Quantity ?? 0;
    public void AddMaterial(string r, int amt) { if (_a.Capabilities?.BuildingMaterials != null && amt > 0) { var e = _a.Capabilities.BuildingMaterials.FirstOrDefault(x => x.BuildingMaterialRef == r); if (e != null) e.Quantity += amt; else { var list = _a.Capabilities.BuildingMaterials.ToList(); list.Add(new BuildingMaterialEntry { BuildingMaterialRef = r, Quantity = amt }); _a.Capabilities.BuildingMaterials = list.ToArray(); } } }
    public void RemoveMaterial(string r, int amt) { if (_a.Capabilities?.BuildingMaterials != null && amt > 0) { var e = _a.Capabilities.BuildingMaterials.FirstOrDefault(x => x.BuildingMaterialRef == r); if (e != null) { e.Quantity = Math.Max(0, e.Quantity - amt); if (e.Quantity == 0) _a.Capabilities.BuildingMaterials = _a.Capabilities.BuildingMaterials.Where(x => x.BuildingMaterialRef != r).ToArray(); } } }

    // Blocks (stackable voxel blocks - same as materials)
    public void AddBlock(string r, int amt) { if (_a.Capabilities?.BuildingMaterials != null && amt > 0) { var e = _a.Capabilities.BuildingMaterials.FirstOrDefault(x => x.BuildingMaterialRef == r); if (e != null) e.Quantity += amt; else { var list = _a.Capabilities.BuildingMaterials.ToList(); list.Add(new BuildingMaterialEntry { BuildingMaterialRef = r, Quantity = amt }); _a.Capabilities.BuildingMaterials = list.ToArray(); } } }
    public void RemoveBlock(string r, int amt) { if (_a.Capabilities?.BuildingMaterials != null && amt > 0) { var e = _a.Capabilities.BuildingMaterials.FirstOrDefault(x => x.BuildingMaterialRef == r); if (e != null) { e.Quantity = Math.Max(0, e.Quantity - amt); if (e.Quantity == 0) _a.Capabilities.BuildingMaterials = _a.Capabilities.BuildingMaterials.Where(x => x.BuildingMaterialRef != r).ToArray(); } } }

    // Equipment (degradable)
    public bool HasEquipment(string r) => _a.Capabilities?.Equipment?.Any(e => e.EquipmentRef == r) ?? false;
    public void AddEquipment(string r) { if (_a.Capabilities?.Equipment != null && !HasEquipment(r)) { var list = _a.Capabilities.Equipment.ToList(); list.Add(new EquipmentEntry { EquipmentRef = r, Condition = 1.0f }); _a.Capabilities.Equipment = list.ToArray(); } }
    public void RemoveEquipment(string r) { if (_a.Capabilities?.Equipment != null) { var e = _a.Capabilities.Equipment.FirstOrDefault(x => x.EquipmentRef == r); if (e != null) { var list = _a.Capabilities.Equipment.ToList(); list.Remove(e); _a.Capabilities.Equipment = list.ToArray(); } } }

    // Tools (degradable)
    public bool HasTool(string r) => _a.Capabilities?.Tools?.Any(e => e.ToolRef == r) ?? false;
    public void AddTool(string r) { if (_a.Capabilities?.Tools != null && !HasTool(r)) { var list = _a.Capabilities.Tools.ToList(); list.Add(new ToolEntry { ToolRef = r, Condition = 1.0f }); _a.Capabilities.Tools = list.ToArray(); } }
    public void RemoveTool(string r) { if (_a.Capabilities?.Tools != null) { var e = _a.Capabilities.Tools.FirstOrDefault(x => x.ToolRef == r); if (e != null) { var list = _a.Capabilities.Tools.ToList(); list.Remove(e); _a.Capabilities.Tools = list.ToArray(); } } }

    // Spells (degradable)
    public bool HasSpell(string r) => _a.Capabilities?.Spells?.Any(e => e.SpellRef == r) ?? false;
    public void AddSpell(string r) { if (_a.Capabilities?.Spells != null && !HasSpell(r)) { var list = _a.Capabilities.Spells.ToList(); list.Add(new SpellEntry { SpellRef = r, Condition = 1.0f }); _a.Capabilities.Spells = list.ToArray(); } }
    public void RemoveSpell(string r) { if (_a.Capabilities?.Spells != null) { var e = _a.Capabilities.Spells.FirstOrDefault(x => x.SpellRef == r); if (e != null) { var list = _a.Capabilities.Spells.ToList(); list.Remove(e); _a.Capabilities.Spells = list.ToArray(); } } }

    // Achievements
    public bool HasAchievement(string r) => _a.Achievements?.Any(e => e.AchievementRef == r) ?? false;
    public void UnlockAchievement(string r) { if (_a.Achievements != null && !HasAchievement(r)) { var list = _a.Achievements.ToList(); list.Add(new AchievementEntry { AchievementRef = r }); _a.Achievements = list.ToArray(); } }

    // Currency & Health
    public float GetCredits() => _a.Stats.Credits;
    public void TransferCurrency(int amt) { if (_a.Stats != null) _a.Stats.Credits += amt; }
    public float GetHealth() => _a.Stats.Health;
    public void ModifyHealth(int amt) { if (_a.Stats != null) _a.Stats.Health = Math.Max(0, _a.Stats.Health + amt); }

    // Dialogue History
    public int GetPlayerVisitCount(string t) => _visited.ContainsKey(t) ? 1 : 0;
    public void RecordNodeVisit(string t, string n) { if (!_visited.ContainsKey(t)) _visited[t] = new HashSet<string>(); _visited[t].Add(n); }
    public bool WasNodeVisited(string t, string n) => _visited.ContainsKey(t) && _visited[t].Contains(n);

    /// <summary>
    /// Gets boss defeated count by querying Saga state machine.
    /// Requires getSagaStateFunc to be injected for this to work.
    /// </summary>
    public int GetBossDefeatedCount(string bossRef)
    {
        if (_getSagaStateFunc == null)
            return 0; // No Saga state provider - return 0 (not defeated)

        // Find Saga instances that might contain this boss
        // For now, check all Sagas - in a real implementation, you'd have a mapping
        foreach (var saga in _w.Gameplay?.SagaArcs ?? Array.Empty<SagaArc>())
        {
            var state = _getSagaStateFunc(saga.RefName);
            if (state != null)
            {
                // Check if this character exists in the Saga state and is defeated
                foreach (var character in state.Characters.Values)
                {
                    if (character.CharacterRef == bossRef && !character.IsAlive)
                    {
                        return 1; // Boss defeated
                    }
                }
            }
        }

        return 0; // Boss not found or still alive
    }

    public void IncrementBossDefeatedCount(string r)
    {
        // This is handled by Saga transactions now - no direct increment
        // The boss defeat is recorded via CharacterDefeated transaction
    }

    // Character State (stored as a special trait)
    public void SetCharacterState(string characterState) => AssignTrait(characterState, null);

    // Character Traits
    public int? GetTraitValue(string trait) => _traits.TryGetValue(trait, out var value) ? value : null;
    public void AssignTrait(string trait, int? traitValue) => _traits[trait] = traitValue;
    public void RemoveTrait(string trait) => _traits.Remove(trait);

    // Quest State
    public bool IsQuestActive(string questRef)
    {
        if (_getSagaStateFunc == null)
            return false; // No Saga state provider

        // Check all Saga instances for active quests
        foreach (var saga in _w.Gameplay?.SagaArcs ?? Array.Empty<SagaArc>())
        {
            var state = _getSagaStateFunc(saga.RefName);
            if (state != null && state.ActiveQuests.ContainsKey(questRef))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsQuestCompleted(string questRef)
    {
        if (_getSagaStateFunc == null)
            return false; // No Saga state provider

        // Check all Saga instances for completed quests
        foreach (var saga in _w.Gameplay?.SagaArcs ?? Array.Empty<SagaArc>())
        {
            var state = _getSagaStateFunc(saga.RefName);
            if (state != null && state.CompletedQuests.Contains(questRef))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsQuestNotStarted(string questRef)
    {
        // Quest not started = neither active nor completed
        return !IsQuestActive(questRef) && !IsQuestCompleted(questRef);
    }

    // Faction Reputation
    public int GetFactionReputation(string factionRef)
    {
        if (_getSagaStateFunc == null)
            return 0; // No Saga state provider - return neutral (0)

        // Check all Saga instances for faction reputation
        foreach (var saga in _w.Gameplay?.SagaArcs ?? Array.Empty<SagaArc>())
        {
            var state = _getSagaStateFunc(saga.RefName);
            if (state != null && state.FactionReputation.TryGetValue(factionRef, out var reputation))
            {
                return reputation;
            }
        }

        // Not found - check if faction has starting reputation
        if (_w.FactionsLookup.TryGetValue(factionRef, out var faction))
        {
            return faction.StartingReputation;
        }

        return 0; // Default to Neutral
    }

    public string GetFactionReputationLevel(string factionRef)
    {
        var reputation = GetFactionReputation(factionRef);
        var level = ReputationManager.GetReputationLevel(reputation);
        return level.ToString();
    }

    public void ChangeReputation(string factionRef, int amount)
    {
        // Reputation changes are handled via ChangeReputation dialogue action
        // which creates ReputationChanged transactions.
        // This method is a placeholder for the interface - actual implementation
        // is in DialogueActionExecutor which has access to Saga context.
        throw new InvalidOperationException(
            "ChangeReputation must be called through DialogueActionExecutor with Saga context");
    }

    /// <summary>
    /// Checks if rewards should be awarded for this dialogue node.
    /// If Saga state is available, checks if this is the first visit.
    /// If Saga state is not available, always returns true (no idempotency).
    /// </summary>
    public bool ShouldAwardNodeRewards(string characterRef, string nodeId)
    {
        // If no Saga state function provided, always award (backward compatibility)
        if (_getSagaStateFunc == null || string.IsNullOrEmpty(_avatarId))
            return true;

        // Check all Saga instances to find one with this dialogue visit
        foreach (var saga in _w.Gameplay?.SagaArcs ?? Array.Empty<SagaArc>())
        {
            var state = _getSagaStateFunc(saga.RefName);
            if (state != null)
            {
                // Check if this node has already been visited
                var visitKey = $"{_avatarId}_{characterRef}_{nodeId}";
                if (state.DialogueNodeVisits.ContainsKey(visitKey))
                {
                    // Already visited - don't award rewards again
                    return false;
                }
            }
        }

        // Not found in any Saga state - this is first visit, award rewards
        return true;
    }
}
