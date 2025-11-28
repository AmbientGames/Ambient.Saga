using Ambient.Domain;
using Ambient.StoryGenerator;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Manages unique RefName generation for source locations and intermediate waypoints
/// </summary>
public class RefNameGenerator
{
    private readonly Dictionary<SourceLocation, string> _refNameMap = new();
    private readonly HashSet<string> _usedRefNames = new();

    /// <summary>
    /// Gets the RefName for a location, or generates a new unique one if not already mapped
    /// </summary>
    public string GetOrGenerateRefName(SourceLocation location)
    {
        if (_refNameMap.TryGetValue(location, out var existingRefName))
            return existingRefName;

        var refName = GenerateUniqueRefName(location.DisplayName);
        _refNameMap[location] = refName;
        return refName;
    }

    /// <summary>
    /// Generates a unique RefName for an intermediate waypoint
    /// </summary>
    public string GenerateIntermediateRefName(SourceLocation location, string sourceRefName, string targetRefName, int index)
    {
        var baseRefName = $"{sourceRefName}_to_{targetRefName}_{index}";
        var refName = baseRefName;
        var counter = 1;

        while (_usedRefNames.Contains(refName))
        {
            refName = $"{baseRefName}_{counter}";
            counter++;
        }

        _usedRefNames.Add(refName);
        _refNameMap[location] = refName;
        return refName;
    }

    /// <summary>
    /// Gets the RefName for an already-mapped location
    /// </summary>
    public string GetRefName(SourceLocation location)
    {
        return _refNameMap[location];
    }

    /// <summary>
    /// Checks if a location already has a RefName assigned
    /// </summary>
    public bool HasRefName(SourceLocation location)
    {
        return _refNameMap.ContainsKey(location);
    }

    private string GenerateUniqueRefName(string displayName)
    {
        var baseRefName = DisplayNameToRefName(displayName);
        var refName = baseRefName;
        var counter = 1;

        while (_usedRefNames.Contains(refName))
        {
            refName = $"{baseRefName}{counter}";
            counter++;
        }

        _usedRefNames.Add(refName);
        return refName;
    }

    private string DisplayNameToRefName(string displayName)
    {
        // Remove punctuation and special characters, keep only letters, digits
        var cleaned = new string(displayName
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray());

        // Remove spaces and convert to PascalCase-like format
        var parts = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var refName = string.Join("", parts.Select(p =>
            char.ToUpper(p[0]) + p.Substring(1).ToLower()));

        return string.IsNullOrEmpty(refName) ? "Location" : refName;
    }
}
