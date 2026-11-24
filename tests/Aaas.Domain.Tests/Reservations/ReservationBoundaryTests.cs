using System.Reflection;
using Aaas.Domain.Common;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;
using static Aaas.Domain.Tests.Events.EventTestData;

namespace Aaas.Domain.Tests.Reservations;

/// <summary>
/// Cas aux bornes des réservations (tâche A) : échéance = min(demande + 48 h, début), états de l'événement,
/// limite d'annulation de 24 h à la milliseconde, recalcul d'échéance sans ranimer une demande échue (ADR 0005, 0018 § 5).
/// Instants fixes : Now = 01/12 09:00Z, début de l'événement = 05/12 09:00Z (4 jours plus tard).
/// </summary>
public class ReservationBoundaryTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);
    private static readonly TimeSpan CancelDeadline = TimeSpan.FromHours(24);
    private static readonly Guid Member = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Reservation RequestAt(Event ev, DateTimeOffset at) =>
        Reservation.Request(ev, Member, null, EventOccupancy.None, at, Lifetime);

    private static Event WithStatus(EventStatus status)
    {
        var ev = Published();
        typeof(Event).GetProperty(nameof(Event.Status), BindingFlags.Instance | BindingFlags.Public)!.SetValue(ev, status);
        return ev;
    }

    // ---- ExpiresAt = min(RequestedAt + 48 h, StartsAt) ----

    [Theory(DisplayName = "Échéance = min(demande + 48 h, début) : avant, exactement et après la bascule")]
    [InlineData(-1, 48 * 3600 - 1)] // début 1 s avant la fin des 48 h → début
    [InlineData(0, 48 * 3600)]      // égalité : les deux formules donnent le même instant
    [InlineData(1, 48 * 3600)]      // début 1 s après les 48 h → demande + 48 h
    [InlineData(3600, 48 * 3600)]
    [InlineData(-47 * 3600, 3600)]
    public void ExpiresAt_is_the_earlier_of_lifetime_and_start(int startOffsetSecondsAfter48h, int expectedSecondsAfterRequest)
    {
        var requested = Now;
        var start = requested.AddHours(48).AddSeconds(startOffsetSecondsAfter48h);

        var expiresAt = ReservationRules.ComputeExpiresAt(requested, Lifetime, start);

        Assert.Equal(requested.AddSeconds(expectedSecondsAfterRequest), expiresAt);
        Assert.True(expiresAt <= start);
        Assert.True(expiresAt > requested);
    }

    [Fact(DisplayName = "Demande exactement 48 h avant le début : échéance = début = demande + 48 h")]
    public void Request_exactly_48_hours_before_start()
    {
        var ev = Published();
        var reservation = RequestAt(ev, ev.StartsAt.AddHours(-48));

        Assert.Equal(ev.StartsAt, reservation.ExpiresAt);
        Assert.Equal(reservation.RequestedAt.AddHours(48), reservation.ExpiresAt);
    }

    [Fact(DisplayName = "Demande 1 ms avant le début : acceptée, échéance = début (strictement après la demande)")]
    public void Request_one_millisecond_before_start_is_accepted()
    {
        var ev = Published();
        var at = ev.StartsAt.AddMilliseconds(-1);

        var reservation = RequestAt(ev, at);

        Assert.Equal(ev.StartsAt, reservation.ExpiresAt);
        Assert.True(reservation.ExpiresAt > reservation.RequestedAt); // CK_Reservations_Deadline
        Assert.Equal(ReservationStatus.Expired, reservation.EffectiveStatus(ev.StartsAt));
    }

    [Fact(DisplayName = "Échéance : le décalage de l'entrée (+01:00) n'influence pas le résultat, qui est en UTC")]
    public void ExpiresAt_is_normalised_to_utc()
    {
        var requested = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.FromHours(1));
        var start = new DateTimeOffset(2026, 12, 10, 10, 0, 0, TimeSpan.FromHours(1));

        var expiresAt = ReservationRules.ComputeExpiresAt(requested, Lifetime, start);

        Assert.Equal(TimeSpan.Zero, expiresAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 12, 3, 9, 0, 0, TimeSpan.Zero), expiresAt);
    }

    // ---- États de l'événement ----

    [Theory(DisplayName = "Demande selon l'état de l'événement : brouillon et commencé → clos, annulé → event_cancelled")]
    [InlineData(EventStatus.Draft, ErrorCodes.ReservationsClosed)]
    [InlineData(EventStatus.Cancelled, ErrorCodes.EventCancelled)]
    public void Request_depends_on_event_status(EventStatus status, string expectedCode)
    {
        var ev = WithStatus(status);

        var error = Assert.Throws<BusinessRuleException>(() => RequestAt(ev, Now));

        Assert.Equal(expectedCode, error.Code);
    }

    [Theory(DisplayName = "Demande après le début : en cours ou terminé → reservations_closed, début exact compris")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3600)]          // en cours
    [InlineData(2 * 3600)]      // fin exacte
    [InlineData(2 * 3600 + 1)]  // terminé
    public void Request_at_or_after_start_is_closed(int secondsAfterStart)
    {
        var ev = Published();

        var error = Assert.Throws<BusinessRuleException>(() => RequestAt(ev, ev.StartsAt.AddSeconds(secondsAfterStart)));

        Assert.Equal(ErrorCodes.ReservationsClosed, error.Code);
    }

    [Fact(DisplayName = "Événement annulé ET commencé : event_cancelled prime sur reservations_closed")]
    public void Cancelled_and_started_reports_cancelled()
    {
        var ev = WithStatus(EventStatus.Cancelled);

        var error = Assert.Throws<BusinessRuleException>(() => RequestAt(ev, ev.StartsAt.AddHours(1)));

        Assert.Equal(ErrorCodes.EventCancelled, error.Code);
    }

    [Fact(DisplayName = "Brouillon sur un événement saturé : reservations_closed prime sur event_full")]
    public void Draft_with_full_occupancy_reports_closed_not_full()
    {
        var error = Assert.Throws<BusinessRuleException>(() =>
            Reservation.Request(Draft(), Member, null, new EventOccupancy(0, 20), Now, Lifetime));

        Assert.Equal(ErrorCodes.ReservationsClosed, error.Code);
    }

    // ---- Annulation par le membre : limite à la milliseconde ----

    private static Reservation ConfirmedFor(Event ev)
    {
        var reservation = RequestAt(ev, Now);
        reservation.Confirm(Now.AddMinutes(1));
        return reservation;
    }

    [Theory(DisplayName = "Confirmée : annulation à la limite (début − 24 h) ± 1 ms")]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Confirmed_cancellation_limit_to_the_millisecond(int millisecondsAfterLimit, bool allowed)
    {
        var ev = Published();
        var reservation = ConfirmedFor(ev);
        var at = ev.StartsAt.AddHours(-24).AddMilliseconds(millisecondsAfterLimit);

        Assert.Equal(allowed, reservation.CanBeCancelledByUser(ev.StartsAt, at, CancelDeadline));
        if (allowed)
        {
            reservation.CancelByUser(ev.StartsAt, at, CancelDeadline);
            Assert.Equal(ReservationStatus.CancelledByUser, reservation.Status);
        }
        else
        {
            var error = Assert.Throws<BusinessRuleException>(() => reservation.CancelByUser(ev.StartsAt, at, CancelDeadline));
            Assert.Equal(ErrorCodes.CancellationDeadlinePassed, error.Code);
            Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
            Assert.Null(reservation.CancelledAt);
        }
    }

    [Fact(DisplayName = "Confirmée, événement commencé puis terminé : plus annulable (cancellation_deadline_passed)")]
    public void Confirmed_cannot_be_cancelled_once_event_has_started_or_ended()
    {
        var ev = Published();
        var reservation = ConfirmedFor(ev);

        foreach (var at in new[] { ev.StartsAt, ev.StartsAt.AddHours(1), ev.EndsAt, ev.EndsAt.AddDays(2) })
        {
            Assert.False(reservation.CanBeCancelledByUser(ev.StartsAt, at, CancelDeadline));
            Assert.Equal(
                ErrorCodes.CancellationDeadlinePassed,
                Assert.Throws<BusinessRuleException>(() => reservation.CancelByUser(ev.StartsAt, at, CancelDeadline)).Code);
        }
    }

    [Fact(DisplayName = "Confirmée d'un événement qui commence dans moins de 24 h dès la confirmation : déjà non annulable")]
    public void Confirmed_inside_the_deadline_window_is_never_cancellable()
    {
        var ev = Published();
        var reservation = RequestAt(ev, ev.StartsAt.AddHours(-10));
        reservation.Confirm(ev.StartsAt.AddHours(-9));

        Assert.False(reservation.CanBeCancelledByUser(ev.StartsAt, ev.StartsAt.AddHours(-9), CancelDeadline));
    }

    [Fact(DisplayName = "En attente non échue : annulable jusqu'à 1 ms avant l'échéance ; à l'échéance, plus (statut effectif Expirée)")]
    public void Pending_cancellation_stops_exactly_at_deadline()
    {
        var ev = Published();
        var reservation = RequestAt(ev, Now);
        var justBefore = reservation.ExpiresAt.AddMilliseconds(-1);

        Assert.True(reservation.CanBeCancelledByUser(ev.StartsAt, justBefore, CancelDeadline));
        Assert.False(reservation.CanBeCancelledByUser(ev.StartsAt, reservation.ExpiresAt, CancelDeadline));

        var error = Assert.Throws<BusinessRuleException>(() => reservation.CancelByUser(ev.StartsAt, reservation.ExpiresAt, CancelDeadline));
        Assert.Equal("Expired", error.Details[ErrorDetailKeys.CurrentStatus]);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        reservation.CancelByUser(ev.StartsAt, justBefore, CancelDeadline);
        Assert.Equal(ReservationStatus.CancelledByUser, reservation.Status);
    }

    [Fact(DisplayName = "Annulation : CancelledAt est écrit en UTC même si now arrive avec un autre décalage")]
    public void CancelledAt_is_utc()
    {
        var ev = Published();
        var reservation = RequestAt(ev, Now);
        var at = Now.AddHours(1).ToOffset(TimeSpan.FromHours(1));

        reservation.CancelByUser(ev.StartsAt, at, CancelDeadline);

        Assert.Equal(TimeSpan.Zero, reservation.CancelledAt!.Value.Offset);
        Assert.Equal(at, reservation.CancelledAt);
    }

    [Fact(DisplayName = "Annulée par le membre : l'échéance est conservée, jamais affichée comme échue ni active")]
    public void Cancelled_reservation_is_neither_due_nor_active()
    {
        var ev = Published();
        var reservation = RequestAt(ev, Now);
        reservation.CancelByUser(ev.StartsAt, Now.AddHours(1), CancelDeadline);

        var afterDeadline = reservation.ExpiresAt.AddDays(1);

        Assert.False(reservation.IsDue(afterDeadline));
        Assert.False(reservation.IsActive(Now.AddHours(2)));
        Assert.Equal(ReservationStatus.CancelledByUser, reservation.EffectiveStatus(afterDeadline));
        Assert.False(reservation.Expire(afterDeadline));
    }

    // ---- Recalcul de l'échéance quand l'admin déplace la date ----

    [Fact(DisplayName = "Recalcul : nouveau début qui redonne la même échéance → aucun changement signalé")]
    public void Reschedule_with_same_result_reports_no_change()
    {
        var reservation = RequestAt(Published(), Now); // échéance = demande + 48 h

        Assert.False(reservation.RescheduleDeadline(Now.AddDays(30), Lifetime, Now.AddHours(1)));
        Assert.Equal(Now.AddHours(48), reservation.ExpiresAt);
    }

    [Fact(DisplayName = "Recalcul : début déplacé exactement sur demande + 48 h → échéance inchangée")]
    public void Reschedule_start_equal_to_lifetime_end()
    {
        var reservation = RequestAt(Published(), Now);

        Assert.False(reservation.RescheduleDeadline(Now.AddHours(48), Lifetime, Now.AddHours(1)));
        Assert.Equal(Now.AddHours(48), reservation.ExpiresAt);
    }

    [Fact(DisplayName = "Recalcul à 1 ms avant l'échéance : encore recalculée ; à l'échéance exacte : intouchée (échue)")]
    public void Reschedule_stops_at_the_deadline_instant()
    {
        var before = RequestAt(Published(), Now);
        var at = before.ExpiresAt;
        Assert.True(before.RescheduleDeadline(Now.AddHours(10), Lifetime, at.AddMilliseconds(-1)));
        Assert.Equal(Now.AddHours(10), before.ExpiresAt);

        var due = RequestAt(Published(), Now);
        Assert.False(due.RescheduleDeadline(Now.AddHours(10), Lifetime, due.ExpiresAt));
        Assert.Equal(Now.AddHours(48), due.ExpiresAt);
        Assert.Equal(ReservationStatus.Expired, due.EffectiveStatus(due.ExpiresAt));
    }

    [Fact(DisplayName = "Recalcul : après un recul du début suivi d'un avancement, l'échéance suit toujours min(demande + 48 h, début)")]
    public void Reschedule_back_and_forth_is_consistent()
    {
        var reservation = RequestAt(Published(), Now);

        reservation.RescheduleDeadline(Now.AddHours(5), Lifetime, Now.AddHours(1));
        Assert.Equal(Now.AddHours(5), reservation.ExpiresAt);

        reservation.RescheduleDeadline(Now.AddDays(20), Lifetime, Now.AddHours(2));
        Assert.Equal(Now.AddHours(48), reservation.ExpiresAt);

        reservation.RescheduleDeadline(Now.AddHours(30), Lifetime, Now.AddHours(3));
        Assert.Equal(Now.AddHours(30), reservation.ExpiresAt);
    }

    [Theory(DisplayName = "Recalcul : jamais appliqué à une réservation annulée par le membre ou expirée écrite")]
    [InlineData("CancelledByUser")]
    [InlineData("Expired")]
    public void Reschedule_ignores_final_reservations(string state)
    {
        var ev = Published();
        var reservation = RequestAt(ev, Now);
        if (state == "Expired")
        {
            reservation.Expire(reservation.ExpiresAt);
        }
        else
        {
            reservation.CancelByUser(ev.StartsAt, Now.AddHours(1), CancelDeadline);
        }

        var before = reservation.ExpiresAt;

        Assert.False(reservation.RescheduleDeadline(Now.AddDays(30), Lifetime, Now.AddHours(1)));
        Assert.Equal(before, reservation.ExpiresAt);
    }
}
