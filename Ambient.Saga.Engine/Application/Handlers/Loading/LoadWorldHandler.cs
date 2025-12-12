using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Queries.Loading;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Loading;

/// <summary>
/// Handler for LoadWorldQuery.
/// Wraps IWorldLoader.LoadWorldByConfigurationAsync in CQRS pattern.
///
/// This is a HEAVY operation that loads:
/// - World configuration and template
/// - All gameplay data (characters, items, dialogue, sagas)
/// - All simulation data (blocks, materials, climate)
/// - All presentation data (textures, graphics)
/// - Builds lookup dictionaries and calculates derived data
///
/// This allows views to load worlds through the MediatR pipeline,
/// ensuring consistent logging, error handling, and future enhancements
/// (caching, lazy loading, background loading, etc).
/// </summary>
internal sealed class LoadWorldHandler : IRequestHandler<LoadWorldQuery, IWorld>
{
    private readonly IWorldLoader _worldLoader;

    public LoadWorldHandler(IWorldLoader worldLoader)
    {
        _worldLoader = worldLoader;
    }

    public async Task<IWorld> Handle(LoadWorldQuery query, CancellationToken ct)
    {
        // Delegate to injected world loader
        return await _worldLoader.LoadWorldByConfigurationAsync(
            query.DataDirectory,
            query.DefinitionDirectory,
            query.ConfigurationRefName);
    }
}
