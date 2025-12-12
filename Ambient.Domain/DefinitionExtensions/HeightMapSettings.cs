
using Ambient.Domain.Contracts;

namespace Ambient.Domain;


public partial class HeightMapSettings : IHeightMapSettings
{
    public double MapResolutionInMeters {  get; set; }
}
