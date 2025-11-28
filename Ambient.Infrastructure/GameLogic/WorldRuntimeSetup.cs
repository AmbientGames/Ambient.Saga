using Ambient.Application.Utilities;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using System.Diagnostics;

namespace Ambient.Infrastructure.GameLogic;

public static class WorldRuntimeSetup
{
    public static void LoadWorld(World world)
    {
        LoadGenerationDetails(world);

        LoadTools(world);
    }

    private static void LoadGenerationDetails(World world)
    {
        if (world.WorldConfiguration.StartDate > DateTime.UtcNow)
        {
            Debug.WriteLine("is this really necessary");
            world.WorldConfiguration.StartDate = DateTime.UtcNow;
        }
    }

    private static void LoadTools(World world)
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