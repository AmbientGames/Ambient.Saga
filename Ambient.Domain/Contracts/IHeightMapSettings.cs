using Ambient.Domain.Sampling;

namespace Ambient.Domain.Contracts;

public interface IHeightMapSettings
{
    string FileName { get; set; }
    double MapResolutionInMeters { get; set; }
    double HorizontalScale { get; set; }
    double VerticalScale { get; set; }
    double VerticalShift { get; set; }
    ElevationWaterMap? ElevationWaterMap { get; set; }
}