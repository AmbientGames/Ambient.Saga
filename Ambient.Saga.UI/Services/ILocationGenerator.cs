namespace Ambient.Saga.UI.Services;

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
/// Battle dialogue lines for Boss/Hostile characters.
/// </summary>
public sealed record BattleDialogueEntry(
    string Opening,
    string? FirstBlood,
    string Berserk,
    string? Retreat,
    string Defeated);

/// <summary>
/// Dialogue hints for generating full conversation trees.
/// </summary>
public sealed record DialogueHintsEntry(
    string Backstory,
    string? QuestHook,
    string? TradeComment,
    string? Rumor,
    string Farewell);

/// <summary>
/// Character data for a location's NPC.
/// </summary>
public sealed record CharacterEntry(
    string Name,
    string Role,
    string Greeting,
    string Personality,
    BattleDialogueEntry? BattleDialogue,
    DialogueHintsEntry? DialogueHints);

/// <summary>
/// Story assignment connecting a location to a canon story bundle.
/// </summary>
public sealed record StoryAssignmentEntry(
    string Story,
    string StoryRole,
    string? CharacterRef);

/// <summary>
/// A single point of interest with geographic coordinates, character, and optional story assignment.
/// </summary>
public sealed record GeneratedLocationEntry(
    string Name,
    string Description,
    double Latitude,
    double Longitude,
    string Category,
    string Kind,
    CharacterEntry? Character,
    StoryAssignmentEntry? StoryAssignment);

/// <summary>
/// Request parameters for generating locations.
/// </summary>
public sealed record LocationGenerationRequest(
    string RegionName,
    string Theme,
    string WorldType,  // "real" or "procedural"
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
