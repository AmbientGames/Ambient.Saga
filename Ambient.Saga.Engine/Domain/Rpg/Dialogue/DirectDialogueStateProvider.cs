using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Domain.AvatarProgress;
using Ambient.Saga.Engine.Domain.Rpg.Party;
using Ambient.Saga.Engine.Domain.Rpg.Reputation;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Domain.Rpg.Dialogue;

/// <summary>
/// Direct implementation of IDialogueStateProvider that manipulates World and Avatar state directly.
/// This provider can be used by any UI framework (WPF, Console, Web, etc.) that has access to domain objects.
/// </summary>
public class DirectDialogueStateProvider : IDialogueStateProvider
{
    private readonly IWorld _w;
    private readonly AvatarBase _a;
    private readonly Dictionary<string, HashSet<string>> _visited = new();
    private readonly Dictionary<string, int?> _traits = new();
    private readonly IAvatarProgressRepository? _progressRepo;
    private readonly Guid _avatarGuid;
    private readonly string? _avatarId;
    private string? _currentCharacterRef;
    private readonly HashSet<string> _sessionTokens = new();

    public DirectDialogueStateProvider(
        IWorld w,
        AvatarBase a,
        IAvatarProgressRepository? progressRepo = null,
        string? avatarId = null,
        string? characterRef = null)
    {
        _w = w;
        _a = a;
        _progressRepo = progressRepo;
        _avatarId = avatarId;
        _avatarGuid = avatarId != null && Guid.TryParse(avatarId, out var g) ? g : Guid.Empty;
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

    // Quest Tokens — read from avatar progress table + session buffer for tokens granted this dialogue
    public bool HasQuestToken(string r)
    {
        if (_sessionTokens.Contains(r)) return true;
        return _progressRepo?.HasQuestToken(_avatarGuid, r) ?? false;
    }

    public void AddQuestToken(string r) => _sessionTokens.Add(r);

    // Consumables (stackable)
    public int GetConsumableQuantity(string r) => _a.Capabilities?.Consumables?.FirstOrDefault(e => e.ConsumableRef == r)?.Quantity ?? 0;
    public void AddConsumable(string r, int amt) { if (_a.Capabilities?.Consumables != null && amt > 0) { var e = _a.Capabilities.Consumables.FirstOrDefault(x => x.ConsumableRef == r); if (e != null) e.Quantity += amt; else { var list = _a.Capabilities.Consumables.ToList(); list.Add(new ConsumableEntry { ConsumableRef = r, Quantity = amt }); _a.Capabilities.Consumables = list.ToArray(); } } }
    public void RemoveConsumable(string r, int amt) { if (_a.Capabilities?.Consumables != null && amt > 0) { var e = _a.Capabilities.Consumables.FirstOrDefault(x => x.ConsumableRef == r); if (e != null) { e.Quantity = Math.Max(0, e.Quantity - amt); if (e.Quantity == 0) _a.Capabilities.Consumables = _a.Capabilities.Consumables.Where(x => x.ConsumableRef != r).ToArray(); } } }

    // Materials (stackable)
    public int GetMaterialQuantity(string r) => _a.Capabilities?.BuildingMaterials?.FirstOrDefault(e => e.BuildingMaterialRef == r)?.Quantity ?? 0;
    public void AddMaterial(string r, int amt) { if (_a.Capabilities?.BuildingMaterials != null && amt > 0) { var e = _a.Capabilities.BuildingMaterials.FirstOrDefault(x => x.BuildingMaterialRef == r); if (e != null) e.Quantity += amt; else { var list = _a.Capabilities.BuildingMaterials.ToList(); list.Add(new BuildingMaterialEntry { BuildingMaterialRef = r, Quantity = amt }); _a.Capabilities.BuildingMaterials = list.ToArray(); } } }
    public void RemoveMaterial(string r, int amt) { if (_a.Capabilities?.BuildingMaterials != null && amt > 0) { var e = _a.Capabilities.BuildingMaterials.FirstOrDefault(x => x.BuildingMaterialRef == r); if (e != null) { e.Quantity = Math.Max(0, e.Quantity - amt); if (e.Quantity == 0) _a.Capabilities.BuildingMaterials = _a.Capabilities.BuildingMaterials.Where(x => x.BuildingMaterialRef != r).ToArray(); } } }

    // Blocks (stackable voxel blocks)
    public float GetBlockQuantity(string r) => _a.Capabilities?.Blocks?.FirstOrDefault(e => e.BlockRef == r)?.Quantity ?? 0;
    public void AddBlock(string r, int amt) { if (_a.Capabilities != null && amt > 0) { _a.Capabilities.Blocks ??= Array.Empty<BlockEntry>(); var e = _a.Capabilities.Blocks.FirstOrDefault(x => x.BlockRef == r); if (e != null) e.Quantity += amt; else { var list = _a.Capabilities.Blocks.ToList(); list.Add(new BlockEntry { BlockRef = r, Quantity = amt }); _a.Capabilities.Blocks = list.ToArray(); } } }
    public void RemoveBlock(string r, int amt) { if (_a.Capabilities?.Blocks != null && amt > 0) { var e = _a.Capabilities.Blocks.FirstOrDefault(x => x.BlockRef == r); if (e != null) { e.Quantity = Math.Max(0, e.Quantity - amt); if (e.Quantity == 0) _a.Capabilities.Blocks = _a.Capabilities.Blocks.Where(x => x.BlockRef != r).ToArray(); } } }

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
    public int GetAvatarVisitCount(string t) => _visited.ContainsKey(t) ? 1 : 0;
    public void RecordNodeVisit(string t, string n) { if (!_visited.ContainsKey(t)) _visited[t] = new HashSet<string>(); _visited[t].Add(n); }
    public bool WasNodeVisited(string t, string n) => _visited.ContainsKey(t) && _visited[t].Contains(n);

    public int GetBossDefeatedCount(string bossRef)
        => _progressRepo?.GetBossDefeatedCount(_avatarGuid, bossRef) ?? 0;

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
        => _progressRepo?.GetQuestStatus(_avatarGuid, questRef) == QuestProgressStatus.Active;

    public bool IsQuestCompleted(string questRef)
        => _progressRepo?.GetQuestStatus(_avatarGuid, questRef) == QuestProgressStatus.Completed;

    public bool IsQuestNotStarted(string questRef)
        => _progressRepo?.GetQuestStatus(_avatarGuid, questRef) == null;

    // Faction Reputation
    public int GetFactionReputation(string factionRef)
    {
        var progressValue = _progressRepo?.GetFactionReputation(_avatarGuid, factionRef) ?? 0;
        if (progressValue != 0) return progressValue;

        // Fall back to faction starting reputation
        if (_w.FactionsLookup.TryGetValue(factionRef, out var faction))
            return faction.StartingReputation;

        return 0;
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

    // Idempotency for dialogue node rewards is handled by SagaDialogueContext
    // and DialogueTransactionHelper.ShouldAwardNodeRewards at the handler level.
    // This method provides a fallback for test scenarios without saga context.
    public bool ShouldAwardNodeRewards(string characterRef, string nodeId) => true;

    // ===== PARTY MANAGEMENT =====

    /// <summary>
    /// Gets the current party size.
    /// </summary>
    public int GetPartySize() => PartyManager.GetPartySize(_a.Party);

    /// <summary>
    /// Checks if a party slot is available based on reputation with the slot faction.
    /// </summary>
    public bool HasAvailablePartySlot()
    {
        var slotFactionRef = _a.Party?.SlotFactionRef;
        if (string.IsNullOrEmpty(slotFactionRef))
        {
            // No faction configured - use default of 1 slot at Neutral
            return PartyManager.HasAvailableSlot(_a.Party, 0);
        }

        var reputation = GetFactionReputation(slotFactionRef);
        return PartyManager.HasAvailableSlot(_a.Party, reputation);
    }

    /// <summary>
    /// Checks if a character is in the party.
    /// If characterRef is null/empty, checks if the current dialogue character is in party.
    /// </summary>
    public bool IsInParty(string? characterRef)
    {
        var refToCheck = string.IsNullOrEmpty(characterRef) ? _currentCharacterRef : characterRef;
        return PartyManager.IsInParty(_a.Party, refToCheck ?? "");
    }

    /// <summary>
    /// Adds a character to the party.
    /// </summary>
    public bool AddPartyMember(string characterRef)
    {
        if (string.IsNullOrEmpty(characterRef))
            return false;

        var slotFactionRef = _a.Party?.SlotFactionRef;
        var reputation = string.IsNullOrEmpty(slotFactionRef) ? 0 : GetFactionReputation(slotFactionRef);

        var updatedParty = PartyManager.AddPartyMember(_a.Party, characterRef, reputation);
        if (updatedParty == null)
            return false; // No slot available

        _a.Party = updatedParty;
        return true;
    }

    /// <summary>
    /// Removes a character from the party.
    /// </summary>
    public void RemovePartyMember(string characterRef)
    {
        if (string.IsNullOrEmpty(characterRef))
            return;

        _a.Party = PartyManager.RemovePartyMember(_a.Party, characterRef);
    }

    // ===== AFFINITY MANAGEMENT =====

    /// <summary>
    /// Checks if the avatar has a specific affinity.
    /// </summary>
    public bool HasAffinity(string affinityRef)
    {
        return _a.Affinities?.Any(a => a.AffinityRef == affinityRef) ?? false;
    }

    /// <summary>
    /// Grants an affinity to the avatar, captured from a character.
    /// </summary>
    public void AddAffinity(string affinityRef, string capturedFromCharacterRef)
    {
        if (string.IsNullOrEmpty(affinityRef))
            return;

        // Don't add duplicate affinities (same affinity from same character)
        if (_a.Affinities?.Any(a => a.AffinityRef == affinityRef && a.CapturedFromCharacterRef == capturedFromCharacterRef) ?? false)
            return;

        var affinities = _a.Affinities?.ToList() ?? new List<Affinity>();
        affinities.Add(new Affinity
        {
            AffinityRef = affinityRef,
            CapturedFromCharacterRef = capturedFromCharacterRef,
            AcquiredDate = DateTime.UtcNow.ToString("O")
        });
        _a.Affinities = affinities.ToArray();
    }
}
