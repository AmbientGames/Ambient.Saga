using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Party;
using Ambient.Saga.Engine.Domain.Rpg.Reputation;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Domain.Rpg.Dialogue;

/// <summary>
/// Direct implementation of IDialogueStateProvider that manipulates World and Avatar state directly.
/// This provider can be used by any UI framework (WPF, Console, Web, etc.) that has access to domain objects.
/// Uses a provider-based pattern for item access - built-in providers handle Saga-owned types,
/// while IGameplayItemProvider handles external types from the consuming application.
/// </summary>
public class DirectDialogueStateProvider : IDialogueStateProvider
{
    private readonly IWorld _w;
    private readonly AvatarBase _a;
    private readonly Dictionary<string, HashSet<string>> _visited = new();
    private readonly Dictionary<string, int?> _traits = new();
    private readonly Func<string, SagaState?>? _getSagaStateFunc;
    private readonly string? _avatarId;
    private string? _currentCharacterRef;

    // Built-in Saga provider names
    private const string EquipmentProvider = "Equipment";
    private const string ConsumablesProvider = "Consumables";
    private const string SpellsProvider = "Spells";
    private const string QuestTokensProvider = "QuestTokens";

    public DirectDialogueStateProvider(
        IWorld w,
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

    // ===== PROVIDER-BASED ITEM ACCESS =====

    /// <inheritdoc/>
    public bool HasItem(string provider, string refName)
    {
        return GetItemQuantity(provider, refName) > 0;
    }

    /// <inheritdoc/>
    public int GetItemQuantity(string provider, string refName)
    {
        if (_a.Capabilities == null) return 0;

        // Handle built-in Saga providers
        switch (provider)
        {
            case EquipmentProvider:
                return _a.Capabilities.Equipment?.Any(e => e.EquipmentRef == refName) == true ? 1 : 0;

            case ConsumablesProvider:
                return _a.Capabilities.Consumables?.FirstOrDefault(e => e.ConsumableRef == refName)?.Quantity ?? 0;

            case SpellsProvider:
                return _a.Capabilities.Spells?.Any(e => e.SpellRef == refName) == true ? 1 : 0;

            case QuestTokensProvider:
                return _a.Capabilities.QuestTokens?.Any(e => e.QuestTokenRef == refName) == true ? 1 : 0;

            default:
                // Try IGameplayItemProvider for external providers
                return GetExternalItemQuantity(provider, refName);
        }
    }

    /// <inheritdoc/>
    public void GiveItem(string provider, string refName, int quantity = 1)
    {
        if (_a.Capabilities == null || quantity <= 0) return;

        switch (provider)
        {
            case EquipmentProvider:
                GiveEquipment(refName);
                break;

            case ConsumablesProvider:
                GiveConsumable(refName, quantity);
                break;

            case SpellsProvider:
                GiveSpell(refName);
                break;

            case QuestTokensProvider:
                GiveQuestToken(refName);
                break;

            default:
                // External providers - delegate to IGameplayItemProvider
                GiveExternalItem(provider, refName, quantity);
                break;
        }
    }

    /// <inheritdoc/>
    public void TakeItem(string provider, string refName, int quantity = 1)
    {
        if (_a.Capabilities == null || quantity <= 0) return;

        switch (provider)
        {
            case EquipmentProvider:
                TakeEquipment(refName);
                break;

            case ConsumablesProvider:
                TakeConsumable(refName, quantity);
                break;

            case SpellsProvider:
                TakeSpell(refName);
                break;

            case QuestTokensProvider:
                TakeQuestToken(refName);
                break;

            default:
                // External providers - delegate to IGameplayItemProvider
                TakeExternalItem(provider, refName, quantity);
                break;
        }
    }

    // ===== BUILT-IN SAGA PROVIDERS (private helpers) =====

    private void GiveEquipment(string r)
    {
        if (_a.Capabilities?.Equipment != null && !(_a.Capabilities.Equipment.Any(e => e.EquipmentRef == r)))
        {
            var list = _a.Capabilities.Equipment.ToList();
            list.Add(new EquipmentEntry { EquipmentRef = r, Condition = 1.0f });
            _a.Capabilities.Equipment = list.ToArray();
        }
    }

    private void TakeEquipment(string r)
    {
        if (_a.Capabilities?.Equipment != null)
        {
            var e = _a.Capabilities.Equipment.FirstOrDefault(x => x.EquipmentRef == r);
            if (e != null)
            {
                var list = _a.Capabilities.Equipment.ToList();
                list.Remove(e);
                _a.Capabilities.Equipment = list.ToArray();
            }
        }
    }

    private void GiveConsumable(string r, int amt)
    {
        if (_a.Capabilities != null && amt > 0)
        {
            _a.Capabilities.Consumables ??= Array.Empty<ConsumableEntry>();
            var e = _a.Capabilities.Consumables.FirstOrDefault(x => x.ConsumableRef == r);
            if (e != null)
                e.Quantity += amt;
            else
            {
                var list = _a.Capabilities.Consumables.ToList();
                list.Add(new ConsumableEntry { ConsumableRef = r, Quantity = amt });
                _a.Capabilities.Consumables = list.ToArray();
            }
        }
    }

    private void TakeConsumable(string r, int amt)
    {
        if (_a.Capabilities?.Consumables != null && amt > 0)
        {
            var e = _a.Capabilities.Consumables.FirstOrDefault(x => x.ConsumableRef == r);
            if (e != null)
            {
                e.Quantity = Math.Max(0, e.Quantity - amt);
                if (e.Quantity == 0)
                    _a.Capabilities.Consumables = _a.Capabilities.Consumables.Where(x => x.ConsumableRef != r).ToArray();
            }
        }
    }

    private void GiveSpell(string r)
    {
        if (_a.Capabilities != null && !(_a.Capabilities.Spells?.Any(e => e.SpellRef == r) ?? false))
        {
            _a.Capabilities.Spells ??= Array.Empty<SpellEntry>();
            var list = _a.Capabilities.Spells.ToList();
            list.Add(new SpellEntry { SpellRef = r, Condition = 1.0f });
            _a.Capabilities.Spells = list.ToArray();
        }
    }

    private void TakeSpell(string r)
    {
        if (_a.Capabilities?.Spells != null)
        {
            var e = _a.Capabilities.Spells.FirstOrDefault(x => x.SpellRef == r);
            if (e != null)
            {
                var list = _a.Capabilities.Spells.ToList();
                list.Remove(e);
                _a.Capabilities.Spells = list.ToArray();
            }
        }
    }

    private void GiveQuestToken(string r)
    {
        if (_a.Capabilities != null && !(_a.Capabilities.QuestTokens?.Any(e => e.QuestTokenRef == r) ?? false))
        {
            _a.Capabilities.QuestTokens ??= Array.Empty<QuestTokenEntry>();
            var list = _a.Capabilities.QuestTokens.ToList();
            list.Add(new QuestTokenEntry { QuestTokenRef = r });
            _a.Capabilities.QuestTokens = list.ToArray();
        }
    }

    private void TakeQuestToken(string r)
    {
        if (_a.Capabilities?.QuestTokens != null)
            _a.Capabilities.QuestTokens = _a.Capabilities.QuestTokens.Where(e => e.QuestTokenRef != r).ToArray();
    }

    // ===== EXTERNAL PROVIDERS (IGameplayItemProvider) =====

    private int GetExternalItemQuantity(string provider, string refName)
    {
        // Find matching IGameplayItemProvider
        var itemProvider = _w.GameplayItemProviders.FirstOrDefault(p => p.Name == provider);
        if (itemProvider == null) return 0;

        // Get quantity from the provider's avatar inventory access
        return itemProvider.GetAvatarItemQuantity(_a, refName);
    }

    private void GiveExternalItem(string provider, string refName, int quantity)
    {
        var itemProvider = _w.GameplayItemProviders.FirstOrDefault(p => p.Name == provider);
        if (itemProvider == null) return;

        itemProvider.GiveAvatarItem(_a, refName, quantity);
    }

    private void TakeExternalItem(string provider, string refName, int quantity)
    {
        var itemProvider = _w.GameplayItemProviders.FirstOrDefault(p => p.Name == provider);
        if (itemProvider == null) return;

        itemProvider.TakeAvatarItem(_a, refName, quantity);
    }

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
        {
            // No Saga state provider - check faction starting reputation
            if (_w.FactionsLookup.TryGetValue(factionRef, out var factionDef))
            {
                return factionDef.StartingReputation;
            }
            return 0; // Neutral
        }

        // Check all Saga instances for faction reputation
        foreach (var saga in _w.Gameplay?.SagaArcs ?? Array.Empty<SagaArc>())
        {
            var state = _getSagaStateFunc(saga.RefName);
            if (state != null && state.FactionReputation.TryGetValue(factionRef, out var reputation))
            {
                return reputation;
            }
        }

        // If no saga arcs in world, try getting state directly (for tests)
        // This handles the case where tests provide a getSagaStateFunc but minimal world has no SagaArcs
        var directState = _getSagaStateFunc(string.Empty);
        if (directState != null && directState.FactionReputation.TryGetValue(factionRef, out var directReputation))
        {
            return directReputation;
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
