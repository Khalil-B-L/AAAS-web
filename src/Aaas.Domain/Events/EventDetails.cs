namespace Aaas.Domain.Events;

/// <summary>Données saisissables d'un événement, passées d'un bloc à la création et à la modification.</summary>
public sealed record EventDetails(
    string Title,
    string Description,
    string Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int Capacity,
    decimal? Price);
