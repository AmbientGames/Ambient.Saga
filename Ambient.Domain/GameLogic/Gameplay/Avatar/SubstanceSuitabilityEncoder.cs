namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

// todo: this changed due to soft coding of substances
public static class SubstanceSuitabilityEncoder
{    
    private static readonly Dictionary<string, uint> MaterialBits = new()
    {
        { "Stone", 1 },
        { "Concrete", 2 },
        { "Wood", 4 },
        { "Decorative", 8 },
        { "Metal", 16 },
        { "Alloy", 32 },
        { "Aggregate", 64 },
        { "Plant", 128 },
        { "Liquid", 256 },
        { "Ore", 512 },
        { "Carbon", 1024 },
        { "Reserved11", 2048 },
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