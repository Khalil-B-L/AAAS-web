using Aaas.Application.Abstractions;
using Aaas.Application.Reservations;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Application.Events;

/// <summary>
/// SEUL endroit qui compte les places occupées d'un événement (R7, ADR 0005, 0012 § 4) :
/// demandes En attente non échues + réservations Confirmées (<see cref="ReservationQueries.IsActive"/>).
/// Appelé sous le verrou de l'événement, il partage la connexion et la transaction du DbContext (scoped) :
/// sous RCSI, la lecture voit la dernière version VALIDÉE au début de l'instruction, donc l'insertion
/// de la demande précédente sur le même événement (ADR 0019 § 2).
/// </summary>
public sealed class EventOccupancyReader(IAppDbContext db)
{
    public async Task<EventOccupancy> GetAsync(Guid eventId, DateTimeOffset now, CancellationToken cancellationToken) =>
        (await GetManyAsync([eventId], now, cancellationToken)).GetValueOrDefault(eventId, EventOccupancy.None);

    /// <summary>
    /// UNE requête pour toute une page (pas de N+1, ADR 0011) : GROUP BY EventId.
    /// Contrat : un événement sans place occupée est ABSENT du dictionnaire ; les appelants lisent avec
    /// <c>GetValueOrDefault(id, EventOccupancy.None)</c>, jamais avec l'indexeur.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, EventOccupancy>> GetManyAsync(
        IReadOnlyCollection<Guid> eventIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        if (eventIds.Count == 0)
        {
            return new Dictionary<Guid, EventOccupancy>();
        }

        var ids = eventIds.Distinct().ToList();
        var rows = await db.Reservations.AsNoTracking()
            .Where(r => ids.Contains(r.EventId))
            .Where(ReservationQueries.IsActive(now))
            .GroupBy(r => r.EventId)
            .Select(g => new
            {
                EventId = g.Key,
                Pending = g.Count(r => r.Status == ReservationStatus.Pending),
                Confirmed = g.Count(r => r.Status == ReservationStatus.Confirmed),
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.EventId, r => new EventOccupancy(r.Pending, r.Confirmed));
    }
}
