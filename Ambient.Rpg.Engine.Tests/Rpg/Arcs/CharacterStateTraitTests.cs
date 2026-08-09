using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Xunit;

namespace Ambient.Rpg.Engine.Tests.Rpg.Arcs;

/// <summary>
/// Value-aware trait semantics on the replayed character state. A key-only
/// presence check (Traits.ContainsKey) once treated a deliberately-off trait as
/// on, so an NPC authored Hostile="0" auto-assaulted the player. CharacterState
/// preserves the authored value (ArcStateMachine keeps trait.Value), and
/// CarriesTrait must honor it.
/// </summary>
public class CharacterStateTraitTests
{
    [Fact]
    public void CarriesTrait_ExplicitZero_IsNotCarried()
    {
        // <Trait Name="Hostile" Value="0"/> — a deliberately non-hostile NPC.
        var state = new CharacterState();
        state.Traits["Hostile"] = 0;
        Assert.False(state.CarriesTrait("Hostile"));
    }

    [Fact]
    public void CarriesTrait_NonZeroValue_IsCarried()
    {
        var state = new CharacterState();
        state.Traits["Hostile"] = 1;
        Assert.True(state.CarriesTrait("Hostile"));
    }

    [Fact]
    public void CarriesTrait_BooleanFlagNull_IsCarried()
    {
        // A bare <Trait Name="Hostile"/> is stored as null and means present.
        var state = new CharacterState();
        state.Traits["Hostile"] = null;
        Assert.True(state.CarriesTrait("Hostile"));
    }

    [Fact]
    public void CarriesTrait_Missing_IsNotCarried()
    {
        var state = new CharacterState();
        Assert.False(state.CarriesTrait("Hostile"));
    }
}
