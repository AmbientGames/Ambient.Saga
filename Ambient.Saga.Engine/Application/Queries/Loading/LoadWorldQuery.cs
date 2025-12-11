using Ambient.Domain.DefinitionExtensions;
using MediatR;

namespace Ambient.Saga.Engine.Application.Queries.Loading;

/// <summary>
/// Query to load a complete world by configuration RefName.
/// This is a read-only infrastructure operation exposed through CQRS.
///
/// This is a HEAVY operation that:
/// - Loads world configuration and template
/// - Loads all gameplay data (characters, items, dialogue, sagas)
/// - Loads simulation data (blocks, materials, climate)
/// - Loads presentation data (textures, graphics)
/// - Builds all lookup dictionaries and calculates derived data
///
/// Usage:
/// - Game startup: Load selected world
/// - World switching: Unload current, load new
/// </summary>
public record LoadWorldQuery : IRequest<IWorld>
{
    /// <summary>
    /// Base data directory containing world templates
    /// </summary>
    public required string DataDirectory { get; init; }

    /// <summary>
    /// Definition directory for XSD validation
    /// </summary>
    public required string DefinitionDirectory { get; init; }

    /// <summary>
    /// The RefName of the WorldConfiguration to load (e.g., "Kagoshima", "Kyoto")
    /// </summary>
    public required string ConfigurationRefName { get; init; }
}
