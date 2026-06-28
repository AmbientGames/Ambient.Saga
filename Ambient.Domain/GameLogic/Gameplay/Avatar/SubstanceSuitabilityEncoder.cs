namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

// todo: this changed due to soft coding of substances
public static class SubstanceSuitabilityEncoder
{    
    private static readonly Dictionary<string, uint> MaterialBits = new()
    {
        { "Stone", 1 },
        { "Concrete", 2 },
        { "Wood", 4 },
        { "Structural", 8 },
        { "Decorative", 16 },
        { "Metal", 32 },
        { "Alloy", 64 },
        { "Aggregate", 128 },
        { "Plant", 256 },
        { "Liquid", 512 },
        { "Ore", 1024 },
        { "Carbon", 2048 },
        { "Reserved12", 4096 },
        { "Reserved13", 8192 },
        { "Reserved14", 16384 },
        { "Miscellaneous", 32768 }
    };

    public static uint Encode(string subtance)
    {
        return MaterialBits.TryGetValue(subtance, out var bits) ? bits : 0;
    }
}