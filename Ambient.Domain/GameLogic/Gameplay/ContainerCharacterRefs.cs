namespace Ambient.Domain;

/// <summary>
/// CharacterRef strings the runtime uses for server-created container arcs
/// (shops, death remains, battle remains, geocaches). Worlds must catalog these
/// in Characters.xml — <c>CharacterSpawned</c> looks them up, and a miss drops
/// the spawn and every following <c>ItemTraded</c> seed.
/// </summary>
public static class ContainerCharacterRefs
{
    public const string RemnantLoot = "REMNANT_LOOT";
    public const string BattleLoot = "BATTLE_LOOT";
    public const string GeoCache = "GEOCACHE";

    public const string ShopkeeperPrefix = "SHOPKEEPER_Generic_";
    public const string ShopkeeperDialoguePrefix = "DIALOGUE_GenericTrader_";
    public const int ShopkeeperVariantCount = 10;

    public static IReadOnlyList<string> Shells { get; } =
        new[] { RemnantLoot, BattleLoot, GeoCache };

    public static string Shopkeeper(int index) => ShopkeeperPrefix + index;

    public static string ShopkeeperDialogue(int index) => ShopkeeperDialoguePrefix + index;

    public static IEnumerable<string> Shopkeepers =>
        Enumerable.Range(0, ShopkeeperVariantCount).Select(Shopkeeper);

    public static IEnumerable<string> ShopkeeperDialogueTrees =>
        Enumerable.Range(0, ShopkeeperVariantCount).Select(ShopkeeperDialogue);

    public static IEnumerable<string> RequiredCharacterRefs => Shells.Concat(Shopkeepers);
}
