namespace Ambient.Domain;

/// <summary>
/// Extensions for SagaArc (generated from XSD).
/// </summary>
public partial class SagaArc
{
    /// <summary>
    /// The Y coordinate at which this SagaArc's structure has been sited.
    /// A value of int.MinValue indicates the structure has not yet been sited.
    /// Once set, this ensures consistent vertical placement across multiple hubs.
    /// </summary>
    public int SitedY { get; set; } = int.MinValue;

    /// <summary>
    /// Returns true if this SagaArc's structure has been vertically sited.
    /// </summary>
    public bool IsSited => SitedY != int.MinValue;
}
