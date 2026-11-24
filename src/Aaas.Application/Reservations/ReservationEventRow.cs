using System.Linq.Expressions;
using Aaas.Domain.Events;

namespace Aaas.Application.Reservations;

/// <summary>
/// Colonnes de l'événement utiles aux DTO de réservation (sans la description, jusqu'à 4 000 caractères).
/// Partagé par les services membre et admin. <see cref="Projection"/> sert dans un <c>Select</c> direct sur
/// <c>Events</c> ; dans un <c>Join</c> suivi d'un type anonyme, EF ne sait pas composer une expression stockée :
/// le constructeur y est recopié à la main (garder les deux listes de colonnes alignées).
/// </summary>
internal sealed record ReservationEventRow(
    Guid Id, string Title, string Location, DateTimeOffset StartsAt, DateTimeOffset EndsAt, decimal? Price, EventStatus Status)
{
    public static readonly Expression<Func<Event, ReservationEventRow>> Projection = e =>
        new ReservationEventRow(e.Id, e.Title, e.Location, e.StartsAt, e.EndsAt, e.Price, e.Status);

    public static ReservationEventRow From(Event e) => new(e.Id, e.Title, e.Location, e.StartsAt, e.EndsAt, e.Price, e.Status);
}
