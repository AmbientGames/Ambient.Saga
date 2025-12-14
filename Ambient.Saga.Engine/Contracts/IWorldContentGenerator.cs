using Ambient.Domain.Contracts;

namespace Ambient.Saga.Engine.Contracts;

/// <summary>
/// Interface for world content generation (characters, dialogue, quests, equipment, etc.).
/// </summary>
public interface IWorldContentGenerator
{
    /// <summary>
    /// Indicates whether this implementation is fully functional.
    /// Mock implementations return false; real implementations return true.
    /// UI can use this to show/hide generation buttons or display appropriate messages.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Message describing the current availability status.
    /// For mock implementations, explains why generation is not available.
    /// </summary>
    string StatusMessage { get; }

    /// <summary>
    /// Generates world content (characters, dialogue, quests, equipment, etc.) for a world configuration.
    /// </summary>
    /// <param name="worldConfig">The world configuration to generate content for</param>
    /// <param name="outputDirectory">Base output directory (e.g., Content/Worlds)</param>
    /// <returns>List of generated file paths, or empty list if generation failed/unavailable</returns>
    Task<List<string>> GenerateWorldContentAsync(IWorldConfiguration worldConfig, string outputDirectory);
}
