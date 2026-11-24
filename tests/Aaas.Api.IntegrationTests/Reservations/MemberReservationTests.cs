using System.Net;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Reservations;

/// <summary>Parcours membre (US-D1 à D4, RG-02 à RG-05) avec une horloge simulée injectée dans l'API.</summary>
public sealed class MemberReservationTests(SqlServerFixture sql) : ReservationApiTestBase(sql)
{
    [Fact(DisplayName = "Demande réussie : 201, Location, Pending, échéance = demande + 48 h, une place de moins, no-store")]
    public async Task Request_succeeds()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 10);
        var member = (await RegisterMemberAsync()).Token;

        var response = await RequestSeatAsync(member, eventId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var json = await ReadJsonAsync(response);
        var id = json.GetProperty("id").GetGuid();
        Assert.Equal($"/api/me/reservations/{id}", response.Headers.Location?.ToString());
        Assert.Equal("Pending", json.GetProperty("status").GetString());
        Assert.Equal(Start, json.GetProperty("requestedAt").GetDateTimeOffset());
        Assert.Equal(Start.AddHours(48), json.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.True(json.GetProperty("canCancel").GetBoolean());
        Assert.Equal(Start.AddHours(48), json.GetProperty("cancellableUntil").GetDateTimeOffset());
        Assert.False(json.GetProperty("isPaid").GetBoolean());
        var ev = json.GetProperty("event");
        Assert.Equal(eventId, ev.GetProperty("id").GetGuid());
        Assert.True(ev.GetProperty("isVisible").GetBoolean());
        Assert.Equal(15m, ev.GetProperty("price").GetDecimal());
        Assert.Equal(9, await RemainingSeatsAsync(eventId));
    }

    [Fact(DisplayName = "Validation : eventId absent → 400 errors.eventId ; inconnu → 404 not_found")]
    public async Task Request_validation_and_unknown_event()
    {
        var member = (await RegisterMemberAsync()).Token;

        var missing = await SendAsync(HttpMethod.Post, "/api/me/reservations", member, new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("L'événement est obligatoire.", (await ReadJsonAsync(missing)).GetProperty("errors").GetProperty("eventId")[0].GetString());

        var unknown = await RequestSeatAsync(member, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var problem = await ReadJsonAsync(unknown);
        Assert.Equal("not_found", problem.GetProperty("code").GetString());
        Assert.Equal("Contenu introuvable.", problem.GetProperty("title").GetString());
    }

    [Fact(DisplayName = "Brouillon → 404 identique à un inconnu ; annulé → event_cancelled ; commencé → reservations_closed")]
    public async Task Request_on_draft_cancelled_and_started_events()
    {
        var admin = await AdminTokenAsync();
        var member = (await RegisterMemberAsync()).Token;
        var draftId = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();
        var cancelledId = await PublishedEventAsync(admin);
        await Factory.ExecuteAsync("UPDATE Events SET Status = 3 WHERE Id = @id", ("@id", cancelledId)); // annulation : bloc 4
        var soonId = await PublishedEventAsync(admin, startsIn: TimeSpan.FromHours(3));

        var draft = await RequestSeatAsync(member, draftId);
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
        Assert.Equal("Contenu introuvable.", (await ReadJsonAsync(draft)).GetProperty("title").GetString());

        var cancelled = await RequestSeatAsync(member, cancelledId);
        Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);
        var cancelledProblem = await ReadJsonAsync(cancelled);
        Assert.Equal("event_cancelled", cancelledProblem.GetProperty("code").GetString());
        Assert.Equal("Cet événement a été annulé.", cancelledProblem.GetProperty("title").GetString());

        Clock.Advance(TimeSpan.FromHours(4));
        var started = await RequestSeatAsync(member, soonId);
        Assert.Equal(HttpStatusCode.Conflict, started.StatusCode);
        Assert.Equal("reservations_closed", await CodeAsync(started));
    }

    [Fact(DisplayName = "Demande échue SANS tâche de fond : place rendue, redemande possible (201), ancienne ligne écrite Expirée")]
    public async Task Due_request_frees_seat_and_allows_a_new_request()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 1);
        var member = (await RegisterMemberAsync()).Token;
        var first = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        Assert.Equal(0, await RemainingSeatsAsync(eventId));

        Clock.Advance(TimeSpan.FromHours(48));

        Assert.Equal(1, await RemainingSeatsAsync(eventId));
        var expired = await GetMyReservationAsync(member, first);
        Assert.Equal("Expired", expired.GetProperty("status").GetString()); // statut effectif, base encore à 1
        Assert.Equal(JsonValueKindNull, expired.GetProperty("expiresAt").ValueKind);
        Assert.False(expired.GetProperty("canCancel").GetBoolean());
        Assert.Equal(1, await Factory.ScalarAsync<byte>("SELECT Status FROM Reservations WHERE Id = @id", ("@id", first)));

        var second = await RequestSeatOkAsync(member, eventId);

        Assert.NotEqual(first, second.GetProperty("id").GetGuid());
        Assert.Equal(4, await Factory.ScalarAsync<byte>("SELECT Status FROM Reservations WHERE Id = @id", ("@id", first)));
    }

    [Fact(DisplayName = "Déjà une demande active → 409 already_reserved, avec reservationId, même si l'événement est complet")]
    public async Task Already_reserved_comes_before_full()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 1);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();

        var again = await RequestSeatAsync(member, eventId);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var problem = await ReadJsonAsync(again);
        Assert.Equal("already_reserved", problem.GetProperty("code").GetString());
        Assert.Equal(id, problem.GetProperty("reservationId").GetGuid());
    }

    [Fact(DisplayName = "Annulation d'une confirmée : J−3 → 200 ; à J−24 h exactement → 200 ; à J−10 h → cancellation_deadline_passed")]
    public async Task Confirmed_cancellation_deadline()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 5, startsIn: TimeSpan.FromDays(4));
        var startsAt = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/events/{eventId}", null))).GetProperty("startsAt").GetDateTimeOffset();
        var tokens = await MemberTokensAsync(3);
        var ids = new List<Guid>();
        foreach (var token in tokens)
        {
            var id = (await RequestSeatOkAsync(token, eventId)).GetProperty("id").GetGuid();
            await ConfirmInDatabaseAsync(id);
            ids.Add(id);
        }

        Clock.SetUtcNow(startsAt.AddDays(-3));
        var confirmed = await GetMyReservationAsync(tokens[0], ids[0]);
        Assert.Equal("Confirmed", confirmed.GetProperty("status").GetString());
        Assert.Equal(startsAt.AddHours(-24), confirmed.GetProperty("cancellableUntil").GetDateTimeOffset());
        var early = await CancelAsync(tokens[0], ids[0]);
        Assert.Equal(HttpStatusCode.OK, early.StatusCode);
        Assert.Equal("CancelledByUser", (await ReadJsonAsync(early)).GetProperty("status").GetString());

        Clock.SetUtcNow(startsAt.AddHours(-24));
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(tokens[1], ids[1])).StatusCode);

        Clock.SetUtcNow(startsAt.AddHours(-10));
        Assert.False((await GetMyReservationAsync(tokens[2], ids[2])).GetProperty("canCancel").GetBoolean());
        var late = await CancelAsync(tokens[2], ids[2]);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("cancellation_deadline_passed", await CodeAsync(late));
        Assert.Equal("Confirmed", (await GetMyReservationAsync(tokens[2], ids[2])).GetProperty("status").GetString());
    }

    [Fact(DisplayName = "Annuler deux fois → 409 reservation_state_changed (CancelledByUser), une seule place libérée")]
    public async Task Cancel_twice()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 2);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(member, id)).StatusCode);
        var again = await CancelAsync(member, id);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var problem = await ReadJsonAsync(again);
        Assert.Equal("reservation_state_changed", problem.GetProperty("code").GetString());
        Assert.Equal("CancelledByUser", problem.GetProperty("currentStatus").GetString());
        Assert.Equal("Cette demande a déjà été traitée ou annulée.", problem.GetProperty("title").GetString());
        Assert.Equal(2, await RemainingSeatsAsync(eventId));
    }

    [Fact(DisplayName = "Annuler une demande échue → 409 avec le message membre « Votre demande a expiré… »")]
    public async Task Cancel_due_request()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();

        Clock.Advance(TimeSpan.FromHours(48));
        var response = await CancelAsync(member, id);

        var problem = await ReadJsonAsync(response);
        Assert.Equal("reservation_state_changed", problem.GetProperty("code").GetString());
        Assert.Equal("Expired", problem.GetProperty("currentStatus").GetString());
        Assert.Equal("Votre demande a expiré. Vous pouvez en faire une nouvelle.", problem.GetProperty("title").GetString());
    }

    [Fact(DisplayName = "« Ma demande » sur le détail : 204 sans demande, 200 avec la plus récente ; brouillon ou inconnu → 404")]
    public async Task My_reservation_for_event()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var draftId = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();
        var member = (await RegisterMemberAsync()).Token;

        var none = await SendAsync(HttpMethod.Get, $"/api/me/events/{eventId}/reservation", member);
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);
        Assert.Equal("no-store", none.Headers.CacheControl?.ToString());

        var first = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(member, first)).StatusCode);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var second = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();

        var latest = await SendAsync(HttpMethod.Get, $"/api/me/events/{eventId}/reservation", member);
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        Assert.Equal(second, (await ReadJsonAsync(latest)).GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/me/events/{draftId}/reservation", member)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/me/events/{Guid.NewGuid()}/reservation", member)).StatusCode);
    }

    [Fact(DisplayName = "Mes réservations : « À venir » (dont un événement en cours) et « Historique » ; tri et pagination")]
    public async Task List_current_and_history()
    {
        var admin = await AdminTokenAsync();
        var later = await PublishedEventAsync(admin, startsIn: TimeSpan.FromDays(10));
        var sooner = await PublishedEventAsync(admin, startsIn: TimeSpan.FromHours(5));
        var cancelledOne = await PublishedEventAsync(admin, startsIn: TimeSpan.FromDays(3));
        var member = (await RegisterMemberAsync()).Token;
        await RequestSeatOkAsync(member, later);
        var soonId = (await RequestSeatOkAsync(member, sooner)).GetProperty("id").GetGuid();
        await ConfirmInDatabaseAsync(soonId);
        var toCancel = (await RequestSeatOkAsync(member, cancelledOne)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(member, toCancel)).StatusCode);

        Clock.Advance(TimeSpan.FromHours(6)); // « sooner » a commencé (en cours, pas terminé) : reste « À venir »

        var current = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/me/reservations?scope=current", member));
        Assert.Equal(2, current.GetProperty("totalCount").GetInt32());
        var items = current.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(sooner, items[0].GetProperty("event").GetProperty("id").GetGuid());
        Assert.True(items[0].GetProperty("event").GetProperty("hasStarted").GetBoolean());
        Assert.Equal(later, items[1].GetProperty("event").GetProperty("id").GetGuid());

        var history = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/me/reservations?scope=history", member));
        var past = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal("CancelledByUser", past.GetProperty("status").GetString());

        var paged = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/me/reservations?scope=current&page=2&pageSize=1", member));
        Assert.Equal(later, Assert.Single(paged.GetProperty("items").EnumerateArray()).GetProperty("event").GetProperty("id").GetGuid());

        Clock.Advance(TimeSpan.FromHours(2)); // « sooner » terminé : passe dans l'historique
        var afterEnd = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/me/reservations?scope=history", member));
        Assert.Equal(2, afterEnd.GetProperty("totalCount").GetInt32());

        var badScope = await SendAsync(HttpMethod.Get, "/api/me/reservations?scope=abc", member);
        Assert.Equal(HttpStatusCode.BadRequest, badScope.StatusCode);
    }

    [Fact(DisplayName = "Isolation : B ne voit ni n'annule la réservation de A (404) et ne la voit pas dans sa liste")]
    public async Task Member_cannot_access_another_members_reservation()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var a = (await RegisterMemberAsync()).Token;
        var b = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(a, eventId)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/me/reservations/{id}", b)).StatusCode);
        var cancel = await CancelAsync(b, id);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Equal("Contenu introuvable.", (await ReadJsonAsync(cancel)).GetProperty("title").GetString());
        Assert.Equal(0, (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/me/reservations", b))).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Get, $"/api/me/events/{eventId}/reservation", b)).StatusCode);
        Assert.Equal("Pending", (await GetMyReservationAsync(a, id)).GetProperty("status").GetString());
    }

    [Fact(DisplayName = "Accès : visiteur 401 sur /api/me (avec no-store) ; identifiant mal formé → 404 pour un membre, 401 visiteur")]
    public async Task Access_rules()
    {
        var member = (await RegisterMemberAsync()).Token;

        var visitor = await SendAsync(HttpMethod.Get, "/api/me/reservations", null);
        Assert.Equal(HttpStatusCode.Unauthorized, visitor.StatusCode);
        Assert.Equal("no-store", visitor.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Post, "/api/me/reservations", null, new { eventId = Guid.NewGuid() })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, "/api/me/reservations/abc", member)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, "/api/me/reservations/abc/cancel", member)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, "/api/me/events/abc/reservation", member)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/me/reservations/abc", null)).StatusCode);
    }

    [Fact(DisplayName = "Un admin peut demander une place pour lui-même (QF-1)")]
    public async Task Admin_can_request_a_seat()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);

        Assert.Equal("Pending", (await RequestSeatOkAsync(admin, eventId)).GetProperty("status").GetString());
    }

    [Fact(DisplayName = "Date avancée par l'admin : l'échéance d'une demande en attente est ramenée au nouveau début")]
    public async Task Moving_event_start_reschedules_pending_deadlines()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, startsIn: TimeSpan.FromDays(10));
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        var version = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{eventId}", admin))).GetProperty("version").GetString();
        var newStart = Start.AddHours(20);
        var body = EventBodyAt(newStart, newStart.AddHours(2), version: version);
        body["confirmRegistrantsWarning"] = true;

        var put = await SendAsync(HttpMethod.Put, $"/api/admin/events/{eventId}", admin, body);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var startsAt = (await ReadJsonAsync(put)).GetProperty("startsAt").GetDateTimeOffset();
        Assert.Equal(startsAt, (await GetMyReservationAsync(member, id)).GetProperty("expiresAt").GetDateTimeOffset());
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;
}
