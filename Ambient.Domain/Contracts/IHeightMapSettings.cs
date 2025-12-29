namespace Ambient.Domain.Contracts;

public interface IHeightMapSettings
{
    string RelativePath { get; set; }
    double MapResolutionInMeters { get; set; }
    double HorizontalScale { get; set; }
    double VerticalScale { get; set; }
    double VerticalShift { get; set; }
}