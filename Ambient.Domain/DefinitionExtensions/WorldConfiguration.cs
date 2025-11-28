using Ambient.Domain.DefinitionExtensions;
using System.Diagnostics;
using System.Xml.Serialization;

namespace Ambient.Domain;

/// <summary>
/// Used to handle Item deserialization issues.
/// </summary>
public partial class WorldConfiguration
{
    [XmlIgnore] public ProceduralSettings ProceduralSettings { get; set; }
    [XmlIgnore] public HeightMapSettings HeightMapSettings { get; set; }
}
