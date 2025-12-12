using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Queries.Loading;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Loading;

/// <summary>
/// Handler for LoadAvailableWorldConfigurationsQuery.
/// Wraps IWorldConfigurationLoader.LoadAvailableWorldConfigurationsAsync in CQRS pattern.
///
/// This allows views to load world configurations through the MediatR pipeline,
/// ensuring consistent logging, error handling, and future enhancements (caching, etc).
/// </summary>
internal sealed class LoadAvailableWorldConfigurationsHandler : IRequestHandler<LoadAvailableWorldConfigurationsQuery, WorldConfiguration[]>
{
    private readonly IWorldConfigurationLoader _configurationLoader;

    public LoadAvailableWorldConfigurationsHandler(IWorldConfigurationLoader configurationLoader)
    {
        _configurationLoader = configurationLoader;
    }

    public async Task<WorldConfiguration[]> Handle(LoadAvailableWorldConfigurationsQuery query, CancellationToken ct)
    {
        // Delegate to injected configuration loader
        return await _configurationLoader.LoadAvailableWorldConfigurationsAsync(
            query.DataDirectory,
            query.DefinitionDirectory);
    }
}
