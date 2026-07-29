using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Domain.AvatarProgress;
using Ambient.Rpg.Engine.Domain.Party;
using Ambient.Rpg.Engine.Domain.Reputation;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Domain.Dialogue;

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
    private readonly ArcInstance? _arcInstance;
    private HashSet<(string Tree, string Node)>? _committedNodeVisits;
    private Dictionary<string, int>? _committedTreeVisits;

    public DirectDialogueStateProvider(
        IWorld w,
        AvatarBase a,
        IAvatarProgressRepository? progressRepo = null,
        string? avatarId = null,
        string? characterRef = null,
        ArcInstance? arcInstance = null)
    {
        _w = w;
        _a = a;
        _progressRepo = progressRepo;
        _avatarId = avatarId;
        _avatarGuid = avatarId != null && Guid.TryParse(avatarId, out var g) ? g : Guid.Empty;
        _currentCharacterRef = characterRef;
        _arcInstance = arcInstance;
    }

    /// <summary>
    /// True once any call in this request actually changed the avatar object
    /// (inventory, currency, health, achievements, party, affinities).
    /// The dialogue command handlers use this to decide whether the avatar
    /// must be persisted after the transaction commit (audit B6) — condition-only
    /// navigation stays write-free.
    /// </summary>
    public bool AvatarMutated { get; private set; }

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
    public void AddConsumable(string r, int amt) { if (_a.Capabilities != null && amt > 0) { AvatarMutated = true; _a.Capabilities.Consumables ??= Array.Empty<ConsumableEntry>(); var e = _a.Capabilities.Consumables.FirstOrDefault(x => x.ConsumableRef == r); if (e != null) e.Quantity += amt; else { var list = _a.Capabilities.Consumables.ToList(); list.Add(new ConsumableEntry { ConsumableRef = r, Quantity = amt }); _a.Capabilities.Consumables = list.ToArray(); } } }
    public void RemoveConsumable(string r, int amt) { if (_a.Capabilities?.Consumables != null && amt > 0) { var e = _a.Capabilities.Consumables.FirstOrDefault(x => x.ConsumableRef == r); if (e != null) { AvatarMutated = true; e.Quantity = Math.Max(0, e.Quantity - amt); if (e.Quantity == 0) _a.Capabilities.Consumables = _a.Capabilities.Consumables.Where(x => x.ConsumableRef != r).ToArray(); } } }

    // Materials (stackable)
    public int GetMaterialQuantity(string r) => _a.Capabilities?.BuildingMaterials?.FirstOrDefault(e => e.BuildingMaterialRef == r)?.Quantity ?? 0;
    public void AddMaterial(string r, int amt) { if (_a.Capabilities != null && amt > 0) { AvatarMutated = true; _a.Capabilities.BuildingMaterials ??= Array.Empty<BuildingMaterialEntry>(); var e = _a.Capabilities.BuildingMaterials.FirstOrDefault(x => x.BuildingMaterialRef == r); if (e != null) e.Quantity += amt; else { var list = _a.Capabilities.BuildingMaterials.ToList(); list.Add(new BuildingMaterialEntry { BuildingMaterialRef = r, Quantity = amt }); _a.Capabilities.BuildingMaterials = list.ToArray(); } } }
    public void RemoveMaterial(string r, int amt) { if (_a.Capabilities?.BuildingMaterials != null && amt > 0) { var e = _a.Capabilities.BuildingMaterials.FirstOrDefault(x => x.BuildingMaterialRef == r); if (e != null) { AvatarMutated = true; e.Quantity = Math.Max(0, e.Quantity - amt); if (e.Quantity == 0) _a.Capabilities.BuildingMaterials = _a.Capabilities.BuildingMaterials.Where(x => x.BuildingMaterialRef != r).ToArray(); } } }

    // Blocks (stackable voxel blocks)
    public float GetBlockQuantity(string r) => _a.Capabilities?.Blocks?.FirstOrDefault(e => e.BlockRef == r)?.Quantity ?? 0;
    public void AddBlock(string r, int amt) { if (_a.Capabilities != null && amt > 0) { AvatarMutated = true; _a.Capabilities.Blocks ??= Array.Empty<BlockEntry>(); var e = _a.Capabilities.Blocks.FirstOrDefault(x => x.BlockRef == r); if (e != null) e.Quantity += amt; else { var list = _a.Capabilities.Blocks.ToList(); list.Add(new BlockEntry { BlockRef = r, Quantity = amt }); _a.Capabilities.Blocks = list.ToArray(); } } }
    public void RemoveBlock(string r, int amt) { if (_a.Capabilities?.Blocks != null && amt > 0) { var e = _a.Capabilities.Blocks.FirstOrDefault(x => x.BlockRef == r); if (e != null) { AvatarMutated = true; e.Quantity = Math.Max(0, e.Quantity - amt); if (e.Quantity == 0) _a.Capabilities.Blocks = _a.Capabilities.Blocks.Where(x => !ReferenceEquals(x, e)).ToArray(); } } }

    // Equipment (degradable)
    public bool HasEquipment(string r) => _a.Capabilities?.Equipment?.Any(e => e.EquipmentRef == r) ?? false;
    public void AddEquipment(string r) { if (_a.Capabilities != null && !HasEquipment(r)) { AvatarMutated = true; _a.Capabilities.Equipment ??= Array.Empty<EquipmentEntry>(); var list = _a.Capabilities.Equipment.ToList(); list.Add(new EquipmentEntry { EquipmentRef = r, Condition = 1.0f }); _a.Capabilities.Equipment = list.ToArray(); } }
    public void RemoveEquipment(string r) { if (_a.Capabilities?.Equipment != null) { var e = _a.Capabilities.Equipment.FirstOrDefault(x => x.EquipmentRef == r); if (e != null) { AvatarMutated = true; var list = _a.Capabilities.Equipment.ToList(); list.Remove(e); _a.Capabilities.Equipment = list.ToArray(); } } }

    // Tools (degradable)
    public bool HasTool(string r) => _a.Capabilities?.Tools?.Any(e => e.ToolRef == r) ?? false;
    public void AddTool(string r) { if (_a.Capabilities != null && !HasTool(r)) { AvatarMutated = true; _a.Capabilities.Tools ??= Array.Empty<ToolEntry>(); var list = _a.Capabilities.Tools.ToList(); list.Add(new ToolEntry { ToolRef = r, Condition = 1.0f }); _a.Capabilities.Tools = list.ToArray(); } }
    public void RemoveTool(string r) { if (_a.Capabilities?.Tools != null) { var e = _a.Capabilities.Tools.FirstOrDefault(x => x.ToolRef == r); if (e != null) { AvatarMutated = true; var list = _a.Capabilities.Tools.ToList(); list.Remove(e); _a.Capabilities.Tools = list.ToArray(); } } }

    // Spells (degradable)
    public bool HasSpell(string r) => _a.Capabilities?.Spells?.Any(e => e.SpellRef == r) ?? false;
    public void AddSpell(string r) { if (_a.Capabilities != null && !HasSpell(r)) { AvatarMutated = true; _a.Capabilities.Spells ??= Array.Empty<SpellEntry>(); var list = _a.Capabilities.Spells.ToList(); list.Add(new SpellEntry { SpellRef = r, Condition = 1.0f }); _a.Capabilities.Spells = list.ToArray(); } }
    public void RemoveSpell(string r) { if (_a.Capabilities?.Spells != null) { var e = _a.Capabilities.Spells.FirstOrDefault(x => x.SpellRef == r); if (e != null) { AvatarMutated = true; var list = _a.Capabilities.Spells.ToList(); list.Remove(e); _a.Capabilities.Spells = list.ToArray(); } } }

    // Achievements
    public bool HasAchievement(string r) => _a.Achievements?.Any(e => e.AchievementRef == r) ?? false;
    public void UnlockAchievement(string r) { if (!HasAchievement(r)) { AvatarMutated = true; _a.Achievements ??= Array.Empty<AchievementEntry>(); var list = _a.Achievements.ToList(); list.Add(new AchievementEntry { AchievementRef = r }); _a.Achievements = list.ToArray(); } }

    // Currency & Health
    public float GetCredits() => _a.Stats.Credits;
    public void TransferCurrency(int amt) { if (_a.Stats != null && amt != 0) { AvatarMutated = true; _a.Stats.Credits += amt; } }
    public float GetHealth() => _a.Stats.Health;
    public void ModifyHealth(int amt) { if (_a.Stats != null && amt != 0) { AvatarMutated = true; _a.Stats.Health = Math.Max(0, _a.Stats.Health + amt); } }

    // Dialogue History — session-local visits (this request) backed by the instance's
    // committed transaction log, so NodeVisited / AvatarVisitCount conditions see
    // previous conversations and survive reloads (the log IS the durable record).
    public int GetAvatarVisitCount(string t)
    {
        EnsureCommittedDialogueHistory();
        var committed = _committedTreeVisits!.GetValueOrDefault(t);
        if (committed > 0) return committed;
        // No committed history (no arc instance wired, e.g. plain engine/test use):
        // the current session is the one and only visit
        return _visited.ContainsKey(t) ? 1 : 0;
    }

    public void RecordNodeVisit(string t, string n) { if (!_visited.ContainsKey(t)) _visited[t] = new HashSet<string>(); _visited[t].Add(n); }

    public bool WasNodeVisited(string t, string n)
    {
        if (_visited.TryGetValue(t, out var nodes) && nodes.Contains(n)) return true;
        EnsureCommittedDialogueHistory();
        return _committedNodeVisits!.Contains((t, n));
    }

    /// <summary>
    /// Lazily projects the instance's committed transaction log into dialogue history:
    /// per-tree node visits (NodeVisited) and per-tree conversation counts
    /// (AvatarVisitCount = completed sessions + the in-progress one, per the XSD's
    /// "number of times the avatar has visited this NPC"). Scoped to this avatar,
    /// skipping transactions undone by a committed TransactionReversed compensation
    /// (avatar persistence failed, so the visit must not count against re-award).
    /// </summary>
    private void EnsureCommittedDialogueHistory()
    {
        if (_committedNodeVisits != null) return;
        _committedNodeVisits = new HashSet<(string, string)>();
        _committedTreeVisits = new Dictionary<string, int>();
        if (_arcInstance == null) return;

        var reversedIds = new HashSet<Guid>();
        foreach (var tx in _arcInstance.Transactions)
        {
            if (tx.Type == ArcTransactionType.TransactionReversed &&
                tx.Status == TransactionStatus.Committed &&
                Guid.TryParse(tx.GetData<string>(TransactionDataKeys.ReversedTransactionId), out var reversedId))
            {
                reversedIds.Add(reversedId);
            }
        }

        // One conversation = one DialogueStarted..DialogueCompleted span (dangling
        // sessions are sealed by StartDialogueHandler, so Completed count is exact);
        // a Started after the tree's last Completed is the in-progress conversation.
        var completedByTree = new Dictionary<string, int>();
        var lastStartedSeq = new Dictionary<string, long>();
        var lastCompletedSeq = new Dictionary<string, long>();

        foreach (var tx in _arcInstance.Transactions.OrderBy(t => t.SequenceNumber))
        {
            if (tx.Status != TransactionStatus.Committed) continue;
            if (reversedIds.Contains(tx.TransactionId)) continue;
            if (_avatarId != null && !string.Equals(tx.AvatarId, _avatarId, StringComparison.Ordinal)) continue;

            var tree = tx.GetData<string>(TransactionDataKeys.DialogueTreeRef);
            if (string.IsNullOrEmpty(tree)) continue;

            switch (tx.Type)
            {
                case ArcTransactionType.DialogueNodeVisited:
                    var node = tx.GetData<string>(TransactionDataKeys.DialogueNodeId);
                    if (!string.IsNullOrEmpty(node)) _committedNodeVisits.Add((tree, node));
                    break;

                case ArcTransactionType.DialogueStarted:
                    lastStartedSeq[tree] = tx.SequenceNumber;
                    break;

                case ArcTransactionType.DialogueCompleted:
                    completedByTree[tree] = completedByTree.GetValueOrDefault(tree) + 1;
                    lastCompletedSeq[tree] = tx.SequenceNumber;
                    break;
            }
        }

        foreach (var (tree, startedSeq) in lastStartedSeq)
        {
            var inProgress = !lastCompletedSeq.TryGetValue(tree, out var completedSeq) || startedSeq > completedSeq ? 1 : 0;
            _committedTreeVisits[tree] = completedByTree.GetValueOrDefault(tree) + inProgress;
        }

        foreach (var (tree, count) in completedByTree)
        {
            if (!_committedTreeVisits.ContainsKey(tree)) _committedTreeVisits[tree] = count;
        }
    }

    public int GetBossDefeatedCount(string bossRef)
        => _progressRepo?.GetBossDefeatedCount(_avatarGuid, bossRef) ?? 0;

    public void IncrementBossDefeatedCount(string r)
    {
        // This is handled by Arc transactions now - no direct increment
        // The boss defeat is recorded via CharacterDefeated transaction
    }

    // Character State (stored as a special trait)
    public void SetCharacterState(string characterState) => AssignTrait(characterState, null);

    // Character Traits — session buffer for traits assigned this request, backed by the
    // persisted AvatarCharacterTraits projection so trait gates survive across requests.
    public int? GetTraitValue(string trait)
    {
        if (_traits.TryGetValue(trait, out var value)) return value;

        // Skill traits (BargainSkill, Merciful, ...) are earned at one NPC and gated at
        // another, so read the avatar's best earned value regardless of source character.
        // A lower assignment at the current NPC must not mask a higher skill earned elsewhere.
        return _progressRepo?.GetMaxTraitValue(_avatarGuid, trait);
    }

    public void AssignTrait(string trait, int? traitValue) => _traits[trait] = traitValue;

    // Null tombstone: a trait removed this request must not resurrect from the projection
    // (the TraitRemoved transaction only projects after commit).
    public void RemoveTrait(string trait) => _traits[trait] = null;

    // Quest State
    public bool IsQuestActive(string questRef)
        => _progressRepo?.GetQuestStatus(_avatarGuid, questRef) == QuestProgressStatus.Active;

    public bool IsQuestCompleted(string questRef)
        => _progressRepo?.GetQuestStatus(_avatarGuid, questRef) == QuestProgressStatus.Completed;

    public bool IsQuestNotStarted(string questRef)
    {
        // Abandoned quests are re-offerable. Offer nodes gate on QuestNotStarted and docs
        // promise "re-accepting starts a fresh scope", but AbandonQuest projects a durable
        // Abandoned row (not null), so treating only null as not-started hid the offer forever —
        // one Abandon tap bricked the quest, and the world if it was the completion quest.
        var status = _progressRepo?.GetQuestStatus(_avatarGuid, questRef);
        return status == null || status == QuestProgressStatus.Abandoned;
    }

    // Faction Reputation — persisted projection + faction starting value +
    // session-local deltas for changes made during this request (the durable
    // record is the ReputationChanged transactions the executor stages)
    private readonly Dictionary<string, int> _sessionReputationDeltas = new();

    public int GetFactionReputation(string factionRef)
    {
        // Starting reputation is the baseline; earned changes are deltas on top.
        // (The old code returned StartingReputation whenever the stored value was
        // exactly 0, so earning +500 then -500 snapped back to the faction default.)
        var baseline = _w.FactionsLookup.TryGetValue(factionRef, out var faction)
            ? faction.StartingReputation
            : 0;

        var earned = _progressRepo?.GetFactionReputation(_avatarGuid, factionRef) ?? 0;
        _sessionReputationDeltas.TryGetValue(factionRef, out var sessionDelta);

        return baseline + earned + sessionDelta;
    }

    public string GetFactionReputationLevel(string factionRef)
    {
        var reputation = GetFactionReputation(factionRef);
        var level = ReputationManager.GetReputationLevel(reputation);
        return level.ToString();
    }

    public void ChangeReputation(string factionRef, int amount)
    {
        _sessionReputationDeltas.TryGetValue(factionRef, out var current);
        _sessionReputationDeltas[factionRef] = current + amount;
    }

    public IReadOnlyList<(string FactionRef, int Amount)> GetReputationSpillover(string factionRef, int amount)
        => ReputationManager.CalculateSpilloverForAll(_w.FactionsLookup, factionRef, amount);

    // Idempotency for dialogue node rewards is handled by ArcDialogueContext
    // and DialogueTransactionHelper.ShouldAwardNodeRewards at the handler level.
    // This method provides a fallback for test scenarios without arc context.
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
        AvatarMutated = true;
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
        AvatarMutated = true;
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
        AvatarMutated = true;
    }
}
