using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Contracts;

namespace Ambient.Saga.Sandbox.DirectX.WorldContentGenerators;

/// <summary>
/// Returns IsAvailable = false and provides an appropriate message.
/// </summary>
public class MockWorldContentGenerator : IWorldContentGenerator
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string StatusMessage => "World content generation mock for future world content generator.";

    /// <inheritdoc />
    public List<string> GenerateWorldContent(IWorldConfiguration worldConfig, string outputDirectory)
    {
        // Return empty list - generation not available
        return new List<string>();
    }
}
