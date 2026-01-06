namespace Ambient.Saga.UI.Services;

/// <summary>
/// Interface for AI-powered world generation service.
/// Implementations orchestrate AI generation and save results to GenerationConfiguration.xml.
/// </summary>
public interface IAIWorldGenerationService
{
    /// <summary>
    /// Generates world content using AI and saves to GenerationConfiguration.xml.
    /// </summary>
    /// <param name="request">Location generation request with region and theme info</param>
    /// <param name="worldRef">RefName for the target world</param>
    /// <param name="displayName">Human-readable world name</param>
    /// <param name="description">World description</param>
    /// <param name="outputDirectory">Directory to save GenerationConfiguration.xml</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result containing success status and summary</returns>
    Task<WorldGenerationResult> GenerateAndSaveAsync(
        LocationGenerationRequest request,
        string worldRef,
        string displayName,
        string? description,
        string outputDirectory,
        CancellationToken ct = default);
}

/// <summary>
/// Result of AI world generation.
/// </summary>
public sealed record WorldGenerationResult(
    bool IsSuccess,
    string? ConfigurationPath,
    int LocationCount,
    int StoryCount,
    int CharacterCount,
    List<string>? ParseErrors,
    string Summary);
