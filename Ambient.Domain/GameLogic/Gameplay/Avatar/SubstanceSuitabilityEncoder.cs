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
        { "Steel", 16 },
        { "SteelReinforcedConcrete", 32 },
        { "Plant", 64 },
        { "Wood", 128 },
        { "Other", 256 },
        { "Custom0", 512 },
        { "Custom1", 1024 },
        { "Custom2", 2048 },
        { "Custom3", 4096 },
        { "Custom4", 8192 },
        { "Custom5", 16384 },
        { "Custom6", 32768 },
        { "Custom7", 65536 },
        { "Custom8", 131072 },
        { "Custom9", 262144 }
    };

    public static uint Encode(string subtance)
    {
        return MaterialBits.TryGetValue(subtance, out var bits) ? bits : 0;
    }
}