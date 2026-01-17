namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

// todo: this changed due to soft coding of substances
public static class SubstanceSuitabilityEncoder
{    
    private static readonly Dictionary<string, uint> MaterialBits = new()
    {
        { "Aggregate", 1 },
        { "Carbon", 2 },
        { "Stone", 4 },
        { "Metal", 8 },
        { "Plant", 16 },
        { "Wood", 32 },
        { "Other", 64 },
        { "Reserved7", 128 },
        { "Reserved8", 256 },
        { "Reserved9", 512 },
        { "Reserved10", 1024 },
        { "Reserved11", 2048 },
        { "Reserved12", 4096 },
        { "Reserved13", 8192 },
        { "Reserved14", 16384 },
        { "Reserved15", 32768 }
    };

    public static uint Encode(string subtance)
    {
        return MaterialBits.TryGetValue(subtance, out var bits) ? bits : 0;
    }
}