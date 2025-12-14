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
    public async Task<List<string>> GenerateWorldContentAsync(IWorldConfiguration worldConfig, string outputDirectory)
    {
        await Task.CompletedTask;

        return new List<string>();
    }
}
