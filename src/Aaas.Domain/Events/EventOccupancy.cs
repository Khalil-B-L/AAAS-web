namespace Aaas.Domain.Events;

/// <summary>
/// Places occupées d'un événement : demandes en attente non échues + réservations confirmées (R7, ADR 0005).
/// Calculée par Application (EventOccupancyReader) et passée au Domain, qui ne fait aucune requête (ADR 0012).
/// </summary>
public readonly record struct EventOccupancy(int Pending, int Confirmed)
{
    public static EventOccupancy None => new(0, 0);

    public int Active => Pending + Confirmed;
}
