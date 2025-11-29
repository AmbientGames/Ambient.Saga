using System.Xml.Serialization;

namespace Ambient.Saga.WorldForge.Models;

/// <summary>
/// Theme manifest metadata (from Theme.xml)
/// </summary>
[XmlRoot("Theme", Namespace = "Ambient.Saga.WorldForge")]
public class ThemeDefinition
{
    [XmlAttribute("RefName")]
    public string RefName { get; set; } = string.Empty;

    [XmlAttribute("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [XmlElement("Description")]
    public string? Description { get; set; }

    [XmlElement("Author")]
    public string? Author { get; set; }

    [XmlElement("Version")]
    public string? Version { get; set; }

    [XmlArray("Tags")]
    [XmlArrayItem("Tag")]
    public List<string>? Tags { get; set; }
}
