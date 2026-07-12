namespace Ambient.Domain;

/// <summary>
/// Trait checks — the single definition of what it means for a character to
/// carry a trait. Evaluators, validators, and tests must all use these instead
/// of hand-rolling Traits.Any(...) so the truth can never drift.
/// </summary>
public partial class Character
{
    /// <summary>
    /// True when the character carries the trait with a non-zero value
    /// (boolean traits use Value=1; numeric traits are "carried" when set).
    /// </summary>
    public bool CarriesTrait(CharacterTraitType trait)
    {
        if (Traits == null) return false;

        foreach (var t in Traits)
        {
            if (t.Name == trait && t.Value != 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the character can end up in combat with the avatar
    /// (hostile or a boss encounter).
    /// </summary>
    public bool IsFightable => CarriesTrait(CharacterTraitType.Hostile) || CarriesTrait(CharacterTraitType.BossFight);
}
