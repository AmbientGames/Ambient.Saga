namespace Ambient.Rpg.Ui.Services;

/// <summary>
/// Interface for AI-powered location generation.
/// Implementations provide platform-specific authentication and API communication.
/// </summary>
public interface ILocationGenerator
{
    /// <summary>
    /// Generates locations for the given request.
    /// </summary>
    Task<LocationGenerationResponse> GenerateAsync(
        LocationGenerationRequest request,
        CancellationToken ct = default);
}

// ============================================================================
// DTOs for Location Generation
// ============================================================================

/// <summary>
/// A single point of interest with geographic coordinates. Characters, dialogue,
/// and story assignments are NOT part of location generation - they are produced
/// later, during world generation (world enrichment).
/// </summary>
public sealed record GeneratedLocationEntry(
    string Name,
    string Description,
    double Latitude,
    double Longitude,
    string Category,
    string Kind);

/// <summary>
/// Request parameters for generating locations.
/// </summary>
public sealed record LocationGenerationRequest(
    string WorldName,
    string? Suggestions,
    string ThemeRef,        // Theme folder name (e.g., "feudal_japan")
    string ThemeDisplayName, // Human-readable (e.g., "Feudal Japan (Edo period)")
    string WorldType,       // "real" or "procedural"
    double MinLatitude,
    double MaxLatitude,
    double MinLongitude,
    double MaxLongitude,
    double SpawnLatitude,
    double SpawnLongitude,
    int LocationCount = 20);

/// <summary>
/// Response from the location generation.
/// </summary>
public sealed record LocationGenerationResponse(
    List<GeneratedLocationEntry> Locations,
    string? RawContent,
    List<string>? ParseErrors);
