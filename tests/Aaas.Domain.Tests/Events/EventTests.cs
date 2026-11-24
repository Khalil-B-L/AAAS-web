using Aaas.Domain.Common;
using Aaas.Domain.Events;
using static Aaas.Domain.Tests.Events.EventTestData;

namespace Aaas.Domain.Tests.Events;

/// <summary>Cycle de vie d'un événement : création, publication, dépublication, suppression, modification (US-E2).</summary>
public class EventTests
{
    private static BusinessRuleException Rejected(Action action) => Assert.Throws<BusinessRuleException>(action);

    // ---- Création ----

    [Fact(DisplayName = "Création : brouillon, dates en UTC, prix normalisé, CreatedAt = UpdatedAt = now")]
    public void Create_makes_a_draft()
    {
        var ev = Event.Create(Details(price: 0m, title: "  Atelier  "), Now);

        Assert.Equal(EventStatus.Draft, ev.Status);
        Assert.Equal(TimeSpan.Zero, ev.StartsAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 12, 5, 9, 0, 0, TimeSpan.Zero), ev.StartsAt); // 10 h Tunis = 09:00Z
        Assert.Null(ev.Price);
        Assert.True(ev.IsFree);
        Assert.Equal("Atelier", ev.Title);
        Assert.Equal(Now, ev.CreatedAt);
        Assert.Equal(Now, ev.UpdatedAt);
        Assert.Null(ev.PublishedAt);
        Assert.Null(ev.FirstPublishedAt);
        Assert.True(ev.CanBeDeleted);
    }

    [Fact(DisplayName = "Création avec des données invalides : filet validation_failed")]
    public void Create_with_invalid_details_throws() =>
        Assert.Equal(ErrorCodes.ValidationFailed, Rejected(() => Event.Create(Details(capacity: 0), Now)).Code);

    // ---- Publication ----

    [Fact(DisplayName = "Publier un brouillon à venir : Published, PublishedAt et FirstPublishedAt = now")]
    public void Publish_draft()
    {
        var ev = Draft();
        var later = Now.AddHours(1);

        ev.Publish(later);

        Assert.Equal(EventStatus.Published, ev.Status);
        Assert.Equal(later, ev.PublishedAt);
        Assert.Equal(later, ev.FirstPublishedAt);
        Assert.Equal(later, ev.UpdatedAt);
    }

    [Fact(DisplayName = "Republier un événement publié : event_state_changed")]
    public void Publish_twice_is_rejected() =>
        Assert.Equal(ErrorCodes.EventStateChanged, Rejected(() => Published().Publish(Now)).Code);

    [Fact(DisplayName = "Publier un événement dont le début est atteint (exactement now) : event_started")]
    public void Publish_started_event_is_rejected()
    {
        var ev = Draft();
        Assert.Equal(ErrorCodes.EventStarted, Rejected(() => ev.Publish(ev.StartsAt)).Code);
    }

    // ---- Dépublication (R14) ----

    [Fact(DisplayName = "Dépublier sans inscrit : Draft, PublishedAt = null, FirstPublishedAt conservé")]
    public void Unpublish_without_registrants()
    {
        var ev = Published();

        ev.Unpublish(Now.AddMinutes(5), EventOccupancy.None);

        Assert.Equal(EventStatus.Draft, ev.Status);
        Assert.Null(ev.PublishedAt);
        Assert.Equal(Now, ev.FirstPublishedAt);
        Assert.False(ev.CanBeDeleted);
    }

    [Fact(DisplayName = "R14 : dépublier avec une seule demande en attente : event_has_active_reservations")]
    public void Unpublish_with_pending_request_is_rejected() =>
        Assert.Equal(
            ErrorCodes.EventHasActiveReservations,
            Rejected(() => Published().Unpublish(Now, new EventOccupancy(Pending: 1, Confirmed: 0))).Code);

    [Fact(DisplayName = "Dépublier un brouillon : event_state_changed")]
    public void Unpublish_draft_is_rejected() =>
        Assert.Equal(ErrorCodes.EventStateChanged, Rejected(() => Draft().Unpublish(Now, EventOccupancy.None)).Code);

    // ---- Suppression (R15, étape 2) ----

    [Fact(DisplayName = "Un brouillon jamais publié peut être supprimé")]
    public void Never_published_draft_can_be_deleted() => Draft().EnsureCanBeDeleted();

    [Fact(DisplayName = "Un événement publié, ou publié puis dépublié, ne peut pas être supprimé : event_already_published")]
    public void Once_published_event_cannot_be_deleted()
    {
        var published = Published();
        Assert.Equal(ErrorCodes.EventAlreadyPublished, Rejected(published.EnsureCanBeDeleted).Code);

        published.Unpublish(Now, EventOccupancy.None);
        Assert.Equal(EventStatus.Draft, published.Status);
        Assert.Equal(ErrorCodes.EventAlreadyPublished, Rejected(published.EnsureCanBeDeleted).Code);
    }

    // ---- Modification : état (R28) ----

    [Fact(DisplayName = "R28 : événement commencé, début inchangé, description modifiée : accepté")]
    public void Started_event_can_be_edited()
    {
        var ev = Published();
        var duringEvent = ev.StartsAt.AddMinutes(30);

        ev.Update(Details(description: "Nouvelle description"), duringEvent, EventOccupancy.None, confirmRegistrantsWarning: false);

        Assert.Equal("Nouvelle description", ev.Description);
        Assert.Equal(duringEvent, ev.UpdatedAt);
    }

    [Fact(DisplayName = "R28 : événement terminé (EndsAt = now) : event_ended")]
    public void Ended_event_cannot_be_edited()
    {
        var ev = Published();
        Assert.Equal(ErrorCodes.EventEnded, Rejected(() => ev.Update(Details(), ev.EndsAt, EventOccupancy.None, false)).Code);
    }

    [Fact(DisplayName = "Événement commencé, fin déplacée dans le passé : filet validation_failed (le 400 est produit avant, par Application)")]
    public void Started_event_end_in_past_is_rejected()
    {
        var ev = Published();
        var duringEvent = ev.StartsAt.AddMinutes(30);
        var details = Details(endsAt: duringEvent.AddMinutes(-1));

        Assert.Equal(ErrorCodes.ValidationFailed, Rejected(() => ev.Update(details, duringEvent, EventOccupancy.None, false)).Code);
    }

    // ---- Modification : R11 capacité ----

    [Fact(DisplayName = "R11 : 10 confirmées + 5 en attente, capacité 12 : refus avec le décompte")]
    public void Capacity_below_active_is_rejected()
    {
        var ev = Published();
        var ex = Rejected(() => ev.Update(Details(capacity: 12), Now, new EventOccupancy(Pending: 5, Confirmed: 10), false));

        Assert.Equal(ErrorCodes.CapacityBelowReserved, ex.Code);
        Assert.StartsWith("15 places sont déjà occupées (10 confirmées, 5 en attente).", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "R11 : capacité ramenée exactement aux places occupées : accepté, l'événement devient complet")]
    public void Capacity_equal_to_active_is_accepted()
    {
        var ev = Published();
        var occupancy = new EventOccupancy(Pending: 5, Confirmed: 10);

        ev.Update(Details(capacity: 15), Now, occupancy, false);

        Assert.True(ev.IsFull(occupancy));
        Assert.Equal(0, ev.RemainingSeats(occupancy));
    }

    [Fact(DisplayName = "Augmenter la capacité d'un événement complet de 20 à 25 : 5 places redeviennent disponibles")]
    public void Increasing_capacity_frees_seats()
    {
        var ev = Published(Details(capacity: 20));
        var full = new EventOccupancy(Pending: 5, Confirmed: 15);
        Assert.True(ev.IsFull(full));

        ev.Update(Details(capacity: 25), Now, full, false);

        Assert.Equal(5, ev.RemainingSeats(full));
    }

    [Fact(DisplayName = "Priorité : capacité trop basse ET date changée sans confirmation : capacity_below_reserved")]
    public void Capacity_rule_comes_before_warning()
    {
        var ev = Published();
        var details = Details(startsAt: Start.AddDays(1), capacity: 1);

        Assert.Equal(ErrorCodes.CapacityBelowReserved, Rejected(() => ev.Update(details, Now, new EventOccupancy(2, 2), true)).Code);
    }

    // ---- Modification : R12 avertissement ----

    public static TheoryData<string, EventDetails> WarningChanges => new()
    {
        { "début", Details(startsAt: Start.AddDays(1), endsAt: Start.AddDays(1).AddHours(2)) },
        { "fin", Details(endsAt: Start.AddHours(3)) },
        { "prix", Details(price: 10m) },
    };

    [Theory(DisplayName = "R12 : inscrits et changement de date ou de prix sans confirmation : registrants_warning, rien n'est modifié")]
    [MemberData(nameof(WarningChanges))]
    public void Date_or_price_change_with_registrants_requires_confirmation(string change, EventDetails details)
    {
        _ = change;
        var ev = Published();
        var before = (ev.StartsAt, ev.EndsAt, ev.Price);

        var ex = Rejected(() => ev.Update(details, Now, new EventOccupancy(Pending: 2, Confirmed: 10), false));

        Assert.Equal(ErrorCodes.RegistrantsWarning, ex.Code);
        Assert.StartsWith("Cet événement a 12 inscrits", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, (ev.StartsAt, ev.EndsAt, ev.Price));
    }

    [Theory(DisplayName = "R12 : avec confirmation, la modification est enregistrée")]
    [MemberData(nameof(WarningChanges))]
    public void Confirmed_change_is_applied(string change, EventDetails details)
    {
        _ = change;
        var ev = Published();

        ev.Update(details, Now, new EventOccupancy(2, 10), confirmRegistrantsWarning: true);

        Assert.Equal(details.StartsAt, ev.StartsAt);
        Assert.Equal(details.EndsAt, ev.EndsAt);
        Assert.Equal(details.Price, ev.Price);
    }

    [Fact(DisplayName = "R12 : titre ou lieu seul changé avec des inscrits : pas d'avertissement (Q21)")]
    public void Title_or_location_change_needs_no_warning()
    {
        var ev = Published();
        ev.Update(Details(title: "Autre titre", location: "Sousse"), Now, new EventOccupancy(1, 1), false);
        Assert.Equal("Sousse", ev.Location);
    }

    [Fact(DisplayName = "R12 : aucun inscrit : pas d'avertissement")]
    public void No_registrants_no_warning() =>
        Published().Update(Details(startsAt: Start.AddDays(1), endsAt: Start.AddDays(1).AddHours(2)), Now, EventOccupancy.None, false);

    [Fact(DisplayName = "R12 : prix 0 sur un événement gratuit = pas de changement, pas d'avertissement")]
    public void Zero_price_on_free_event_is_not_a_change() =>
        Published(Details(price: null)).Update(Details(price: 0m), Now, new EventOccupancy(1, 0), false);

    [Fact(DisplayName = "R12 : même instant exprimé avec un autre décalage = pas de changement")]
    public void Same_instant_other_offset_is_not_a_change()
    {
        var ev = Published();
        var sameInstant = Details(startsAt: Start.ToUniversalTime(), endsAt: Start.AddHours(2).ToOffset(TimeSpan.FromHours(3)));
        ev.Update(sameInstant, Now, new EventOccupancy(1, 0), false);
    }

    // ---- R7 / R2 ----

    [Theory(DisplayName = "R7 : places restantes, jamais négatives ; complet si toutes occupées")]
    [InlineData(8, 4, 8, false)]
    [InlineData(15, 5, 0, true)]
    [InlineData(25, 5, 0, true)] // occupation > capacité : impossible en principe, mais jamais négatif
    public void Remaining_seats(int confirmed, int pending, int remaining, bool full)
    {
        var ev = Published(Details(capacity: 20));
        var occupancy = new EventOccupancy(pending, confirmed);

        Assert.Equal(remaining, ev.RemainingSeats(occupancy));
        Assert.Equal(full, ev.IsFull(occupancy));
        Assert.Equal(!full, ev.IsBookable(Now, occupancy));
    }

    [Fact(DisplayName = "R2 : brouillon, commencé ou complet : non réservable")]
    public void Bookability()
    {
        Assert.False(Draft().IsBookable(Now, EventOccupancy.None));

        var published = Published();
        Assert.True(published.IsBookable(Now, EventOccupancy.None));
        Assert.False(published.IsBookable(published.StartsAt, EventOccupancy.None));
        Assert.False(published.IsBookable(Now, new EventOccupancy(0, published.Capacity)));
    }

    [Fact(DisplayName = "EnsureCanAcceptRequest : publié à venir OK ; brouillon et commencé → reservations_closed ; complet → event_full")]
    public void Ensure_can_accept_request_matches_bookability()
    {
        var published = Published(Details(capacity: 3));
        published.EnsureCanAcceptRequest(Now, new EventOccupancy(1, 1));

        Assert.Equal(ErrorCodes.ReservationsClosed, Rejected(() => Draft().EnsureCanAcceptRequest(Now, EventOccupancy.None)).Code);
        Assert.Equal(ErrorCodes.ReservationsClosed, Rejected(() => published.EnsureCanAcceptRequest(published.StartsAt, EventOccupancy.None)).Code);
        Assert.Equal(ErrorCodes.EventFull, Rejected(() => published.EnsureCanAcceptRequest(Now, new EventOccupancy(2, 1))).Code);

        // Même règle que IsBookable (R2) : réservable ⇔ aucune exception.
        Assert.False(published.IsBookable(Now, new EventOccupancy(2, 1)));
        Assert.True(published.IsBookable(Now, new EventOccupancy(1, 1)));
    }

    [Fact(DisplayName = "Commencé / terminé : calculés à partir de now (bornes incluses)")]
    public void Started_and_ended_are_computed()
    {
        var ev = Draft();
        Assert.False(ev.HasStarted(ev.StartsAt.AddTicks(-1)));
        Assert.True(ev.HasStarted(ev.StartsAt));
        Assert.False(ev.HasEnded(ev.EndsAt.AddTicks(-1)));
        Assert.True(ev.HasEnded(ev.EndsAt));
    }
}
