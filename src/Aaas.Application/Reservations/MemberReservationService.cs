using Aaas.Application.Abstractions;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Application.Events;
using Aaas.Application.Options;
using Aaas.Domain.Common;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aaas.Application.Reservations;

/// <summary>
/// Réservations du membre connecté (US-D1 à D4, ADR 0018, 0019, 0021). <c>userId</c> vient toujours du jeton :
/// une réservation d'un autre membre est introuvable (404), jamais « interdite » (on ne révèle pas son existence).
/// </summary>
public sealed class MemberReservationService(
    IAppDbContext db,
    IEventLock eventLock,
    EventOccupancyReader occupancyReader,
    ReservationExpiryService expiry,
    IOptions<ReservationOptions> options,
    TimeProvider time)
{
    // Même message qu'un contenu introuvable du détail public : un brouillon et un identifiant inconnu sont indiscernables.
    public const string NotFoundMessage = PublicEventService.NotFoundMessage;

    private ReservationOptions Settings => options.Value;

    /// <summary>
    /// Demande de place sous verrou (ADR 0019 § 2). Ordre : verrou → now → brouillon (404) → matérialisation des
    /// échues de l'événement → occupation et demande active du membre → règles du Domain → insertion.
    /// </summary>
    public async Task<MyReservationDto> RequestAsync(Guid userId, CreateReservationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var eventId = request.EventId ?? throw RequestValidationException.For("eventId", "L'événement est obligatoire.");

        try
        {
            var (created, ev, now) = await eventLock.ExecuteWithLockedEventAsync(eventId, async (ev, ct) =>
            {
                // Après le verrou : une demande qui a attendu 2 s ne décide pas avec une heure périmée.
                var now = time.GetUtcNow();
                if (ev.Status == EventStatus.Draft)
                {
                    throw new NotFoundException(NotFoundMessage);
                }

                // Libère l'index unique filtré d'une demande échue du même membre (sinon : 2601 au lieu d'une nouvelle demande).
                await expiry.ExpireDueForEventAsync(ev.Id, now, ct);
                var occupancy = await occupancyReader.GetAsync(ev.Id, now, ct);
                var active = await ActiveReservationIdAsync(ev.Id, userId, now, ct);

                var reservation = Reservation.Request(ev, userId, active, occupancy, now, Settings.PendingLifetime);
                db.Reservations.Add(reservation);
                return (reservation, ev, now);
            }, cancellationToken);

            return ToDto(created, ReservationEventRow.From(ev), now);
        }
        catch (NotFoundException)
        {
            // IEventLock a son propre message pour un identifiant inconnu : on unifie avec celui du brouillon.
            throw new NotFoundException(NotFoundMessage);
        }
        catch (UniqueConstraintViolationException)
        {
            // Filet (ADR 0019 § 3) : impossible en temps normal, les demandes d'un événement étant sérialisées.
            var existing = await ActiveReservationIdAsync(eventId, userId, time.GetUtcNow(), cancellationToken);
            throw new BusinessRuleException(
                ErrorCodes.AlreadyReserved,
                ReservationRules.AlreadyReservedMessage,
                existing is { } id ? new Dictionary<string, object?> { [ErrorDetailKeys.ReservationId] = id } : null);
        }
    }

    /// <summary>US-D2 : deux sections, « À venir » et « Historique », paginées (ADR 0011, 0021 § 5).</summary>
    public async Task<PagedResult<MyReservationDto>> ListAsync(Guid userId, MyReservationListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = time.GetUtcNow();
        var current = ReservationQueries.IsCurrentForMember(now, db.Events);
        var filtered = db.Reservations.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Where(query.Scope == MyReservationScope.Current ? current : ReservationQueries.Not(current));

        var total = await filtered.CountAsync(cancellationToken);
        var joined = filtered.Join(db.Events, r => r.EventId, e => e.Id, (r, e) => new { Reservation = r, Event = e });
        var ordered = query.Scope == MyReservationScope.Current
            ? joined.OrderBy(x => x.Event.StartsAt).ThenBy(x => x.Reservation.Id)
            : joined.OrderByDescending(x => x.Event.StartsAt).ThenByDescending(x => x.Reservation.RequestedAt).ThenBy(x => x.Reservation.Id);
        var rows = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new
            {
                x.Reservation,
                Event = new ReservationEventRow(
                    x.Event.Id, x.Event.Title, x.Event.Location, x.Event.StartsAt, x.Event.EndsAt, x.Event.Price, x.Event.Status),
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => ToDto(r.Reservation, r.Event, now)).ToList();
        return new PagedResult<MyReservationDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<MyReservationDto> GetAsync(Guid userId, Guid reservationId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var row = await OwnedWithEvent(userId).Where(x => x.Reservation.Id == reservationId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(NotFoundMessage);
        return ToDto(row.Reservation, row.Event, now);
    }

    /// <summary>
    /// « Ma demande » sur le détail d'un événement (ADR 0021 § 1) : la plus récente, ou null (204).
    /// Événement inconnu ou brouillon → 404, comme le détail public.
    /// </summary>
    public async Task<MyReservationDto?> GetForEventAsync(Guid userId, Guid eventId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var visible = await db.Events.AsNoTracking()
            .Where(EventQueries.IsVisiblePublicly)
            .AnyAsync(e => e.Id == eventId, cancellationToken);
        if (!visible)
        {
            throw new NotFoundException(NotFoundMessage);
        }

        var latest = await OwnedWithEvent(userId)
            .Where(x => x.Reservation.EventId == eventId)
            .OrderByDescending(x => x.Reservation.RequestedAt)
            .ThenByDescending(x => x.Reservation.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return latest is null ? null : ToDto(latest.Reservation, latest.Event, now);
    }

    /// <summary>
    /// US-D3, sans verrou (ADR 0019 § 1 : annuler ne fait que libérer une place). Une écriture concurrente change la
    /// rowversion. Si elle a changé le statut (admin qui confirme, tâche d'expiration) : 409 reservation_state_changed
    /// avec le statut relu. Si elle n'a PAS changé le statut effectif (l'admin a déplacé la date : échéance recalculée,
    /// ADR 0018 § 5) : la règle est réappliquée UNE fois sur la ligne relue, sinon le membre lirait « déjà traitée »
    /// alors que sa demande est toujours en attente.
    /// </summary>
    public async Task<MyReservationDto> CancelAsync(Guid userId, Guid reservationId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        for (var attempt = 1; ; attempt++)
        {
            var reservation = await db.Reservations
                .SingleOrDefaultAsync(r => r.Id == reservationId && r.UserId == userId, cancellationToken)
                ?? throw new NotFoundException(NotFoundMessage);
            var ev = await db.Events.AsNoTracking().Where(e => e.Id == reservation.EventId).Select(ReservationEventRow.Projection)
                .SingleAsync(cancellationToken);

            var seen = reservation.EffectiveStatus(now);
            reservation.CancelByUser(ev.StartsAt, now, Settings.ConfirmedCancellationDeadline);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return ToDto(reservation, ev, now);
            }
            catch (DbUpdateConcurrencyException)
            {
                var current = await EffectiveStatusInDatabaseAsync(reservationId, now, cancellationToken);
                if (current != seen || attempt > 1)
                {
                    throw ReservationRules.StateChanged(current, forMember: true);
                }

                // Sans Clear, EF renverrait l'entité suivie (modifiée, ancienne rowversion) au lieu de la ligne relue.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<ReservationStatus> EffectiveStatusInDatabaseAsync(Guid reservationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reread = await db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => new { r.Status, r.ExpiresAt })
            .SingleAsync(cancellationToken);
        return reread.Status == ReservationStatus.Pending && now >= reread.ExpiresAt ? ReservationStatus.Expired : reread.Status;
    }

    private IQueryable<ReservationWithEvent> OwnedWithEvent(Guid userId) =>
        db.Reservations.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Join(db.Events, r => r.EventId, e => e.Id, (r, e) => new ReservationWithEvent
            {
                Reservation = r,
                Event = new ReservationEventRow(e.Id, e.Title, e.Location, e.StartsAt, e.EndsAt, e.Price, e.Status),
            });

    private Task<Guid?> ActiveReservationIdAsync(Guid eventId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Reservations.AsNoTracking()
            .Where(r => r.EventId == eventId && r.UserId == userId)
            .Where(ReservationQueries.IsActive(now))
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private MyReservationDto ToDto(Reservation r, ReservationEventRow ev, DateTimeOffset now)
    {
        var status = r.EffectiveStatus(now);
        var deadline = Settings.ConfirmedCancellationDeadline;
        return new MyReservationDto(
            r.Id,
            status,
            r.RequestedAt,
            status == ReservationStatus.Pending ? r.ExpiresAt : null,
            r.DecidedAt,
            r.RefusalReason,
            r.CancelledAt,
            r.IsPaid,
            r.PaidAt,
            r.CanBeCancelledByUser(ev.StartsAt, now, deadline),
            r.CancellableUntil(ev.StartsAt, now, deadline),
            new ReservationEventDto(
                ev.Id, ev.Title, ev.Location, ev.StartsAt, ev.EndsAt, ev.Price, ev.Status,
                ev.StartsAt <= now, ev.EndsAt <= now, ev.Status != EventStatus.Draft));
    }

    private sealed class ReservationWithEvent
    {
        public required Reservation Reservation { get; init; }

        public required ReservationEventRow Event { get; init; }
    }
}
