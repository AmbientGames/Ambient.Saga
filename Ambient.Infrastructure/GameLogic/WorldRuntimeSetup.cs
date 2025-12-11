using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using System.Diagnostics;

namespace Ambient.Infrastructure.GameLogic;

public static class WorldRuntimeSetup
{
    public static void LoadWorld(IWorld world)
    {
        LoadGenerationDetails(world);

        LoadTools(world);
    }

    private static void LoadGenerationDetails(IWorld world)
    {
        if (world.WorldConfiguration.StartDate > DateTime.UtcNow)
        {
            Debug.WriteLine("is this really necessary");
            world.WorldConfiguration.StartDate = DateTime.UtcNow;
        }
    }

    private static void LoadTools(IWorld world)
    {
        foreach (var tool in world.Gameplay.Tools)
        {
            foreach (var substance in tool.EffectiveSubstances)
            {
                tool.Class |= SubstanceSuitabilityEncoder.Encode(substance.SubstanceRef);
            }
        }
    }
}