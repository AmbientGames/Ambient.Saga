using Ambient.Domain.Contracts;
using MediatR;

namespace Ambient.Saga.Engine.Application.Queries.Loading;

/// <summary>
/// Query to load all available world configurations from disk.
/// This is a read-only infrastructure operation exposed through CQRS.
///
/// Usage:
/// - Game startup: Show list of available worlds to load
/// - Configuration UI: Display world selection dropdown
/// </summary>
public record LoadAvailableWorldConfigurationsQuery : IRequest<IWorldConfiguration[]>
{
    /// <summary>
    /// Base data directory containing WorldConfigurations.xml
    /// </summary>
    public required string DataDirectory { get; init; }

    /// <summary>
    /// Definition directory for XSD validation
    /// </summary>
    public required string DefinitionDirectory { get; init; }
}
