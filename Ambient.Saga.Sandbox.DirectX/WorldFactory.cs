using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.Saga.Sandbox.DirectX;

/// <summary>
/// Factory for creating World instances.
/// Located at the composition root (Sandbox) to allow different applications
/// to provide their own World implementations.
/// </summary>
public class WorldFactory : IWorldFactory
{
    /// <summary>
    /// Creates a new world instance.
    /// </summary>
    public IWorld CreateWorld() => new World();
}
