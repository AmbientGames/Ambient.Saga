namespace Ambient.Application.WorldCreation;

/// <summary>
/// Parameters required to create a new world.
/// This is a data transfer object that captures all user choices from the world creation wizard.
/// </summary>
public class WorldCreationParameters
{
    // Basic world info
    public required string WorldRef { get; init; }
    public required string DisplayName { get; init; }
    public required string Theme { get; init; }

    // World type
    public required bool IsRealWorld { get; init; }

    // Height map settings (for real world terrain)
    public string? TerrainFilePath { get; init; }
    public int MinElevation { get; init; }
    public int MaxElevation { get; init; }
    public double[]? GeoTransform { get; init; }

    // Procedural settings (for generated terrain)
    public string? ProceduralMode { get; init; }

    // Spawn location
    public double SpawnLatitude { get; init; }
    public double SpawnLongitude { get; init; }

    // Calculated or selected chunk height
    public int ChunkHeight { get; init; }

    // Locations for generation
    public List<LocationEntry> Locations { get; init; } = new();
    public string LocationGenerationType { get; init; } = "radial";
}

/// <summary>
/// A location entry for world generation.
/// </summary>
public class LocationEntry
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string Category { get; init; } = "";
    public string Kind { get; init; } = "";
}
