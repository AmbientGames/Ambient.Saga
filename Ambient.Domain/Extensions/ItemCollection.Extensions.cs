using System.Diagnostics.CodeAnalysis;

namespace Ambient.Domain.Extensions;

/// <summary>
/// Extension methods for ItemCollection to provide dictionary-like access to inventory arrays.
/// </summary>
public static class ItemCollectionExtensions
{
    #region Equipment (Condition-based)

    public static bool TryGetEquipment(this ItemCollection collection, string equipmentRef, [NotNullWhen(true)] out EquipmentEntry? equipment)
    {
        if (collection?.Equipment == null)
        {
            equipment = null;
            return false;
        }
        equipment = Array.Find(collection.Equipment, e => e.EquipmentRef == equipmentRef);
        return equipment != null;
    }

    public static EquipmentEntry? GetEquipment(this ItemCollection collection, string equipmentRef)
    {
        return collection?.Equipment?.FirstOrDefault(e => e.EquipmentRef == equipmentRef);
    }

    public static EquipmentEntry GetOrAddEquipment(this ItemCollection collection, string equipmentRef)
    {
        if (collection.Equipment == null)
        {
            collection.Equipment = Array.Empty<EquipmentEntry>();
        }

        var existing = Array.Find(collection.Equipment, e => e.EquipmentRef == equipmentRef);
        if (existing != null) return existing;

        var newEquipment = new EquipmentEntry { EquipmentRef = equipmentRef, Condition = 1.0f };
        var items = collection.Equipment;
        Array.Resize(ref items, items.Length + 1);
        items[items.Length - 1] = newEquipment;
        collection.Equipment = items;
        return newEquipment;
    }

    #endregion

    #region Spells (Condition-based)

    public static bool TryGetSpell(this ItemCollection collection, string spellRef, [NotNullWhen(true)] out SpellEntry? spell)
    {
        if (collection?.Spells == null)
        {
            spell = null;
            return false;
        }
        spell = Array.Find(collection.Spells, s => s.SpellRef == spellRef);
        return spell != null;
    }

    public static SpellEntry? GetSpell(this ItemCollection collection, string spellRef)
    {
        return collection?.Spells?.FirstOrDefault(s => s.SpellRef == spellRef);
    }

    public static SpellEntry GetOrAddSpell(this ItemCollection collection, string spellRef)
    {
        if (collection.Spells == null)
        {
            collection.Spells = Array.Empty<SpellEntry>();
        }

        var existing = Array.Find(collection.Spells, s => s.SpellRef == spellRef);
        if (existing != null) return existing;

        var newSpell = new SpellEntry { SpellRef = spellRef, Condition = 1.0f };
        var spells = collection.Spells;
        Array.Resize(ref spells, spells.Length + 1);
        spells[spells.Length - 1] = newSpell;
        collection.Spells = spells;
        return newSpell;
    }

    #endregion

    #region Consumables (Quantity-based)

    public static bool TryGetConsumable(this ItemCollection collection, string consumableRef, [NotNullWhen(true)] out ConsumableEntry? consumable)
    {
        if (collection?.Consumables == null)
        {
            consumable = null;
            return false;
        }
        consumable = Array.Find(collection.Consumables, c => c.ConsumableRef == consumableRef);
        return consumable != null;
    }

    public static ConsumableEntry? GetConsumable(this ItemCollection collection, string consumableRef)
    {
        return collection?.Consumables?.FirstOrDefault(c => c.ConsumableRef == consumableRef);
    }

    public static ConsumableEntry GetOrAddConsumable(this ItemCollection collection, string consumableRef)
    {
        if (collection.Consumables == null)
        {
            collection.Consumables = Array.Empty<ConsumableEntry>();
        }

        var existing = Array.Find(collection.Consumables, c => c.ConsumableRef == consumableRef);
        if (existing != null) return existing;

        var newConsumable = new ConsumableEntry { ConsumableRef = consumableRef, Quantity = 0 };
        var consumables = collection.Consumables;
        Array.Resize(ref consumables, consumables.Length + 1);
        consumables[consumables.Length - 1] = newConsumable;
        collection.Consumables = consumables;
        return newConsumable;
    }

    #endregion

    #region QuestTokens (Presence-based)

    public static bool HasQuestToken(this ItemCollection collection, string questTokenRef)
    {
        if (collection?.QuestTokens == null) return false;
        return Array.Exists(collection.QuestTokens, q => q.QuestTokenRef == questTokenRef);
    }

    public static QuestTokenEntry? GetQuestToken(this ItemCollection collection, string questTokenRef)
    {
        return collection?.QuestTokens?.FirstOrDefault(q => q.QuestTokenRef == questTokenRef);
    }

    public static QuestTokenEntry AddQuestToken(this ItemCollection collection, string questTokenRef)
    {
        if (collection.QuestTokens == null)
        {
            collection.QuestTokens = Array.Empty<QuestTokenEntry>();
        }

        var existing = Array.Find(collection.QuestTokens, q => q.QuestTokenRef == questTokenRef);
        if (existing != null) return existing;

        var newToken = new QuestTokenEntry { QuestTokenRef = questTokenRef };
        var tokens = collection.QuestTokens;
        Array.Resize(ref tokens, tokens.Length + 1);
        tokens[tokens.Length - 1] = newToken;
        collection.QuestTokens = tokens;
        return newToken;
    }

    public static bool RemoveQuestToken(this ItemCollection collection, string questTokenRef)
    {
        if (collection?.QuestTokens == null) return false;

        var index = Array.FindIndex(collection.QuestTokens, q => q.QuestTokenRef == questTokenRef);
        if (index == -1) return false;

        var tokens = collection.QuestTokens.ToList();
        tokens.RemoveAt(index);
        collection.QuestTokens = tokens.ToArray();
        return true;
    }

    #endregion
}
