using Aaas.Domain.Events;

namespace Aaas.Application.Events;

/// <summary>
/// SEUL endroit qui compte les places occupées d'un événement (R7, ADR 0005, 0012 § 4).
/// Étape 2 : la table Reservations n'existe pas, aucune place ne peut être occupée → toujours zéro.
/// </summary>
public sealed class EventOccupancyReader
{
    // Étape 4 : compter les réservations actives (ADR 0005, ReservationQueries.IsActive(now)) :
    // Confirmed + Pending non échues, groupées par événement, en une requête (pas de N+1, ADR 0011).
    // Injecter alors IAppDbContext ; les tests différés sont listés dans plan-etape-2.md § 9.4.
#pragma warning disable CA1822 // deviendra une méthode d'instance à l'étape 4 (requête sur IAppDbContext)
    public Task<EventOccupancy> GetAsync(Guid eventId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(EventOccupancy.None);

    /// <summary>
    /// Contrat : un identifiant sans place occupée peut être absent du dictionnaire (à l'étape 4, un GROUP BY ne
    /// renvoie pas les événements sans réservation). Les appelants lisent donc avec
    /// <c>GetValueOrDefault(id, EventOccupancy.None)</c>, jamais avec l'indexeur.
    /// </summary>
    public Task<IReadOnlyDictionary<Guid, EventOccupancy>> GetManyAsync(
        IReadOnlyCollection<Guid> eventIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        IReadOnlyDictionary<Guid, EventOccupancy> none = eventIds.Distinct().ToDictionary(id => id, _ => EventOccupancy.None);
        return Task.FromResult(none);
    }
#pragma warning restore CA1822
}
