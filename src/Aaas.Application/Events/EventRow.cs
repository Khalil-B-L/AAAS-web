using System.Linq.Expressions;
using Aaas.Domain.Events;

namespace Aaas.Application.Events;

/// <summary>
/// Projection des colonnes utiles aux listes (sans la description, jusqu'à 4 000 caractères) : la requête SQL
/// ne lit que ces colonnes (ADR 0011). Les calculs R2/R7 viennent ensuite du Domain (EventRules).
/// </summary>
internal sealed record EventRow(
    Guid Id,
    string Title,
    string Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal? Price,
    int Capacity,
    EventStatus Status,
    DateTimeOffset? FirstPublishedAt)
{
    public static readonly Expression<Func<Event, EventRow>> Projection = e =>
        new EventRow(e.Id, e.Title, e.Location, e.StartsAt, e.EndsAt, e.Price, e.Capacity, e.Status, e.FirstPublishedAt);
}
