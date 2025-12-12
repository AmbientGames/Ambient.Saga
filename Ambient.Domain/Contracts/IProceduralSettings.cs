namespace Ambient.Domain.Contracts;

public interface IProceduralSettings
{
    double LatitudeDegreesToUnits { get; set; }
    double LongitudeDegreesToUnits { get; set; }
}