using Aaas.Domain.Common;
using Aaas.Domain.Events;

namespace Aaas.Domain.Reservations;

/// <summary>
/// Demande de place d'un membre pour un événement (US-D1 à D5, ADR 0018). Une ligne = une demande = une place.
/// Plusieurs lignes possibles par (membre, événement), une seule active (RG-03, index unique filtré).
/// Chaque règle reçoit <c>now</c> (et l'événement quand elle en dépend) : aucune ne lit l'horloge ni la base.
/// « Échue » = En attente et <c>now &gt;= ExpiresAt</c> (ADR 0005) : elle est présentée Expirée avant même d'être écrite.
/// </summary>
public sealed class Reservation
{
    private Reservation()
    {
        // Pour EF Core.
    }

    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    /// <summary>Null après la suppression du compte (ADR 0007) ; l'historique et le décompte restent justes.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Statut STOCKÉ ; à exposer via <see cref="EffectiveStatus"/>, jamais tel quel.</summary>
    public ReservationStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Échéance d'une demande en attente ; conservée après décision (historique).</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? RefusalReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Mention « Payée » (RG-08), pas un statut.</summary>
    public bool IsPaid { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public Guid? PaidByAdminId { get; private set; }

    /// <summary>rowversion : arrête une transition concurrente (ADR 0006, 0019 § 5).</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Nouvelle demande En attente. Ordre des refus (ADR 0018 § 3) : annulé, clos, déjà inscrit, complet —
    /// un membre déjà inscrit à un événement complet apprend qu'il est inscrit, pas que c'est complet.
    /// </summary>
    /// <param name="activeReservationId">Demande active (En attente non échue ou Confirmée) du membre pour cet événement, ou null.</param>
    /// <param name="occupancy">Places occupées, lues SOUS le verrou de l'événement (ADR 0019 § 2).</param>
    public static Reservation Request(
        Event ev,
        Guid userId,
        Guid? activeReservationId,
        EventOccupancy occupancy,
        DateTimeOffset now,
        TimeSpan pendingLifetime)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ev.EnsureOpenForRequests(now);

        if (activeReservationId is { } existing)
        {
            throw new BusinessRuleException(
                ErrorCodes.AlreadyReserved,
                ReservationRules.AlreadyReservedMessage,
                new Dictionary<string, object?> { [ErrorDetailKeys.ReservationId] = existing });
        }

        ev.EnsureCanAcceptRequest(now, occupancy);

        var requestedAt = now.ToUniversalTime();
        return new Reservation
        {
            EventId = ev.Id,
            UserId = userId,
            Status = ReservationStatus.Pending,
            RequestedAt = requestedAt,
            ExpiresAt = ReservationRules.ComputeExpiresAt(requestedAt, pendingLifetime, ev.StartsAt),
        };
    }

    /// <summary>En attente et échéance atteinte (l'instant de l'échéance est déjà échu).</summary>
    public bool IsDue(DateTimeOffset now) => Status == ReservationStatus.Pending && now >= ExpiresAt;

    /// <summary>Occupe une place : Confirmée, ou En attente non échue. Traduction SQL : ReservationQueries.IsActive (même règle).</summary>
    public bool IsActive(DateTimeOffset now) =>
        Status == ReservationStatus.Confirmed || (Status == ReservationStatus.Pending && now < ExpiresAt);

    /// <summary>Statut à présenter : Expirée pour une demande échue pas encore écrite (ADR 0005 § 3).</summary>
    public ReservationStatus EffectiveStatus(DateTimeOffset now) => IsDue(now) ? ReservationStatus.Expired : Status;

    /// <summary>
    /// Pour le bouton « Annuler » (canCancel des DTO) : même règle que <see cref="CancelByUser"/>, sans exception.
    /// Reçoit le début de l'événement plutôt que l'entité : les listes ne chargent que quelques colonnes.
    /// </summary>
    public bool CanBeCancelledByUser(DateTimeOffset eventStartsAt, DateTimeOffset now, TimeSpan confirmedCancellationDeadline) =>
        EffectiveStatus(now) switch
        {
            ReservationStatus.Pending => true,
            ReservationStatus.Confirmed => now <= ReservationRules.ConfirmedCancellationLimit(eventStartsAt, confirmedCancellationDeadline),
            _ => false,
        };

