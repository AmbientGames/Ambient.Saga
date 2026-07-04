namespace Ambient.Domain;

/// <summary>
/// Common read surface for the two generated stat blocks: Attributes
/// (multiplier semantics — omitted resource attributes default to the 1.0
/// identity) and EffectAttributes (additive effect/threshold semantics —
/// everything defaults to 0, so an omitted attribute means "no effect").
/// Lets display/utility code render either without caring which it has.
/// </summary>
public interface IStatAttributes
{
    float Health { get; }
    float Stamina { get; }
    float Mana { get; }
    float Temperature { get; }
    float Strength { get; }
    float Defense { get; }
    float Magic { get; }
    float Speed { get; }
    float Endurance { get; }
}

public partial class Attributes : IStatAttributes { }

public partial class EffectAttributes : IStatAttributes { }
