using System.Globalization;
using Aaas.Application.Abstractions;
using Aaas.Application.Accounts;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Application.Events;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Application.Reservations;

/// <summary>
/// Traitement des réservations par un administrateur (US-E4 à E7, ADR 0006, 0019 § 1 et § 5, 0021).
/// Aucune de ces opérations ne prend le verrou de l'événement : confirmer ne change pas le nombre de places prises,
/// refuser et annuler en libèrent, « Payée » n'y touche pas. Une écriture concurrente (autre admin, membre,
/// expiration) change la rowversion : 409 reservation_state_changed avec le statut effectif relu.
/// Coordonnées des membres : UNE requête par écran (IIdentityService.GetContactsAsync), jamais journalisées.
/// </summary>
public sealed class AdminReservationService(IAppDbContext db, IIdentityService identity, TimeProvider time)
{
    public const string NotFoundMessage = PublicEventService.NotFoundMessage;

    // Tri « feuille d'émargement » : insensible à la casse ; sans ICU (mode invariant), repli ordinal sans erreur.
    private static readonly StringComparer NameComparer = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);

    /// <summary>US-E6 : demandes En attente non échues de tous les événements, échéance la plus proche en tête.</summary>
    public async Task<PagedResult<AdminReservationDto>> ListPendingAsync(PendingReservationListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = time.GetUtcNow();
        var pending = db.Reservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Pending && r.ExpiresAt > now);

        var total = await pending.CountAsync(cancellationToken);
        var rows = await pending
            .Join(db.Events, r => r.EventId, e => e.Id, (r, e) => new { Reservation = r, Event = e })
            .OrderBy(x => x.Reservation.ExpiresAt)
            .ThenBy(x => x.Reservation.RequestedAt)
            .ThenBy(x => x.Reservation.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new
            {
                x.Reservation,
                // Recopie de ReservationEventRow.Projection : EF ne compose pas une expression stockée dans ce Join.
                Event = new ReservationEventRow(
                    x.Event.Id, x.Event.Title, x.Event.Location, x.Event.StartsAt, x.Event.EndsAt, x.Event.Price, x.Event.Status),
            })
            .ToListAsync(cancellationToken);

        var contacts = await ContactsAsync(rows.Select(r => r.Reservation), cancellationToken);
        var items = rows.Select(r => ToDto(r.Reservation, r.Event, contacts, now)).ToList();
        return new PagedResult<AdminReservationDto>(items, query.Page, query.PageSize, total);
    }

    /// <summary>US-E4 : inscrits d'un événement (brouillon compris), non paginé (P-R7), trois groupes triés.</summary>
    public async Task<EventRegistrantsDto> GetEventRegistrantsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var ev = await db.Events.AsNoTracking()
            .Where(e => e.Id == eventId)
            .Select(e => new
            {
                // Recopie de ReservationEventRow.Projection (même raison : type anonyme englobant).
                Row = new ReservationEventRow(e.Id, e.Title, e.Location, e.StartsAt, e.EndsAt, e.Price, e.Status),
                e.Capacity,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(AdminEventService.NotFoundMessage);

        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => r.EventId == eventId)
            .ToListAsync(cancellationToken);
        var contacts = await ContactsAsync(reservations, cancellationToken);

        // Statut effectif (Domain) : une demande échue non encore écrite est rangée dans « autres » et ne compte pas.
        var dtos = reservations
            .Select(r => (Reservation: r, Dto: ToDto(r, ev.Row, contacts, now)))
            .ToList();

        var pending = dtos.Where(x => x.Dto.Status == ReservationStatus.Pending)
            .OrderBy(x => x.Reservation.RequestedAt).ThenBy(x => x.Reservation.Id)
            .Select(x => x.Dto).ToList();
        var confirmed = dtos.Where(x => x.Dto.Status == ReservationStatus.Confirmed)
            .OrderBy(x => x.Dto.Member is null) // comptes supprimés à la fin
            .ThenBy(x => x.Dto.Member?.LastName, NameComparer)
            .ThenBy(x => x.Dto.Member?.FirstName, NameComparer)
            .ThenBy(x => x.Reservation.RequestedAt)
            .Select(x => x.Dto).ToList();
        var others = dtos.Where(x => x.Dto.Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
            .OrderByDescending(x => x.Reservation.RequestedAt).ThenBy(x => x.Reservation.Id)
            .Select(x => x.Dto).ToList();

        var occupancy = new EventOccupancy(pending.Count, confirmed.Count);
        return new EventRegistrantsDto(
            new RegistrantsEventDto(
                ev.Row.Id, ev.Row.Title, ev.Row.StartsAt, ev.Row.EndsAt, ev.Capacity, ev.Row.Price, ev.Row.Status,
                ev.Row.StartsAt <= now, ev.Row.EndsAt <= now),
            new RegistrantsOccupancyDto(
                occupancy.Pending, occupancy.Confirmed, occupancy.Active, EventRules.RemainingSeats(ev.Capacity, occupancy)),
            pending,
            confirmed,
            others);
    }

    public Task<AdminReservationDto> ConfirmAsync(Guid reservationId, CancellationToken cancellationToken) =>
        TransitionAsync(reservationId, (r, _, now) => r.Confirm(now), cancellationToken);

    public Task<AdminReservationDto> RefuseAsync(Guid reservationId, RefuseReservationRequest? request, CancellationToken cancellationToken)
    {
        var reason = ReservationRules.NormalizeRefusalReason(request?.Reason);
        if (reason is { Length: > ReservationRules.RefusalReasonMaxLength })
        {
            throw RequestValidationException.For("reason", ReservationRules.RefusalReasonTooLongMessage);
        }

        return TransitionAsync(reservationId, (r, _, now) => r.Refuse(reason, now), cancellationToken);
    }

    public Task<AdminReservationDto> CancelAsync(Guid reservationId, CancellationToken cancellationToken) =>
        TransitionAsync(reservationId, (r, ev, now) => r.CancelByAssociation(ev.EndsAt, now), cancellationToken);

    public Task<AdminReservationDto> MarkPaidAsync(Guid adminId, Guid reservationId, CancellationToken cancellationToken) =>
        TransitionAsync(reservationId, (r, ev, now) => r.MarkPaid(ev.Price is null, adminId, now), cancellationToken);

    // Retirer « Payée » est permis quel que soit le statut : un changement de statut concurrent (annulation par le
    // membre, expiration) ne rend pas l'action caduque, on la réapplique sur la ligne relue.
    public Task<AdminReservationDto> UnmarkPaidAsync(Guid reservationId, CancellationToken cancellationToken) =>
        TransitionAsync(reservationId, (r, _, _) => r.UnmarkPaid(), cancellationToken, reapplyIfStatusChanged: true);

    /// <summary>
    /// Charge (suivie), applique la règle du Domain, enregistre. La rowversion est dans le WHERE de l'UPDATE : si une
    /// autre écriture est passée entre la lecture et l'enregistrement, 0 ligne → DbUpdateConcurrencyException.
    /// Statut effectif changé entre-temps (autre admin, membre, expiration) → 409 reservation_state_changed avec le
    /// statut relu, sauf si <paramref name="reapplyIfStatusChanged"/> (action valable dans tous les statuts). Statut
    /// inchangé (échéance recalculée par un déplacement de date, « Payée » posée par un autre admin) → la règle est
    /// réappliquée UNE fois sur la ligne relue (même comportement que l'annulation par le membre). Un second conflit
    /// → 409 concurrent_update : la ligne bouge sans cesse, rien ne dit que la demande a été traitée.
    /// Une action sans effet (double « Payée ») n'écrit rien et renvoie l'état actuel.
    /// </summary>
    private async Task<AdminReservationDto> TransitionAsync(
        Guid reservationId,
        Action<Reservation, ReservationEventRow, DateTimeOffset> apply,
        CancellationToken cancellationToken,
        bool reapplyIfStatusChanged = false)
    {
        var now = time.GetUtcNow();
        for (var attempt = 1; ; attempt++)
        {
            var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == reservationId, cancellationToken)
                ?? throw new NotFoundException(NotFoundMessage);
            var ev = await db.Events.AsNoTracking()
                .Where(e => e.Id == reservation.EventId)
                .Select(ReservationEventRow.Projection)
                .SingleAsync(cancellationToken);

            var seen = reservation.EffectiveStatus(now);
            apply(reservation, ev, now);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                var contacts = await ContactsAsync([reservation], cancellationToken);
                return ToDto(reservation, ev, contacts, now);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Pas de journalisation : conflit attendu.
                var current = await EffectiveStatusInDatabaseAsync(reservationId, now, cancellationToken);
                if (current != seen && !reapplyIfStatusChanged)
                {
                    throw ReservationRules.StateChanged(current, forMember: false);
                }

                if (attempt > 1)
                {
                    // Second conflit sans changement de statut exploitable : « déjà traitée » serait faux.
                    throw ConcurrencyGuard.Conflict();
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

    /// <summary>Membres et admins « Payée » de toutes les lignes en un seul appel (une requête SQL).</summary>
    private Task<IReadOnlyDictionary<Guid, MemberContact>> ContactsAsync(IEnumerable<Reservation> reservations, CancellationToken cancellationToken)
    {
        var ids = reservations
            .SelectMany(r => new[] { r.UserId, r.PaidByAdminId })
            .OfType<Guid>()
            .Distinct()
            .ToList();
        return identity.GetContactsAsync(ids, cancellationToken);
    }

    private static AdminReservationDto ToDto(
        Reservation r, ReservationEventRow ev, IReadOnlyDictionary<Guid, MemberContact> contacts, DateTimeOffset now)
    {
        var status = r.EffectiveStatus(now);
        var member = r.UserId is { } userId && contacts.TryGetValue(userId, out var m)
            ? new ReservationMemberDto(m.Id, m.FirstName, m.LastName, m.Email, m.PhoneNumber)
            : null;
        var paidBy = r.PaidByAdminId is { } adminId && contacts.TryGetValue(adminId, out var a)
            ? new ReservationPaidByDto(a.FirstName, a.LastName)
            : null;
        var actions = r.AdminActions(ev.EndsAt, ev.Price is null, now);

        return new AdminReservationDto(
            r.Id,
            status,
            r.RequestedAt,
            status == ReservationStatus.Pending ? r.ExpiresAt : null,
            r.DecidedAt,
            r.RefusalReason,
            r.CancelledAt,
            r.IsPaid,
            r.PaidAt,
            paidBy,
            member,
            new AdminReservationEventDto(ev.Id, ev.Title, ev.StartsAt, ev.EndsAt, ev.Price, ev.Status),
            new AdminReservationActionsDto(actions.CanConfirm, actions.CanRefuse, actions.CanCancel, actions.CanMarkPaid, actions.CanUnmarkPaid));
    }
}
