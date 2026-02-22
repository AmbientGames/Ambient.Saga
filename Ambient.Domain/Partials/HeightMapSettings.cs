
using Ambient.Domain.Contracts;
using Ambient.Domain.Sampling;
using System.Xml.Serialization;

namespace Ambient.Domain;


public partial class HeightMapSettings : IHeightMapSettings
{
    public double MapResolutionInMeters {  get; set; }
    [XmlIgnore] public ElevationWaterMap ElevationWaterMap { get; set; }
}
