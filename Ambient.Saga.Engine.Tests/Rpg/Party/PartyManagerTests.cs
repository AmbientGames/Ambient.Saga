using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Party;
using Ambient.Saga.Engine.Domain.Rpg.Reputation;
using Xunit;

namespace Ambient.Saga.Engine.Tests.Rpg.Party;

/// <summary>
/// Tests for PartyManager - party slot calculation, membership management, and reputation-gated slots.
/// </summary>
public class PartyManagerTests
{
    #region GetMaxPartySlots Tests

    [Theory]
    [InlineData(ReputationLevel.Hated, 0)]
    [InlineData(ReputationLevel.Hostile, 0)]
    [InlineData(ReputationLevel.Unfriendly, 1)]
    [InlineData(ReputationLevel.Neutral, 1)]
    [InlineData(ReputationLevel.Friendly, 2)]
    [InlineData(ReputationLevel.Honored, 3)]
    [InlineData(ReputationLevel.Revered, 3)]
    [InlineData(ReputationLevel.Exalted, 4)]
    public void GetMaxPartySlots_ByLevel_ReturnsCorrectSlots(ReputationLevel level, int expectedSlots)
    {
        // Act
        var slots = PartyManager.GetMaxPartySlots(level);

        // Assert
        Assert.Equal(expectedSlots, slots);
    }

    [Theory]
    [InlineData(-42000, 0)]  // Hated
    [InlineData(-21000, 0)]  // Hostile
    [InlineData(-6000, 1)]   // Unfriendly
    [InlineData(0, 1)]       // Neutral
    [InlineData(3000, 2)]    // Friendly
    [InlineData(9000, 3)]    // Honored
    [InlineData(21000, 3)]   // Revered
    [InlineData(42000, 4)]   // Exalted
    public void GetMaxPartySlots_ByValue_ReturnsCorrectSlots(int reputationValue, int expectedSlots)
    {
        // Act
        var slots = PartyManager.GetMaxPartySlots(reputationValue);

        // Assert
        Assert.Equal(expectedSlots, slots);
    }

    #endregion

    #region GetPartySize Tests

    [Fact]
    public void GetPartySize_NullParty_ReturnsZero()
    {
        // Act
        var size = PartyManager.GetPartySize(null);

        // Assert
        Assert.Equal(0, size);
    }

    [Fact]
    public void GetPartySize_EmptyParty_ReturnsZero()
    {
        // Arrange
        var party = new PartyInventory();

        // Act
        var size = PartyManager.GetPartySize(party);

        // Assert
        Assert.Equal(0, size);
    }

