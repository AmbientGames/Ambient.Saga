using Ambient.Domain;

namespace Ambient.Saga.Presentation.UI.Services;

/// <summary>
/// Abstraction for selecting an avatar archetype
/// Allows MainViewModel to work with both WPF and ImGui implementations
/// </summary>
public interface IArchetypeSelector
{
    /// <summary>
    /// Prompts the user to select an archetype
    /// </summary>
    /// <param name="archetypes">Available archetypes to choose from</param>
    /// <param name="currencyName">Currency name to display in selection UI</param>
    /// <returns>Selected archetype, or null if cancelled</returns>
    Task<AvatarArchetype?> SelectArchetypeAsync(
        IEnumerable<AvatarArchetype> archetypes,
        string? currencyName);
}
