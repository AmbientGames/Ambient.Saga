using Ambient.Domain;
using Ambient.Saga.Engine.Application.Queries.Loading;
using Ambient.Saga.Engine.Infrastructure.Loading;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Loading;

/// <summary>
/// Handler for LoadAvailableWorldConfigurationsQuery.
/// Wraps WorldAssetLoader.LoadAvailableWorldConfigurationsAsync in CQRS pattern.
///
/// This allows views to load world configurations through the MediatR pipeline,
/// ensuring consistent logging, error handling, and future enhancements (caching, etc).
/// </summary>
internal sealed class LoadAvailableWorldConfigurationsHandler : IRequestHandler<LoadAvailableWorldConfigurationsQuery, WorldConfiguration[]>
{
    public async Task<WorldConfiguration[]> Handle(LoadAvailableWorldConfigurationsQuery query, CancellationToken ct)
    {
        // Delegate to existing infrastructure loader
        return await WorldConfigurationLoader.LoadAvailableWorldConfigurationsAsync(
            query.DataDirectory,
            query.DefinitionDirectory);
    }
}
