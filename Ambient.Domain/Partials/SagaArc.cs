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

    /// <summary>
    /// True for arcs created server-side via <c>ServerSagaArcCreationService</c>
    /// (Market / GeoCache / RemnantLoot — world-shared, transactionally seeded). Set when
    /// the client registers the arc locally from a server DTO. Authored sagas (loaded from
    /// the world XML) leave this false. Lets the client decide multiplayer-vs-single-player
    /// instance routing and server-arc-vs-quest-character visual handling without relying
    /// on naming conventions in the SagaRef.
    /// </summary>
    public bool IsServerArc { get; set; }
}
