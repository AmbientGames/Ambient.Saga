namespace Ambient.Application.WorldCreation;

/// <summary>
/// Builds WorldConfiguration.xml content from world creation parameters.
/// </summary>
public class WorldConfigurationBuilder
{
    private const double VerticalScale = 0.333333;
    private const double HorizontalScale = 0.333333;

    /// <summary>
    /// Build the WorldConfiguration.xml content.
    /// </summary>
    public string Build(WorldCreationParameters parameters)
    {
        var seed = new Random().NextDouble();

        string terrainSettingsXml;
        if (parameters.IsRealWorld)
        {
            var terrainFileName = $"{parameters.WorldRef}_terrain.tif";
            var verticalShift = CalculateVerticalShift(parameters.MinElevation);

            terrainSettingsXml = $@"  <HeightMapSettings FileName=""{terrainFileName}"" HorizontalScale=""{HorizontalScale}"" VerticalScale=""{VerticalScale}"" VerticalShift=""{verticalShift}"" />";
        }
        else
        {
            terrainSettingsXml = $@"  <ProceduralSettings ProceduralGenerationMode=""{parameters.ProceduralMode}"" />";
        }

        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<WorldConfiguration xmlns=""Ambient.Core.Domain"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
    RefName=""{parameters.WorldRef}""
    DisplayName=""{EscapeXml(parameters.DisplayName)}""
    Description=""World generated from {(parameters.IsRealWorld ? "GeoTIFF elevation data" : "procedural terrain")} using World Creation Wizard""
    Namespace=""{parameters.WorldRef}""
    ContentPackLibrary=""{parameters.WorldRef}""
    ContentPackTheme=""{parameters.Theme}""
    SpawnLatitude=""{parameters.SpawnLatitude:F6}""
    SpawnLongitude=""{parameters.SpawnLongitude:F6}""
    Seed=""{seed:F4}""
    ShortNightsAndWinter=""true""
    ChunkHeight=""{parameters.ChunkHeight}"">
{terrainSettingsXml}
</WorldConfiguration>";
    }

    /// <summary>
    /// Calculate vertical shift: round down lowest elevation to nearest 100, then negate.
    /// e.g., 253 → -(253/100) * 100 = -200
    /// </summary>
    public static int CalculateVerticalShift(int minElevation)
    {
        return -(minElevation / 100) * 100;
    }

    /// <summary>
    /// Calculate the appropriate ChunkHeight for height map terrain.
    /// </summary>
    public static int CalculateChunkHeight(int minElevation, int maxElevation)
    {
        var verticalShift = CalculateVerticalShift(minElevation);
        var scaledMaxHeight = (maxElevation + verticalShift) * VerticalScale;

        int[] chunkHeightOptions = { 256, 512, 1024, 2048, 4096 };
        foreach (var option in chunkHeightOptions)
        {
            if (option >= scaledMaxHeight)
            {
                return option;
            }
        }
        return chunkHeightOptions[^1]; // Default to largest
    }

    private static string EscapeXml(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