    /// <summary>
    /// Dernier instant où le membre peut annuler lui-même : échéance (exclue) pour une demande en attente non échue,
    /// début − 24 h (inclus) pour une confirmée, null sinon. Pour l'affichage seulement ; la règle est dans <see cref="CancelByUser"/>.
    /// </summary>
    public DateTimeOffset? CancellableUntil(DateTimeOffset eventStartsAt, DateTimeOffset now, TimeSpan confirmedCancellationDeadline) =>
        EffectiveStatus(now) switch
        {
            ReservationStatus.Pending => ExpiresAt,
            ReservationStatus.Confirmed => ReservationRules.ConfirmedCancellationLimit(eventStartsAt, confirmedCancellationDeadline),
            _ => null,
        };

    /// <summary>
    /// US-D3 : En attente non échue → Annulée par le membre, à tout moment ; Confirmée → idem jusqu'à début − 24 h
    /// inclus (RG-05), sinon cancellation_deadline_passed (le front affiche le numéro à contacter, R8).
    /// La mention « Payée » est conservée : le remboursement éventuel se règle avec l'association.
    /// </summary>
    public void CancelByUser(DateTimeOffset eventStartsAt, DateTimeOffset now, TimeSpan confirmedCancellationDeadline)
    {
        var current = EffectiveStatus(now);
        if (current == ReservationStatus.Confirmed
            && now > ReservationRules.ConfirmedCancellationLimit(eventStartsAt, confirmedCancellationDeadline))
        {
            throw new BusinessRuleException(ErrorCodes.CancellationDeadlinePassed, ReservationRules.CancellationDeadlinePassedMessage);
        }

        if (current is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
        {
            throw ReservationRules.StateChanged(current, forMember: true);
        }

        Status = ReservationStatus.CancelledByUser;
        CancelledAt = now.ToUniversalTime();
    }

    /// <summary>
    /// US-E6 : En attente non échue → Confirmée. Une demande échue ne se confirme plus (ADR 0005) : le membre doit
    /// refaire une demande. L'échéance ne dépassant jamais le début, « non échue » implique « événement pas commencé ».
    /// </summary>
    public void Confirm(DateTimeOffset now)
    {
        EnsurePendingForAdmin(now);
        Status = ReservationStatus.Confirmed;
        DecidedAt = now.ToUniversalTime();
    }

    /// <summary>
    /// US-E6 : En attente non échue → Refusée, motif facultatif (≤ 500, vide → null) visible du membre.
    /// L'admin qui refuse n'est pas enregistré (P-R6, minimisation).
    /// </summary>
    public void Refuse(string? reason, DateTimeOffset now)
    {
        var normalized = ReservationRules.NormalizeRefusalReason(reason);
        if (normalized is { Length: > ReservationRules.RefusalReasonMaxLength })
        {
            // Filet inatteignable par l'API (Application renvoie d'abord un 400 sur « reason ») : une
            // BusinessRuleException deviendrait un 409 trompeur, une erreur de programmation donne un 500 journalisé.
            throw new ArgumentException(ReservationRules.RefusalReasonTooLongMessage, nameof(reason));
        }

        EnsurePendingForAdmin(now);
        Status = ReservationStatus.Refused;
        DecidedAt = now.ToUniversalTime();
        RefusalReason = normalized;
    }

    /// <summary>
    /// US-E5 : En attente non échue ou Confirmée → Annulée par l'association, sans délai, tant que l'événement n'est
    /// pas terminé (event_ended ensuite). La mention « Payée » est conservée (remboursement hors application).
    /// Servira aussi à l'annulation d'un événement (bloc 4).
    /// </summary>
    public void CancelByAssociation(DateTimeOffset eventEndsAt, DateTimeOffset now)
    {
        var current = EffectiveStatus(now);
        if (current is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
        {
            throw ReservationRules.StateChanged(current, forMember: false);
        }

        if (now >= eventEndsAt)
        {
            throw new BusinessRuleException(ErrorCodes.EventEnded, ReservationRules.EventEndedForCancellationMessage);
        }

        Status = ReservationStatus.CancelledByAssociation;
        CancelledAt = now.ToUniversalTime();
    }

    /// <summary>
    /// US-E7, RG-08 : mention « Payée » sur une Confirmée d'un événement payant, sans limite de date (QF-4).
    /// Ordre : statut final (reservation_state_changed, la ligne de l'admin est périmée), gratuit
    /// (payment_not_applicable), en attente (reservation_not_confirmed). Déjà payée : sans effet (double clic),
    /// la date et l'admin d'origine sont conservés. Renvoie vrai si la mention a été posée.
    /// </summary>
    public bool MarkPaid(bool eventIsFree, Guid adminId, DateTimeOffset now)
    {
        var current = EffectiveStatus(now);
        if (current is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
        {
            throw ReservationRules.StateChanged(current, forMember: false);
        }

        if (eventIsFree)
        {
            throw new BusinessRuleException(ErrorCodes.PaymentNotApplicable, ReservationRules.PaymentNotApplicableMessage);
        }

        if (current == ReservationStatus.Pending)
        {
            throw new BusinessRuleException(
                ErrorCodes.ReservationNotConfirmed,
                ReservationRules.NotConfirmedMessage,
                new Dictionary<string, object?> { [ErrorDetailKeys.CurrentStatus] = current.ToString() });
        }

        if (IsPaid)
        {
            return false;
        }

        IsPaid = true;
        PaidAt = now.ToUniversalTime();
        PaidByAdminId = adminId;
        return true;
    }

    /// <summary>US-E7 : retire la mention quel que soit le statut, y compris annulée (QF-5) ; sans effet si non payée.</summary>
    public bool UnmarkPaid()
    {
        if (!IsPaid)
        {
            return false;
        }

        IsPaid = false;
        PaidAt = null;
        PaidByAdminId = null;
        return true;
    }

    /// <summary>
    /// Boutons proposés à l'admin (actions des DTO) : mêmes règles que les méthodes ci-dessus, sans exception.
    /// Le serveur revérifie toujours : un bouton affiché n'est pas une autorisation.
    /// </summary>
    public ReservationActions AdminActions(DateTimeOffset eventEndsAt, bool eventIsFree, DateTimeOffset now)
    {
        var current = EffectiveStatus(now);
        var pending = current == ReservationStatus.Pending;
        var confirmed = current == ReservationStatus.Confirmed;
        return new ReservationActions(
            CanConfirm: pending,
            CanRefuse: pending,
            CanCancel: (pending || confirmed) && now < eventEndsAt,
            CanMarkPaid: confirmed && !eventIsFree && !IsPaid,
            CanUnmarkPaid: IsPaid);
    }

    private void EnsurePendingForAdmin(DateTimeOffset now)
    {
        var current = EffectiveStatus(now);
        if (current != ReservationStatus.Pending)
        {
            throw ReservationRules.StateChanged(current, forMember: false);
        }
    }

    /// <summary>Écrit Expirée sur une demande échue ; sans effet sinon. Renvoie vrai si le statut a changé.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (!IsDue(now))
        {
            return false;
        }

        Status = ReservationStatus.Expired;
        return true;
    }

    /// <summary>
    /// ADR 0018 § 5 : le début de l'événement a changé. Une demande en attente NON échue reprend l'échéance
    /// min(demande + 48 h, nouveau début) : ramenée si le début avance, allongée (plafonnée) s'il recule.
    /// Une demande échue n'est jamais ranimée ; les autres statuts ne sont pas touchés.
    /// </summary>
    public bool RescheduleDeadline(DateTimeOffset newStartsAt, TimeSpan pendingLifetime, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Pending || IsDue(now))
        {
            return false;
        }

        var recomputed = ReservationRules.ComputeExpiresAt(RequestedAt, pendingLifetime, newStartsAt);
        if (recomputed == ExpiresAt)
        {
            return false;
        }

        ExpiresAt = recomputed;
        return true;
    }
}
