using System.Reflection;

namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

public static class CharacterStatsCopier
{

    public static void CopyCharacterStats(CharacterStats sourceStats, CharacterStats destinationStats)
    {
        var type = typeof(CharacterStats);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            // Only copy if both readable and writable
            if (prop.CanRead && prop.CanWrite)
            {
                var value = prop.GetValue(sourceStats);
                prop.SetValue(destinationStats, value);
            }
        }
    }
}
