using System;

namespace Ambient.Domain.Contracts;

public interface IWorldConfiguration
{
    string RefName { get; set; }

    string ContentPackLibrary {  get; set; }
    string ContentPackTheme {  get; set; }
    string Namespace { get; set; }

    double SpawnLatitude { get; set; }
    double SpawnLongitude { get; set; }
    IProceduralSettings ProceduralSettings { get; set; }
    IHeightMapSettings HeightMapSettings { get; set; }
    string CurrencyName { get; set; }
    DateTime StartDate { get; set; }
    int SecondsInHour { get; set; }
    object Item { get; set; }
    string DisplayName { get; set; }
    string Description { get; set; }

    ClimateModel ClimateModel { get; set; }

    int DisplayOrder { get; set; }
}