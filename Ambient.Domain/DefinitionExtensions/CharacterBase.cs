using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Ambient.Domain;

public partial class CharacterBase
{
    [XmlIgnore] public Dictionary<string, string>? CombatProfile { get; set; } = new Dictionary<string, string>();
}