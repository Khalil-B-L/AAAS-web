using Aaas.Application.Abstractions;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Application.Events;

/// <summary>Consultation publique, sans compte (US-C1, US-C2, RG-14).</summary>
public sealed class PublicEventService(IAppDbContext db, EventOccupancyReader occupancyReader, TimeProvider time)
{
    // Même message pour un brouillon et un identifiant inconnu : on ne révèle pas l'existence d'un brouillon.
    internal const string NotFoundMessage = "Contenu introuvable.";

    /// <summary>Publiés ou annulés, non terminés, du plus proche au plus lointain (RG-14, ADR 0011).</summary>
    public async Task<PagedResult<EventSummaryDto>> ListAsync(PublicEventListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = time.GetUtcNow();
        var listed = db.Events.AsNoTracking().Where(EventQueries.IsListedPublicly(now));

        var total = await listed.CountAsync(cancellationToken);
        var rows = await listed
            .OrderFor(EventPeriod.Upcoming)
            .Page(query.Page, query.PageSize)
            .Select(EventRow.Projection)
            .ToListAsync(cancellationToken);
        var occupancies = await occupancyReader.GetManyAsync([.. rows.Select(r => r.Id)], now, cancellationToken);

        var items = rows.Select(r =>
        {
            var occupancy = occupancies.GetValueOrDefault(r.Id, EventOccupancy.None);
            return new EventSummaryDto(
                r.Id, r.Title, r.Location, r.StartsAt, r.EndsAt, r.Price, r.Capacity,
                EventRules.RemainingSeats(r.Capacity, occupancy), r.Status,
                r.StartsAt <= now, r.EndsAt <= now,
                EventRules.IsFull(r.Capacity, occupancy),
                EventRules.IsBookable(r.Status, r.StartsAt, r.Capacity, now, occupancy));
        }).ToList();

        return new PagedResult<EventSummaryDto>(items, query.Page, query.PageSize, total);
    }

    /// <summary>Publié ou annulé, même terminé (historique, RG-14) ; brouillon ou inconnu → 404.</summary>
    public async Task<EventDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var ev = await db.Events.AsNoTracking()
            .Where(EventQueries.IsVisiblePublicly)
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException(NotFoundMessage);
        var occupancy = await occupancyReader.GetAsync(id, now, cancellationToken);

        return new EventDetailDto(
            ev.Id, ev.Title, ev.Description, ev.Location, ev.StartsAt, ev.EndsAt, ev.Price, ev.Capacity,
            ev.RemainingSeats(occupancy), ev.Status, ev.HasStarted(now), ev.HasEnded(now),
            ev.IsFull(occupancy), ev.IsBookable(now, occupancy));
    }
}
