using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using System.Diagnostics;

namespace Ambient.Infrastructure.GameLogic;

public static class WorldRuntimeSetup
{
    public static void LoadWorld(IWorld world)
    {
        LoadGenerationDetails(world);
    }

    private static void LoadGenerationDetails(IWorld world)
    {
        if (world.WorldConfiguration.StartDate > DateTime.UtcNow)
        {
            Debug.WriteLine("is this really necessary");
            world.WorldConfiguration.StartDate = DateTime.UtcNow;
        }
    }
}
