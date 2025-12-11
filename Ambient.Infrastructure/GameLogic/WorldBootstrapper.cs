using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Enums;
using Ambient.Domain.ValueObjects;
using Ambient.Infrastructure.GameLogic.Services;

namespace Ambient.Infrastructure.GameLogic;

/// <summary>
/// Provides static methods for initializing and configuring world instances.
/// Handles world setup, texture loading, block derivation, and climate zone generation.
/// </summary>
public static class WorldBootstrapper
{
    /// <summary>
    /// Initializes a world instance using its existing configuration.
    /// Loads textures and applies all world settings using current world properties.
    /// </summary>
    /// <param name="world">The world instance to initialize</param>
    public static void Initialize(IWorld world)
    {
        WorldRuntimeSetup.LoadWorld(world);

        InitializeWorldSettings(world);
    }

    public static void Initialize(IWorld world, WorldConfiguration worldConfiguration)
    {
        WorldRuntimeSetup.LoadWorld(world);

        world.WorldConfiguration = worldConfiguration;

        InitializeWorldSettings(world);
    }


    private static void InitializeWorldSettings(IWorld world)
    {
        // Apply world configuration processing (serialization fix, scale, timing)
        WorldConfigurationService.InitializeWorldTiming(world);
    }
}