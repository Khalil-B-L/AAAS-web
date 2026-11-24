using System.Linq.Expressions;
using Aaas.Domain.Events;

namespace Aaas.Application.Events;

/// <summary>
/// Filtres et tris réutilisables, traduits en SQL par EF. Changer la définition de « listé publiquement »
/// (RG-14) ne touche que <see cref="IsListedPublicly"/>.
/// </summary>
internal static class EventQueries
{
    /// <summary>Détail public : publié ou annulé, même terminé (RG-14). Jamais un brouillon.</summary>
    public static readonly Expression<Func<Event, bool>> IsVisiblePublicly =
        e => e.Status == EventStatus.Published || e.Status == EventStatus.Cancelled;

    /// <summary>Liste publique : publié ou annulé, non terminé (un événement en cours reste listé, RG-14).</summary>
    public static Expression<Func<Event, bool>> IsListedPublicly(DateTimeOffset now) =>
        e => (e.Status == EventStatus.Published || e.Status == EventStatus.Cancelled) && e.EndsAt > now;

    public static IQueryable<Event> InPeriod(this IQueryable<Event> events, EventPeriod period, DateTimeOffset now) => period switch
    {
        EventPeriod.Upcoming => events.Where(e => e.EndsAt > now),
        EventPeriod.Past => events.Where(e => e.EndsAt <= now),
        _ => events,
    };

    /// <summary>Tri imposé, complété par Id pour un ordre stable d'une page à l'autre (ADR 0011).</summary>
    public static IQueryable<Event> OrderFor(this IQueryable<Event> events, EventPeriod period) => period == EventPeriod.Upcoming
        ? events.OrderBy(e => e.StartsAt).ThenBy(e => e.Id)
        : events.OrderByDescending(e => e.StartsAt).ThenBy(e => e.Id);

    // Page : Common/QueryablePaging.cs (générique).
}
