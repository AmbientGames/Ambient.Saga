using Ambient.Saga.WorldForge.Models;

namespace Ambient.Saga.WorldForge.XmlGenerators;

/// <summary>
/// Interface for XML content generators.
/// Each implementation generates a specific XML file type (Sagas, Characters, Equipment, etc.)
/// Extracted from StoryGenerator as part of SRP refactoring.
/// </summary>
public interface IXmlContentGenerator
{
    /// <summary>
    /// Gets the name of this generator (for logging/debugging)
    /// </summary>
    string GeneratorName { get; }

    /// <summary>
    /// Generates XML content and writes it to the specified path.
    /// Implementations should handle all aspects of XML generation for their specific content type.
    /// </summary>
    /// <param name="context">Generation context containing all required data (world config, narrative, theme, locations, etc.)</param>
    /// <param name="outputPath">The full path where the XML file should be written</param>
    void GenerateXml(GenerationContext context, string outputPath);
}
