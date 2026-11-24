using System.Reflection;
using Aaas.Domain.Common;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;
using static Aaas.Domain.Tests.Events.EventTestData;

namespace Aaas.Domain.Tests.Reservations;

/// <summary>
/// Demande, statut effectif, annulation par le membre, échéance et son recalcul (RG-02 à RG-05, ADR 0005, 0018).
/// Instants fixes : Now = 01/12 10 h Tunis (09:00Z), début de l'événement = 05/12 10 h Tunis.
/// </summary>
public class ReservationTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);
    private static readonly TimeSpan CancelDeadline = TimeSpan.FromHours(24);
    private static readonly Guid Member = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static BusinessRuleException Rejected(Action action) => Assert.Throws<BusinessRuleException>(action);

    private static Reservation Pending(Event? ev = null, DateTimeOffset? at = null) =>
        Reservation.Request(ev ?? Published(), Member, null, EventOccupancy.None, at ?? Now, Lifetime);

    private static Reservation Confirmed(Event ev)
    {
        var reservation = Pending(ev);
        reservation.Confirm(Now.AddMinutes(1));
        return reservation;
    }

    /// <summary>L'annulation d'un événement arrive au bloc 4 : on force le statut pour tester l'ordre des refus.</summary>
    private static Event CancelledEvent()
    {
        var ev = Published();
        typeof(Event).GetProperty(nameof(Event.Status), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(ev, EventStatus.Cancelled);
        return ev;
    }

    // ---- Échéance (RG-04, US-D5) ----

    [Fact(DisplayName = "Échéance : demande le 10/03 10 h, début le 20/03 → 12/03 10 h (48 h)")]
    public void Deadline_is_48_hours_after_request()
    {
        var requested = new DateTimeOffset(2027, 3, 10, 10, 0, 0, TimeSpan.FromHours(1));
        var start = new DateTimeOffset(2027, 3, 20, 10, 0, 0, TimeSpan.FromHours(1));

        Assert.Equal(new DateTimeOffset(2027, 3, 12, 9, 0, 0, TimeSpan.Zero), ReservationRules.ComputeExpiresAt(requested, Lifetime, start));
    }

    [Fact(DisplayName = "Échéance : début le 11/03 9 h, plus tôt que 48 h → ramenée au début")]
    public void Deadline_is_capped_at_event_start()
    {
        var requested = new DateTimeOffset(2027, 3, 10, 10, 0, 0, TimeSpan.FromHours(1));
        var start = new DateTimeOffset(2027, 3, 11, 9, 0, 0, TimeSpan.FromHours(1));

        Assert.Equal(start, ReservationRules.ComputeExpiresAt(requested, Lifetime, start));
        Assert.Equal(TimeSpan.Zero, ReservationRules.ComputeExpiresAt(requested, Lifetime, start).Offset);
    }

    // ---- Demande (US-D1, RG-02, RG-03) ----

    [Fact(DisplayName = "Demande acceptée : En attente, dates en UTC, échéance = demande + 48 h")]
    public void Request_creates_pending_reservation()
    {
        var ev = Published();

        var reservation = Reservation.Request(ev, Member, null, new EventOccupancy(10, 9), Now, Lifetime);

        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Equal(ev.Id, reservation.EventId);
        Assert.Equal(Member, reservation.UserId);
        Assert.Equal(Now, reservation.RequestedAt);
        Assert.Equal(Now.AddHours(48), reservation.ExpiresAt);
        Assert.False(reservation.IsPaid);
        Assert.Null(reservation.DecidedAt);
        Assert.Null(reservation.CancelledAt);
    }

    [Fact(DisplayName = "19 places prises sur 20 → acceptée ; 20 sur 20 → event_full")]
    public void Request_on_last_seat_then_full()
    {
        var ev = Published(Details(capacity: 20));

        Reservation.Request(ev, Member, null, new EventOccupancy(4, 15), Now, Lifetime);
        var error = Rejected(() => Reservation.Request(ev, Member, null, new EventOccupancy(5, 15), Now, Lifetime));

        Assert.Equal(ErrorCodes.EventFull, error.Code);
        Assert.Equal(ReservationRules.EventFullMessage, error.Message);
    }

    [Fact(DisplayName = "Ordre des refus : annulé passe avant complet et déjà inscrit")]
    public void Cancelled_event_is_reported_first() =>
        Assert.Equal(ErrorCodes.EventCancelled, Rejected(() =>
            Reservation.Request(CancelledEvent(), Member, Guid.NewGuid(), new EventOccupancy(0, 20), Now, Lifetime)).Code);

    [Fact(DisplayName = "Ordre des refus : commencé (exactement au début) passe avant déjà inscrit et complet")]
    public void Started_event_is_closed_before_already_reserved()
    {
        var ev = Published();
        Assert.Equal(ErrorCodes.ReservationsClosed, Rejected(() =>
            Reservation.Request(ev, Member, Guid.NewGuid(), new EventOccupancy(0, 20), ev.StartsAt, Lifetime)).Code);
    }

    [Fact(DisplayName = "Ordre des refus : déjà inscrit passe avant complet, avec reservationId en détail")]
    public void Already_reserved_comes_before_full()
    {
        var existing = Guid.NewGuid();

        var error = Rejected(() => Reservation.Request(Published(), Member, existing, new EventOccupancy(0, 20), Now, Lifetime));

        Assert.Equal(ErrorCodes.AlreadyReserved, error.Code);
        Assert.Equal(existing, error.Details[ErrorDetailKeys.ReservationId]);
    }

    [Fact(DisplayName = "Brouillon : demande refusée (filet ; Application répond 404 avant)")]
    public void Draft_event_is_rejected() =>
        Assert.Equal(ErrorCodes.ReservationsClosed, Rejected(() =>
            Reservation.Request(Draft(), Member, null, EventOccupancy.None, Now, Lifetime)).Code);

    [Fact(DisplayName = "Événement terminé : reservations_closed")]
    public void Ended_event_is_closed()
    {
        var ev = Published();
        Assert.Equal(ErrorCodes.ReservationsClosed, Rejected(() =>
            Reservation.Request(ev, Member, null, EventOccupancy.None, ev.EndsAt.AddHours(1), Lifetime)).Code);
    }

    [Fact(DisplayName = "Demande 2 h avant le début : échéance = début (pas 48 h)")]
    public void Request_close_to_start_expires_at_start()
    {
        var ev = Published();

        var reservation = Pending(ev, ev.StartsAt.AddHours(-2));

        Assert.Equal(ev.StartsAt, reservation.ExpiresAt);
    }

    // ---- Statut effectif (ADR 0005) ----

    [Fact(DisplayName = "Statut effectif : En attente à échéance − 1 s ; Expirée à l'échéance exacte")]
    public void Effective_status_switches_exactly_at_deadline()
    {
        var reservation = Pending();

        Assert.Equal(ReservationStatus.Pending, reservation.EffectiveStatus(reservation.ExpiresAt.AddSeconds(-1)));
        Assert.True(reservation.IsActive(reservation.ExpiresAt.AddSeconds(-1)));
        Assert.False(reservation.IsDue(reservation.ExpiresAt.AddSeconds(-1)));

        Assert.Equal(ReservationStatus.Expired, reservation.EffectiveStatus(reservation.ExpiresAt));
        Assert.False(reservation.IsActive(reservation.ExpiresAt));
        Assert.True(reservation.IsDue(reservation.ExpiresAt));
        Assert.Equal(ReservationStatus.Pending, reservation.Status); // pas encore écrite
    }

    [Fact(DisplayName = "Confirmée : active quelle que soit l'heure (l'échéance ne la concerne plus)")]
    public void Confirmed_is_always_active()
    {
        var reservation = Confirmed(Published());
        Assert.True(reservation.IsActive(reservation.ExpiresAt.AddDays(3)));
        Assert.Equal(ReservationStatus.Confirmed, reservation.EffectiveStatus(reservation.ExpiresAt.AddDays(3)));
    }

    [Fact(DisplayName = "Expire : écrit Expirée sur une échue ; sans effet avant l'échéance ou sur une confirmée")]
    public void Expire_only_due_reservations()
    {
        var pending = Pending();
        Assert.False(pending.Expire(pending.ExpiresAt.AddSeconds(-1)));
        Assert.Equal(ReservationStatus.Pending, pending.Status);
        Assert.True(pending.Expire(pending.ExpiresAt));
        Assert.Equal(ReservationStatus.Expired, pending.Status);
        Assert.False(pending.Expire(pending.ExpiresAt.AddDays(1)));

        var confirmed = Confirmed(Published());
        Assert.False(confirmed.Expire(confirmed.ExpiresAt.AddDays(1)));
        Assert.Equal(ReservationStatus.Confirmed, confirmed.Status);
    }

    // ---- Confirmation (anticipée de la tâche B) ----

    [Fact(DisplayName = "Confirmer à échéance − 1 min → Confirmée ; à l'échéance → reservation_state_changed (Expired)")]
    public void Confirm_before_and_at_deadline()
    {
        var early = Pending();
        early.Confirm(early.ExpiresAt.AddMinutes(-1));
        Assert.Equal(ReservationStatus.Confirmed, early.Status);
        Assert.Equal(early.ExpiresAt.AddMinutes(-1), early.DecidedAt);

        var late = Pending();
        var error = Rejected(() => late.Confirm(late.ExpiresAt));
        Assert.Equal(ErrorCodes.ReservationStateChanged, error.Code);
        Assert.Equal("Expired", error.Details[ErrorDetailKeys.CurrentStatus]);
        Assert.Equal(ReservationRules.ExpiredForAdminMessage, error.Message);
    }

    // ---- Annulation par le membre (US-D3, RG-05) ----

    [Fact(DisplayName = "Annuler une demande en attente 5 h avant le début → Annulée par le membre")]
    public void Cancel_pending_shortly_before_start()
    {
        var ev = Published();
        var reservation = Pending(ev, ev.StartsAt.AddHours(-10));
        var at = ev.StartsAt.AddHours(-5);

        Assert.True(reservation.CanBeCancelledByUser(ev.StartsAt, at, CancelDeadline));
        reservation.CancelByUser(ev.StartsAt, at, CancelDeadline);

        Assert.Equal(ReservationStatus.CancelledByUser, reservation.Status);
        Assert.Equal(at, reservation.CancelledAt);
        Assert.False(reservation.IsActive(at));
    }

    [Fact(DisplayName = "Annuler une confirmée 3 jours avant → OK")]
    public void Cancel_confirmed_three_days_before()
    {
        var ev = Published();
        var reservation = Confirmed(ev);

        reservation.CancelByUser(ev.StartsAt, ev.StartsAt.AddDays(-3), CancelDeadline);

        Assert.Equal(ReservationStatus.CancelledByUser, reservation.Status);
    }

    [Fact(DisplayName = "Annuler une confirmée EXACTEMENT 24 h avant → OK (borne incluse)")]
    public void Cancel_confirmed_exactly_at_limit()
    {
        var ev = Published();
        var reservation = Confirmed(ev);
        var limit = ev.StartsAt.AddHours(-24);

        Assert.True(reservation.CanBeCancelledByUser(ev.StartsAt, limit, CancelDeadline));
        Assert.Equal(limit, reservation.CancellableUntil(ev.StartsAt, limit, CancelDeadline));
        reservation.CancelByUser(ev.StartsAt, limit, CancelDeadline);

        Assert.Equal(ReservationStatus.CancelledByUser, reservation.Status);
    }

    [Fact(DisplayName = "Annuler une confirmée 24 h − 1 s avant → cancellation_deadline_passed, statut inchangé")]
    public void Cancel_confirmed_after_limit_is_rejected()
    {
        var ev = Published();
        var reservation = Confirmed(ev);
        var tooLate = ev.StartsAt.AddHours(-24).AddSeconds(1);

        Assert.False(reservation.CanBeCancelledByUser(ev.StartsAt, tooLate, CancelDeadline));
        var error = Rejected(() => reservation.CancelByUser(ev.StartsAt, tooLate, CancelDeadline));

        Assert.Equal(ErrorCodes.CancellationDeadlinePassed, error.Code);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
    }

    [Fact(DisplayName = "Annuler une demande échue → reservation_state_changed, currentStatus Expired")]
    public void Cancel_due_pending_is_rejected()
    {
        var ev = Published();
        var reservation = Pending(ev);

        Assert.False(reservation.CanBeCancelledByUser(ev.StartsAt, reservation.ExpiresAt, CancelDeadline));
        var error = Rejected(() => reservation.CancelByUser(ev.StartsAt, reservation.ExpiresAt, CancelDeadline));

        Assert.Equal(ErrorCodes.ReservationStateChanged, error.Code);
        Assert.Equal("Expired", error.Details[ErrorDetailKeys.CurrentStatus]);
        Assert.Equal(ReservationRules.ExpiredForMemberMessage, error.Message); // message côté membre (libellés § 11)
    }

    [Fact(DisplayName = "Annuler deux fois → reservation_state_changed (CancelledByUser), une seule place libérée")]
    public void Cancel_twice_is_rejected()
    {
        var ev = Published();
        var reservation = Pending(ev);
        reservation.CancelByUser(ev.StartsAt, Now.AddHours(1), CancelDeadline);

        var error = Rejected(() => reservation.CancelByUser(ev.StartsAt, Now.AddHours(2), CancelDeadline));

        Assert.Equal(ErrorCodes.ReservationStateChanged, error.Code);
        Assert.Equal("CancelledByUser", error.Details[ErrorDetailKeys.CurrentStatus]);
        Assert.Equal(Now.AddHours(1), reservation.CancelledAt);
        Assert.Equal(ReservationRules.AlreadyHandledMessage, error.Message);
    }

    /// <summary>Tableau des transitions (mvp § 2) : depuis un statut final, aucune action de la tâche A n'est permise.</summary>
    public static TheoryData<string> FinalStates => new() { "Expired", "CancelledByUser" };

    [Theory(DisplayName = "Depuis un statut final : ni annuler ni confirmer ; Expire sans effet")]
    [MemberData(nameof(FinalStates))]
    public void Final_states_reject_transitions(string state)
    {
        var ev = Published();
        var reservation = Pending(ev);
        DateTimeOffset at;
        if (state == "Expired")
        {
            at = reservation.ExpiresAt.AddMinutes(1);
            reservation.Expire(at);
        }
        else
        {
            at = Now.AddHours(1);
            reservation.CancelByUser(ev.StartsAt, at, CancelDeadline);
        }

        Assert.Equal(ErrorCodes.ReservationStateChanged, Rejected(() => reservation.CancelByUser(ev.StartsAt, at, CancelDeadline)).Code);
        Assert.Equal(ErrorCodes.ReservationStateChanged, Rejected(() => reservation.Confirm(at)).Code);
        Assert.False(reservation.Expire(at.AddDays(10)));
        Assert.False(reservation.RescheduleDeadline(ev.StartsAt.AddDays(1), Lifetime, at));
        Assert.Null(reservation.CancellableUntil(ev.StartsAt, at, CancelDeadline));
        Assert.Equal(state, reservation.EffectiveStatus(at).ToString());
    }

    // ---- Recalcul de l'échéance (ADR 0018 § 5) ----

    [Fact(DisplayName = "Début avancé avant l'échéance → échéance ramenée au nouveau début")]
    public void Reschedule_earlier_start_pulls_deadline()
    {
        var reservation = Pending(); // échéance 03/12 09:00Z
        var newStart = Now.AddHours(20);

        Assert.True(reservation.RescheduleDeadline(newStart, Lifetime, Now.AddHours(1)));

        Assert.Equal(newStart, reservation.ExpiresAt);
    }

    [Fact(DisplayName = "Début reculé → échéance allongée, plafonnée à demande + 48 h")]
    public void Reschedule_later_start_extends_up_to_lifetime()
    {
        var ev = Published();
        var reservation = Pending(ev, ev.StartsAt.AddHours(-10)); // échéance = début
        Assert.Equal(ev.StartsAt, reservation.ExpiresAt);

        Assert.True(reservation.RescheduleDeadline(ev.StartsAt.AddDays(7), Lifetime, ev.StartsAt.AddHours(-9)));

        Assert.Equal(reservation.RequestedAt.AddHours(48), reservation.ExpiresAt);
    }

    [Fact(DisplayName = "Demande déjà échue → jamais ranimée par un recul du début")]
    public void Reschedule_never_revives_due_request()
    {
        var reservation = Pending();
        var expiresAt = reservation.ExpiresAt;

        Assert.False(reservation.RescheduleDeadline(Now.AddDays(30), Lifetime, expiresAt));

        Assert.Equal(expiresAt, reservation.ExpiresAt);
        Assert.Equal(ReservationStatus.Expired, reservation.EffectiveStatus(expiresAt));
    }

    [Fact(DisplayName = "Confirmée → échéance inchangée par le recalcul")]
    public void Reschedule_ignores_confirmed()
    {
        var reservation = Confirmed(Published());
        var expiresAt = reservation.ExpiresAt;

        Assert.False(reservation.RescheduleDeadline(Now.AddHours(5), Lifetime, Now.AddHours(1)));

        Assert.Equal(expiresAt, reservation.ExpiresAt);
    }

    [Fact(DisplayName = "CancellableUntil : échéance pour une demande en attente")]
    public void Cancellable_until_for_pending()
    {
        var ev = Published();
        var reservation = Pending(ev);
        Assert.Equal(reservation.ExpiresAt, reservation.CancellableUntil(ev.StartsAt, Now, CancelDeadline));
        Assert.Null(reservation.CancellableUntil(ev.StartsAt, reservation.ExpiresAt, CancelDeadline)); // échue
    }
}
