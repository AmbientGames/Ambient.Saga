using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Reputation;

namespace Ambient.Saga.Engine.Domain.Rpg.Party;

/// <summary>
/// Manages party membership, slot calculations, and party operations.
/// Party slots are gated by reputation with a designated faction.
/// </summary>
public static class PartyManager
{
    /// <summary>
    /// Party slot allocation by reputation level.
    /// </summary>
    private static readonly Dictionary<ReputationLevel, int> SlotsByReputationLevel = new()
    {
        [ReputationLevel.Hated] = 0,
        [ReputationLevel.Hostile] = 0,
        [ReputationLevel.Unfriendly] = 1,
        [ReputationLevel.Neutral] = 1,
        [ReputationLevel.Friendly] = 2,
        [ReputationLevel.Honored] = 3,
        [ReputationLevel.Revered] = 3,
        [ReputationLevel.Exalted] = 4
    };

    /// <summary>
    /// Gets the maximum number of party slots based on reputation level.
    /// </summary>
    /// <param name="reputationLevel">Current reputation level with the party slot faction</param>
    /// <returns>Maximum party slots available</returns>
    public static int GetMaxPartySlots(ReputationLevel reputationLevel)
    {
        return SlotsByReputationLevel.TryGetValue(reputationLevel, out var slots) ? slots : 1;
    }

    /// <summary>
    /// Gets the maximum number of party slots based on numeric reputation value.
    /// </summary>
    /// <param name="reputationValue">Numeric reputation value</param>
    /// <returns>Maximum party slots available</returns>
    public static int GetMaxPartySlots(int reputationValue)
    {
        var level = ReputationManager.GetReputationLevel(reputationValue);
        return GetMaxPartySlots(level);
    }

    /// <summary>
    /// Checks if there is an available party slot.
    /// </summary>
    /// <param name="party">Current party inventory</param>
    /// <param name="reputationValue">Current reputation value with slot faction</param>
    /// <returns>True if a slot is available</returns>
    public static bool HasAvailableSlot(PartyInventory? party, int reputationValue)
    {
        var maxSlots = GetMaxPartySlots(reputationValue);
        var currentSize = GetPartySize(party);
        return currentSize < maxSlots;
    }

    /// <summary>
    /// Gets the current party size.
    /// </summary>
    /// <param name="party">Party inventory</param>
    /// <returns>Number of party members</returns>
    public static int GetPartySize(PartyInventory? party)
    {
        return party?.Member?.Length ?? 0;
    }

    /// <summary>
    /// Checks if a character is in the party.
    /// </summary>
    /// <param name="party">Party inventory</param>
    /// <param name="characterRef">Character reference to check</param>
    /// <returns>True if character is in the party</returns>
    public static bool IsInParty(PartyInventory? party, string characterRef)
    {
        if (party?.Member == null || string.IsNullOrEmpty(characterRef))
            return false;

        return party.Member.Any(m => m.CharacterRef == characterRef);
    }

    /// <summary>
    /// Gets the next available party slot reference.
    /// </summary>
    /// <param name="party">Current party inventory</param>
    /// <param name="reputationValue">Current reputation value with slot faction</param>
    /// <returns>Slot reference (e.g., "Party1", "Party2"), or null if no slots available</returns>
    public static string? GetNextAvailableSlot(PartyInventory? party, int reputationValue)
    {
        var maxSlots = GetMaxPartySlots(reputationValue);
        var usedSlots = party?.Member?.Select(m => m.SlotRef).ToHashSet() ?? new HashSet<string>();

        for (int i = 1; i <= maxSlots; i++)
        {
            var slotRef = $"Party{i}";
            if (!usedSlots.Contains(slotRef))
                return slotRef;
        }

        return null;
    }

    /// <summary>
    /// Adds a character to the party.
    /// </summary>
    /// <param name="party">Party inventory (will be created if null)</param>
    /// <param name="characterRef">Character to add</param>
    /// <param name="reputationValue">Current reputation value with slot faction</param>
    /// <returns>Updated party inventory, or null if no slot available</returns>
    public static PartyInventory? AddPartyMember(PartyInventory? party, string characterRef, int reputationValue)
    {
        if (string.IsNullOrEmpty(characterRef))
            return party;

        // Check if already in party
        if (IsInParty(party, characterRef))
            return party;

        // Get next available slot
        var slotRef = GetNextAvailableSlot(party, reputationValue);
        if (slotRef == null)
            return null; // No slots available

        // Create party if needed
        party ??= new PartyInventory();

        // Create new member
        var newMember = new PartyMember
        {
            SlotRef = slotRef,
            CharacterRef = characterRef,
            JoinedDate = DateTime.UtcNow.ToString("O")
        };

        // Add to members array
        var members = party.Member?.ToList() ?? new List<PartyMember>();
        members.Add(newMember);
        party.Member = members.ToArray();

        return party;
    }

    /// <summary>
    /// Removes a character from the party.
    /// </summary>
    /// <param name="party">Party inventory</param>
    /// <param name="characterRef">Character to remove</param>
    /// <returns>Updated party inventory</returns>
    public static PartyInventory? RemovePartyMember(PartyInventory? party, string characterRef)
    {
        if (party?.Member == null || string.IsNullOrEmpty(characterRef))
            return party;

        var members = party.Member.Where(m => m.CharacterRef != characterRef).ToArray();
        party.Member = members;

        return party;
    }

    /// <summary>
    /// Gets all party member character references.
    /// </summary>
    /// <param name="party">Party inventory</param>
    /// <returns>Array of character references</returns>
    public static string[] GetPartyMemberRefs(PartyInventory? party)
    {
        return party?.Member?.Select(m => m.CharacterRef).ToArray() ?? Array.Empty<string>();
    }
}
