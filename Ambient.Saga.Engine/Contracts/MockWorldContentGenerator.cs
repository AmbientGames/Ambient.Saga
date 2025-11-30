using Ambient.Domain;

namespace Ambient.Saga.Engine.Contracts;

/// <summary>
/// Mock implementation of IWorldContentGenerator for when WorldForge is not available.
/// Returns IsAvailable = false and provides an appropriate message.
/// </summary>
public class MockWorldContentGenerator : IWorldContentGenerator
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string StatusMessage => "World content generation requires WorldForge, which is not included in this build.";

    /// <inheritdoc />
    public List<string> GenerateWorldContent(WorldConfiguration worldConfig, string outputDirectory)
    {
        // Return empty list - generation not available
        return new List<string>();
    }
}
