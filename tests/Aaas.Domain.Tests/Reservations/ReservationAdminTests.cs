using Aaas.Domain.Common;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;
using static Aaas.Domain.Tests.Events.EventTestData;

namespace Aaas.Domain.Tests.Reservations;

/// <summary>
/// Traitement par l'administrateur (US-E5 à E7, RG-08, ADR 0018 § 3) et tableau complet des transitions (mvp § 2) :
/// 6 statuts × 7 actions. Événement payant (15 TND) le 05/12 à 10 h Tunis ; demande le 01/12 à 10 h, échéance le 03/12.
/// </summary>
public class ReservationAdminTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);
    private static readonly TimeSpan CancelDeadline = TimeSpan.FromHours(24);
    private static readonly Guid Member = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Admin = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BusinessRuleException Rejected(Action action) => Assert.Throws<BusinessRuleException>(action);

    private static Event PaidEvent() => Published(Details(price: 15m));

    private static Reservation Pending(Event ev) => Reservation.Request(ev, Member, null, EventOccupancy.None, Now, Lifetime);

    /// <summary>Réservation dans l'état demandé, et l'instant auquel on agit ensuite (avant l'échéance sauf pour Expirée).</summary>
    private static (Reservation Reservation, DateTimeOffset At) InState(Event ev, ReservationStatus state)
    {
        var reservation = Pending(ev);
        var at = Now.AddHours(1);
        switch (state)
        {
            case ReservationStatus.Pending:
                break;
            case ReservationStatus.Confirmed:
                reservation.Confirm(at);
                break;
            case ReservationStatus.Refused:
                reservation.Refuse("Réservé aux adhérents", at);
                break;
            case ReservationStatus.Expired:
                at = reservation.ExpiresAt.AddMinutes(1);
                Assert.True(reservation.Expire(at));
                break;
            case ReservationStatus.CancelledByUser:
                reservation.CancelByUser(ev.StartsAt, at, CancelDeadline);
                break;
            case ReservationStatus.CancelledByAssociation:
                reservation.CancelByAssociation(ev.EndsAt, at);
                break;
        }

        Assert.Equal(state, reservation.Status);
        return (reservation, at.AddMinutes(1));
    }

    /// <summary>Résultat d'une action : nouveau statut (« Confirmed », « Paid »…), « noop » ou code d'erreur.</summary>
    private static string Act(Reservation reservation, Event ev, string action, DateTimeOffset at)
    {
        var before = reservation.Status;
        try
        {
            switch (action)
            {
                case "Confirm": reservation.Confirm(at); break;
                case "Refuse": reservation.Refuse(null, at); break;
                case "CancelByUser": reservation.CancelByUser(ev.StartsAt, at, CancelDeadline); break;
                case "CancelByAssociation": reservation.CancelByAssociation(ev.EndsAt, at); break;
                case "Expire": return reservation.Expire(at) ? reservation.Status.ToString() : "noop";
                case "MarkPaid": return reservation.MarkPaid(ev.IsFree, Admin, at) ? "Paid" : "noop";
                case "UnmarkPaid": return reservation.UnmarkPaid() ? "Unpaid" : "noop";
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
        catch (BusinessRuleException ex)
        {
            Assert.Equal(before, reservation.Status); // un refus ne change jamais l'état
            return ex.Code;
        }

        return reservation.Status.ToString();
    }

    private const string StateChanged = ErrorCodes.ReservationStateChanged;

    public static TheoryData<ReservationStatus, string, string> Transitions()
    {
        var data = new TheoryData<ReservationStatus, string, string>
        {
            { ReservationStatus.Pending, "Confirm", "Confirmed" },
            { ReservationStatus.Pending, "Refuse", "Refused" },
            { ReservationStatus.Pending, "CancelByUser", "CancelledByUser" },
            { ReservationStatus.Pending, "CancelByAssociation", "CancelledByAssociation" },
            { ReservationStatus.Pending, "Expire", "noop" },
            { ReservationStatus.Pending, "MarkPaid", ErrorCodes.ReservationNotConfirmed },
            { ReservationStatus.Pending, "UnmarkPaid", "noop" },

            { ReservationStatus.Confirmed, "Confirm", StateChanged },
            { ReservationStatus.Confirmed, "Refuse", StateChanged },
            { ReservationStatus.Confirmed, "CancelByUser", "CancelledByUser" },
            { ReservationStatus.Confirmed, "CancelByAssociation", "CancelledByAssociation" },
            { ReservationStatus.Confirmed, "Expire", "noop" },
            { ReservationStatus.Confirmed, "MarkPaid", "Paid" },
            { ReservationStatus.Confirmed, "UnmarkPaid", "noop" },
        };

        // Statuts finaux : aucune transition, Expire et UnmarkPaid (non payée) sans effet.
        foreach (var final in new[]
                 {
                     ReservationStatus.Refused, ReservationStatus.Expired,
                     ReservationStatus.CancelledByUser, ReservationStatus.CancelledByAssociation,
                 })
        {
            data.Add(final, "Confirm", StateChanged);
            data.Add(final, "Refuse", StateChanged);
            data.Add(final, "CancelByUser", StateChanged);
            data.Add(final, "CancelByAssociation", StateChanged);
            data.Add(final, "Expire", "noop");
            data.Add(final, "MarkPaid", StateChanged);
            data.Add(final, "UnmarkPaid", "noop");
        }

        return data;
    }

    [Theory(DisplayName = "Tableau des transitions : 6 statuts × 7 actions")]
    [MemberData(nameof(Transitions))]
    public void Transition_table(ReservationStatus state, string action, string expected)
    {
        var ev = PaidEvent();
        var (reservation, at) = InState(ev, state);

        Assert.Equal(expected, Act(reservation, ev, action, at));
    }

    [Theory(DisplayName = "Statut final : reservation_state_changed porte currentStatus, message admin selon le statut")]
    [InlineData(ReservationStatus.Refused, ReservationRules.AlreadyHandledMessage)]
    [InlineData(ReservationStatus.Expired, ReservationRules.ExpiredForAdminMessage)]
    [InlineData(ReservationStatus.CancelledByUser, ReservationRules.AlreadyHandledMessage)]
    [InlineData(ReservationStatus.CancelledByAssociation, ReservationRules.AlreadyHandledMessage)]
    public void Final_state_error_carries_current_status(ReservationStatus state, string message)
    {
        var ev = PaidEvent();
        var (reservation, at) = InState(ev, state);

        foreach (var action in new Action[] { () => reservation.Confirm(at), () => reservation.Refuse(null, at), () => reservation.CancelByAssociation(ev.EndsAt, at) })
        {
            var error = Rejected(action);
            Assert.Equal(state.ToString(), error.Details[ErrorDetailKeys.CurrentStatus]);
            Assert.Equal(message, error.Message);
        }
    }

    // ---- Demande échue mais pas encore écrite (statut effectif) ----

    [Fact(DisplayName = "Échue non écrite : confirmer, refuser, annuler (admin) → state_changed Expired, statut stocké inchangé")]
    public void Due_but_unwritten_request_is_handled_as_expired()
    {
        var ev = PaidEvent();
        var reservation = Pending(ev);
        var at = reservation.ExpiresAt;

        foreach (var action in new Action[] { () => reservation.Confirm(at), () => reservation.Refuse("x", at), () => reservation.CancelByAssociation(ev.EndsAt, at), () => reservation.MarkPaid(false, Admin, at) })
        {
            var error = Rejected(action);
            Assert.Equal(ErrorCodes.ReservationStateChanged, error.Code);
            Assert.Equal("Expired", error.Details[ErrorDetailKeys.CurrentStatus]);
            Assert.Equal(ReservationRules.ExpiredForAdminMessage, error.Message);
        }

        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Equal(new ReservationActions(false, false, false, false, false), reservation.AdminActions(ev.EndsAt, false, at));
    }

    // ---- Refus (US-E6) ----

    [Theory(DisplayName = "Refus : motif conservé (espaces retirés) ; vide ou blanc → null")]
    [InlineData("  Réservé aux adhérents  ", "Réservé aux adhérents")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Refuse_normalizes_reason(string? reason, string? expected)
    {
        var reservation = Pending(PaidEvent());
        var at = Now.AddHours(2);

        reservation.Refuse(reason, at);

        Assert.Equal(ReservationStatus.Refused, reservation.Status);
        Assert.Equal(expected, reservation.RefusalReason);
        Assert.Equal(at, reservation.DecidedAt);
        Assert.False(reservation.IsActive(at));
    }

    [Fact(DisplayName = "Refus : 500 caractères acceptés, 501 refusés (filet du Domain), statut inchangé")]
    public void Refuse_reason_length_limit()
    {
        var ok = Pending(PaidEvent());
        ok.Refuse(new string('a', 500), Now.AddHours(1));
        Assert.Equal(500, ok.RefusalReason!.Length);

        var tooLong = Pending(PaidEvent());
        Assert.Throws<ArgumentException>(() => tooLong.Refuse(new string('a', 501), Now.AddHours(1)));
        Assert.Equal(ReservationStatus.Pending, tooLong.Status);
    }

    // ---- Annulation par l'association (US-E5) ----

    [Fact(DisplayName = "Annulation admin d'une confirmée 2 h avant le début → Annulée par l'association")]
    public void Association_cancels_confirmed_shortly_before_start()
    {
        var ev = PaidEvent();
        var (reservation, _) = InState(ev, ReservationStatus.Confirmed);
        var at = ev.StartsAt.AddHours(-2);

        reservation.CancelByAssociation(ev.EndsAt, at);

        Assert.Equal(ReservationStatus.CancelledByAssociation, reservation.Status);
        Assert.Equal(at, reservation.CancelledAt);
        Assert.False(reservation.IsActive(at));
    }

    [Fact(DisplayName = "Annulation admin pendant l'événement → OK ; à la fin exacte → event_ended")]
    public void Association_cancel_until_event_end()
    {
        var ev = PaidEvent();
        var during = InState(ev, ReservationStatus.Confirmed).Reservation;
        during.CancelByAssociation(ev.EndsAt, ev.StartsAt.AddMinutes(30));
        Assert.Equal(ReservationStatus.CancelledByAssociation, during.Status);

        var ended = InState(ev, ReservationStatus.Confirmed).Reservation;
        var error = Rejected(() => ended.CancelByAssociation(ev.EndsAt, ev.EndsAt));
        Assert.Equal(ErrorCodes.EventEnded, error.Code);
        Assert.Equal(ReservationRules.EventEndedForCancellationMessage, error.Message);
        Assert.Equal(ReservationStatus.Confirmed, ended.Status);
    }

    // ---- Mention « Payée » (US-E7, RG-08) ----

    [Fact(DisplayName = "Marquer payée : date et admin enregistrés ; deuxième clic sans effet (date d'origine conservée)")]
    public void Mark_paid_records_admin_and_date()
    {
        var ev = PaidEvent();
        var (reservation, at) = InState(ev, ReservationStatus.Confirmed);

        Assert.True(reservation.MarkPaid(ev.IsFree, Admin, at));
        Assert.False(reservation.MarkPaid(ev.IsFree, Guid.NewGuid(), at.AddHours(1)));

        Assert.True(reservation.IsPaid);
        Assert.Equal(at, reservation.PaidAt);
        Assert.Equal(Admin, reservation.PaidByAdminId);
    }

    [Fact(DisplayName = "Marquer payée après la fin de l'événement → accepté (QF-4)")]
    public void Mark_paid_after_event_end()
    {
        var ev = PaidEvent();
        var (reservation, _) = InState(ev, ReservationStatus.Confirmed);

        Assert.True(reservation.MarkPaid(ev.IsFree, Admin, ev.EndsAt.AddDays(10)));
    }

    [Fact(DisplayName = "Événement gratuit → payment_not_applicable (même en attente) ; action non proposée")]
    public void Mark_paid_on_free_event()
    {
        var ev = Published();
        var (confirmed, at) = InState(ev, ReservationStatus.Confirmed);
        var pending = Pending(ev);

        var error = Rejected(() => confirmed.MarkPaid(ev.IsFree, Admin, at));
        Assert.Equal(ErrorCodes.PaymentNotApplicable, error.Code);
        Assert.Equal(ReservationRules.PaymentNotApplicableMessage, error.Message);
        Assert.Equal(ErrorCodes.PaymentNotApplicable, Rejected(() => pending.MarkPaid(ev.IsFree, Admin, at)).Code);
        Assert.False(confirmed.AdminActions(ev.EndsAt, ev.IsFree, at).CanMarkPaid);
    }

    [Fact(DisplayName = "En attente → reservation_not_confirmed, message « Confirmez d'abord… », currentStatus Pending")]
    public void Mark_paid_on_pending()
    {
        var ev = PaidEvent();
        var reservation = Pending(ev);

        var error = Rejected(() => reservation.MarkPaid(ev.IsFree, Admin, Now.AddHours(1)));

        Assert.Equal(ErrorCodes.ReservationNotConfirmed, error.Code);
        Assert.Equal(ReservationRules.NotConfirmedMessage, error.Message);
        Assert.Equal("Pending", error.Details[ErrorDetailKeys.CurrentStatus]);
        Assert.False(reservation.IsPaid);
    }

    [Theory(DisplayName = "Payée puis annulée (membre ou association) → reste payée ; la mention peut être retirée")]
    [InlineData(ReservationStatus.CancelledByUser)]
    [InlineData(ReservationStatus.CancelledByAssociation)]
    public void Paid_then_cancelled_stays_paid_and_can_be_unmarked(ReservationStatus cancelled)
    {
        var ev = PaidEvent();
        var (reservation, at) = InState(ev, ReservationStatus.Confirmed);
        reservation.MarkPaid(ev.IsFree, Admin, at);

        if (cancelled == ReservationStatus.CancelledByUser)
        {
            reservation.CancelByUser(ev.StartsAt, at.AddHours(1), CancelDeadline);
        }
        else
        {
            reservation.CancelByAssociation(ev.EndsAt, at.AddHours(1));
        }

        Assert.Equal(cancelled, reservation.Status);
        Assert.True(reservation.IsPaid);
        Assert.Equal(at, reservation.PaidAt);
        Assert.Equal(new ReservationActions(false, false, false, false, true), reservation.AdminActions(ev.EndsAt, ev.IsFree, at.AddHours(2)));

        Assert.True(reservation.UnmarkPaid());

        Assert.False(reservation.IsPaid);
        Assert.Null(reservation.PaidAt);
        Assert.Null(reservation.PaidByAdminId);
        Assert.Equal(cancelled, reservation.Status);
    }

    // ---- Actions proposées ----

    [Fact(DisplayName = "Actions : en attente (confirmer, refuser, annuler) ; confirmée payante (annuler, payée) ; après la fin (payée seulement)")]
    public void Admin_actions_follow_rules()
    {
        var ev = PaidEvent();
        var pending = Pending(ev);
        Assert.Equal(new ReservationActions(true, true, true, false, false), pending.AdminActions(ev.EndsAt, ev.IsFree, Now.AddHours(1)));

        var (confirmed, at) = InState(ev, ReservationStatus.Confirmed);
        Assert.Equal(new ReservationActions(false, false, true, true, false), confirmed.AdminActions(ev.EndsAt, ev.IsFree, at));
        Assert.Equal(new ReservationActions(false, false, false, true, false), confirmed.AdminActions(ev.EndsAt, ev.IsFree, ev.EndsAt));

        confirmed.MarkPaid(ev.IsFree, Admin, at);
        Assert.Equal(new ReservationActions(false, false, true, false, true), confirmed.AdminActions(ev.EndsAt, ev.IsFree, at));
    }
}