    [Fact]
    public void GetPartySize_WithMembers_ReturnsCorrectCount()
    {
        // Arrange
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" },
                new PartyMember { SlotRef = "Party2", CharacterRef = "COMPANION_B" }
            }
        };

        // Act
        var size = PartyManager.GetPartySize(party);

        // Assert
        Assert.Equal(2, size);
    }

    #endregion

    #region HasAvailableSlot Tests

    [Fact]
    public void HasAvailableSlot_NullPartyNeutralRep_ReturnsTrue()
    {
        // Neutral reputation = 1 slot, null party = 0 members
        var hasSlot = PartyManager.HasAvailableSlot(null, 0);

        Assert.True(hasSlot);
    }

    [Fact]
    public void HasAvailableSlot_FullParty_ReturnsFalse()
    {
        // Arrange - Neutral has 1 slot
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        // Act - at Neutral reputation (1 slot max)
        var hasSlot = PartyManager.HasAvailableSlot(party, 0);

        // Assert
        Assert.False(hasSlot);
    }

    [Fact]
    public void HasAvailableSlot_PartiallyFullParty_ReturnsTrue()
    {
        // Arrange - Friendly has 2 slots
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        // Act - at Friendly reputation (2 slots max)
        var hasSlot = PartyManager.HasAvailableSlot(party, 3000);

        // Assert
        Assert.True(hasSlot);
    }

    [Fact]
    public void HasAvailableSlot_HatedReputation_ReturnsFalse()
    {
        // Hated reputation = 0 slots
        var hasSlot = PartyManager.HasAvailableSlot(null, -42000);

        Assert.False(hasSlot);
    }

    #endregion

    #region IsInParty Tests

    [Fact]
    public void IsInParty_NullParty_ReturnsFalse()
    {
        var result = PartyManager.IsInParty(null, "COMPANION_A");

        Assert.False(result);
    }

    [Fact]
    public void IsInParty_EmptyCharacterRef_ReturnsFalse()
    {
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        var result = PartyManager.IsInParty(party, "");

        Assert.False(result);
    }

    [Fact]
    public void IsInParty_CharacterInParty_ReturnsTrue()
    {
        // Arrange
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" },
                new PartyMember { SlotRef = "Party2", CharacterRef = "COMPANION_B" }
            }
        };

        // Act
        var result = PartyManager.IsInParty(party, "COMPANION_B");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInParty_CharacterNotInParty_ReturnsFalse()
    {
        // Arrange
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        // Act
        var result = PartyManager.IsInParty(party, "COMPANION_C");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetNextAvailableSlot Tests

    [Fact]
    public void GetNextAvailableSlot_EmptyParty_ReturnsParty1()
    {
        // Act - Neutral reputation (1 slot)
        var slot = PartyManager.GetNextAvailableSlot(null, 0);

        // Assert
        Assert.Equal("Party1", slot);
    }

    [Fact]
    public void GetNextAvailableSlot_Party1Used_ReturnsParty2()
    {
        // Arrange
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        // Act - Friendly reputation (2 slots)
        var slot = PartyManager.GetNextAvailableSlot(party, 3000);

        // Assert
        Assert.Equal("Party2", slot);
    }

    [Fact]
    public void GetNextAvailableSlot_AllSlotsUsed_ReturnsNull()
    {
        // Arrange - Neutral has 1 slot
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        // Act - Neutral reputation (1 slot max)
        var slot = PartyManager.GetNextAvailableSlot(party, 0);

        // Assert
        Assert.Null(slot);
    }

    [Fact]
    public void GetNextAvailableSlot_GapInSlots_ReturnsFirstAvailable()
    {
        // Arrange - Party2 used, but Party1 is free
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party2", CharacterRef = "COMPANION_B" }
            }
        };

        // Act - Friendly reputation (2 slots)
        var slot = PartyManager.GetNextAvailableSlot(party, 3000);

        // Assert
        Assert.Equal("Party1", slot);
    }

    #endregion

    #region AddPartyMember Tests

    [Fact]
    public void AddPartyMember_EmptyCharacterRef_ReturnsOriginalParty()
    {
        var party = new PartyInventory();

        var result = PartyManager.AddPartyMember(party, "", 0);

        Assert.Same(party, result);
    }

    [Fact]
    public void AddPartyMember_NullParty_CreatesNewParty()
    {
        // Act
        var result = PartyManager.AddPartyMember(null, "COMPANION_A", 0);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Member);
        Assert.Equal("COMPANION_A", result.Member[0].CharacterRef);
        Assert.Equal("Party1", result.Member[0].SlotRef);
    }

    [Fact]
    public void AddPartyMember_AlreadyInParty_ReturnsOriginalParty()
    {
        // Arrange
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        // Act
        var result = PartyManager.AddPartyMember(party, "COMPANION_A", 3000);

        // Assert
        Assert.Same(party, result);
        Assert.Single(result.Member);
    }

    [Fact]
    public void AddPartyMember_NoSlotsAvailable_ReturnsNull()
    {
        // Arrange - Neutral reputation (1 slot)
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        // Act
        var result = PartyManager.AddPartyMember(party, "COMPANION_B", 0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AddPartyMember_SlotAvailable_AddsToParty()
    {
        // Arrange - Friendly reputation (2 slots)
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        // Act
        var result = PartyManager.AddPartyMember(party, "COMPANION_B", 3000);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Member.Length);
        Assert.Contains(result.Member, m => m.CharacterRef == "COMPANION_B");
        Assert.Contains(result.Member, m => m.SlotRef == "Party2");
    }

    [Fact]
    public void AddPartyMember_SetsJoinedDate()
    {
        // Act
        var before = DateTime.UtcNow;
        var result = PartyManager.AddPartyMember(null, "COMPANION_A", 0);
        var after = DateTime.UtcNow;

        // Assert
        Assert.NotNull(result?.Member[0].JoinedDate);
        var joinedDate = DateTime.Parse(result.Member[0].JoinedDate, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.InRange(joinedDate, before.AddSeconds(-1), after.AddSeconds(1));  // Allow small timing variance
    }

    #endregion

    #region RemovePartyMember Tests

    [Fact]
    public void RemovePartyMember_NullParty_ReturnsNull()
    {
        var result = PartyManager.RemovePartyMember(null, "COMPANION_A");

        Assert.Null(result);
    }

    [Fact]
    public void RemovePartyMember_EmptyCharacterRef_ReturnsOriginalParty()
    {
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        var result = PartyManager.RemovePartyMember(party, "");

        Assert.Same(party, result);
    }

    [Fact]
    public void RemovePartyMember_CharacterInParty_RemovesFromParty()
    {
        // Arrange
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" },
                new PartyMember { SlotRef = "Party2", CharacterRef = "COMPANION_B" }
            }
        };

        // Act
        var result = PartyManager.RemovePartyMember(party, "COMPANION_A");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Member);
        Assert.Equal("COMPANION_B", result.Member[0].CharacterRef);
    }

    [Fact]
    public void RemovePartyMember_CharacterNotInParty_ReturnsUnchangedParty()
    {
        // Arrange
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" }
            }
        };

        // Act
        var result = PartyManager.RemovePartyMember(party, "COMPANION_C");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Member);
        Assert.Equal("COMPANION_A", result.Member[0].CharacterRef);
    }

    #endregion

    #region GetPartyMemberRefs Tests

    [Fact]
    public void GetPartyMemberRefs_NullParty_ReturnsEmptyArray()
    {
        var refs = PartyManager.GetPartyMemberRefs(null);

        Assert.Empty(refs);
    }

    [Fact]
    public void GetPartyMemberRefs_WithMembers_ReturnsAllRefs()
    {
        // Arrange
        var party = new PartyInventory
        {
            Member = new[]
            {
                new PartyMember { SlotRef = "Party1", CharacterRef = "COMPANION_A" },
                new PartyMember { SlotRef = "Party2", CharacterRef = "COMPANION_B" },
                new PartyMember { SlotRef = "Party3", CharacterRef = "COMPANION_C" }
            }
        };

        // Act
        var refs = PartyManager.GetPartyMemberRefs(party);

        // Assert
        Assert.Equal(3, refs.Length);
        Assert.Contains("COMPANION_A", refs);
        Assert.Contains("COMPANION_B", refs);
        Assert.Contains("COMPANION_C", refs);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void PartySlotProgression_ReputationIncrease_UnlocksMoreSlots()
    {
        // Start at Neutral (1 slot)
        var party = PartyManager.AddPartyMember(null, "COMPANION_A", 0);
        Assert.NotNull(party);
        Assert.Single(party.Member);

        // Can't add second member at Neutral
        var cantAdd = PartyManager.AddPartyMember(party, "COMPANION_B", 0);
        Assert.Null(cantAdd);

        // Increase to Friendly (2 slots) - now can add
        party = PartyManager.AddPartyMember(party, "COMPANION_B", 3000);
        Assert.NotNull(party);
        Assert.Equal(2, party.Member.Length);

        // Increase to Honored (3 slots) - can add third
        party = PartyManager.AddPartyMember(party, "COMPANION_C", 9000);
        Assert.NotNull(party);
        Assert.Equal(3, party.Member.Length);

        // Increase to Exalted (4 slots) - can add fourth
        party = PartyManager.AddPartyMember(party, "COMPANION_D", 42000);
        Assert.NotNull(party);
        Assert.Equal(4, party.Member.Length);

        // Can't add fifth even at Exalted
        var cantAddFifth = PartyManager.AddPartyMember(party, "COMPANION_E", 42000);
        Assert.Null(cantAddFifth);
    }

    [Fact]
    public void PartyManagement_AddAndRemove_MaintainsCorrectState()
    {
        // Add companions
        var party = PartyManager.AddPartyMember(null, "COMPANION_A", 9000);  // Honored (3 slots)
        party = PartyManager.AddPartyMember(party, "COMPANION_B", 9000);
        party = PartyManager.AddPartyMember(party, "COMPANION_C", 9000);

        Assert.Equal(3, PartyManager.GetPartySize(party));
        Assert.True(PartyManager.IsInParty(party, "COMPANION_B"));
        Assert.False(PartyManager.HasAvailableSlot(party, 9000));

        // Remove middle companion
        party = PartyManager.RemovePartyMember(party, "COMPANION_B");
        Assert.Equal(2, PartyManager.GetPartySize(party));
        Assert.False(PartyManager.IsInParty(party, "COMPANION_B"));
        Assert.True(PartyManager.HasAvailableSlot(party, 9000));

        // Can add new companion in vacated slot
        party = PartyManager.AddPartyMember(party, "COMPANION_D", 9000);
        Assert.Equal(3, PartyManager.GetPartySize(party));
        Assert.True(PartyManager.IsInParty(party, "COMPANION_D"));
    }

    #endregion
}
