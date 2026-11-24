using System.Net;
using System.Text.Json;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Reservations;

/// <summary>
/// Tâche B : traitement des demandes par l'administrateur (US-E4 à E7), de bout en bout sur un vrai SQL Server,
/// horloge simulée. Les jetons sont obtenus avant d'avancer l'horloge (JwtBearer valide avec l'horloge réelle).
/// </summary>
public sealed class AdminReservationTests(SqlServerFixture sql) : AdminReservationApiTestBase(sql)
{
    private const string AlreadyHandled = "Cette demande a déjà été traitée ou annulée.";
    private const string ExpiredForAdmin = "Cette demande a expiré : le membre doit faire une nouvelle demande.";

    // Les commandes SQL sont journalisées (Information) : le test des journaux vérifie aussi leur contenu.
    protected override Dictionary<string, string?>? ConfigurationOverrides => new(base.ConfigurationOverrides!)
    {
        ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Information",
    };

    // ---- Demandes à traiter (US-E6) ----

    [Fact(DisplayName = "Demandes à traiter : tous événements, échéance la plus proche d'abord, coordonnées, totalCount, no-store")]
    public async Task Pending_list_is_sorted_by_deadline()
    {
        var admin = await AdminTokenAsync();
        var later = await PublishedEventAsync(admin, startsIn: TimeSpan.FromDays(10));   // échéance = demande + 48 h
        var sooner = await PublishedEventAsync(admin, startsIn: TimeSpan.FromHours(20)); // échéance = début (20 h)
        var (token, memberId, email) = await RegisterNamedMemberAsync("Amel", "Ben Ali");
        var other = (await RegisterMemberAsync()).Token;
        var first = await RequestSeatIdAsync(token, later);
        var second = await RequestSeatIdAsync(other, sooner);
        var confirmed = await RequestSeatIdAsync(other, later);
        await AdminActionOkAsync(admin, confirmed, "confirm");

        var response = await SendAsync(HttpMethod.Get, "/api/admin/reservations/pending", admin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var page = await ReadJsonAsync(response);

        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { second, first }, items.Select(i => i.GetProperty("id").GetGuid()));
        var row = items[1];
        Assert.Equal("Pending", Str(row, "status"));
        Assert.Equal(memberId, row.GetProperty("member").GetProperty("id").GetGuid());
        Assert.Equal("Amel", Str(row.GetProperty("member"), "firstName"));
        Assert.Equal("Ben Ali", Str(row.GetProperty("member"), "lastName"));
        Assert.Equal(email, Str(row.GetProperty("member"), "email"));
        Assert.False(string.IsNullOrEmpty(Str(row.GetProperty("member"), "phoneNumber")));
        Assert.Equal(later, row.GetProperty("event").GetProperty("id").GetGuid());
        Assert.True(row.GetProperty("actions").GetProperty("canConfirm").GetBoolean());
        Assert.False(row.GetProperty("actions").GetProperty("canMarkPaid").GetBoolean());

        // Compteur du menu : pageSize=1 donne le total sans charger la liste.
        var counter = await PendingListAsync(admin, "?pageSize=1");
        Assert.Equal(2, counter.GetProperty("totalCount").GetInt32());
        Assert.Single(counter.GetProperty("items").EnumerateArray());

        // Une demande échue (non encore écrite) disparaît de la liste.
        Clock.Advance(TimeSpan.FromHours(20));
        var afterDeadline = await PendingListAsync(admin);
        Assert.Equal(1, afterDeadline.GetProperty("totalCount").GetInt32());
    }

    [Fact(DisplayName = "Demandes à traiter : pagination invalide → 400")]
    public async Task Pending_list_validates_paging()
    {
        var admin = await AdminTokenAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Get, "/api/admin/reservations/pending?pageSize=51", admin)).StatusCode);
    }

    // ---- Confirmer (US-E6) ----

    [Fact(DisplayName = "Confirmer : Confirmée, places inchangées, le membre voit Confirmée ; deuxième clic → 409 currentStatus Confirmed")]
    public async Task Confirm_request()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 10);
        var member = (await RegisterMemberAsync()).Token;
        var id = await RequestSeatIdAsync(member, eventId);
        Assert.Equal(9, await RemainingSeatsAsync(eventId));

        var confirmed = await AdminActionOkAsync(admin, id, "confirm");

        Assert.Equal("Confirmed", Str(confirmed, "status"));
        Assert.Equal(JsonValueKind.Null, confirmed.GetProperty("expiresAt").ValueKind);
        Assert.Equal(Clock.GetUtcNow(), confirmed.GetProperty("decidedAt").GetDateTimeOffset());
        Assert.True(confirmed.GetProperty("actions").GetProperty("canMarkPaid").GetBoolean());
        Assert.Equal(9, await RemainingSeatsAsync(eventId));
        Assert.Equal("Confirmed", Str(await GetMyReservationAsync(member, id), "status"));

        var again = await AdminActionAsync(admin, id, "confirm");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.True(again.Headers.CacheControl?.NoStore);
        var problem = await ReadJsonAsync(again);
        Assert.Equal("reservation_state_changed", Str(problem, "code"));
        Assert.Equal("Confirmed", Str(problem, "currentStatus"));
        Assert.Equal(AlreadyHandled, Str(problem, "title"));
    }

    [Fact(DisplayName = "Confirmer à échéance − 1 min → 200 ; à l'échéance → 409 currentStatus Expired, message admin")]
    public async Task Confirm_around_deadline()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var early = await RequestSeatIdAsync((await RegisterMemberAsync()).Token, eventId);
        var late = await RequestSeatIdAsync((await RegisterMemberAsync()).Token, eventId);

        Clock.Advance(TimeSpan.FromHours(48) - TimeSpan.FromMinutes(1));
        Assert.Equal("Confirmed", Str(await AdminActionOkAsync(admin, early, "confirm"), "status"));

        Clock.Advance(TimeSpan.FromMinutes(1));
        var response = await AdminActionAsync(admin, late, "confirm");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("reservation_state_changed", Str(problem, "code"));
        Assert.Equal("Expired", Str(problem, "currentStatus"));
        Assert.Equal(ExpiredForAdmin, Str(problem, "title"));
    }

    // ---- Refuser (US-E6) ----

    [Fact(DisplayName = "Refuser avec motif : place libérée, motif visible du membre")]
    public async Task Refuse_with_reason()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 10);
        var member = (await RegisterMemberAsync()).Token;
        var id = await RequestSeatIdAsync(member, eventId);

        var refused = await AdminActionOkAsync(admin, id, "refuse", new { reason = "  Réservé aux adhérents  " });

        Assert.Equal("Refused", Str(refused, "status"));
        Assert.Equal("Réservé aux adhérents", Str(refused, "refusalReason"));
        Assert.Equal(10, await RemainingSeatsAsync(eventId));
        var mine = await GetMyReservationAsync(member, id);
        Assert.Equal("Refused", Str(mine, "status"));
        Assert.Equal("Réservé aux adhérents", Str(mine, "refusalReason"));

        // Le membre peut refaire une demande (RG-03, QF-10).
        Assert.Equal(HttpStatusCode.Created, (await RequestSeatAsync(member, eventId)).StatusCode);
    }

    [Fact(DisplayName = "Refuser sans corps, avec {}, ou motif blanc → motif null ; 500 caractères OK ; 501 → 400 errors.reason")]
    public async Task Refuse_reason_variants()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var ids = new List<Guid>();
        foreach (var token in await MemberTokensAsync(5))
        {
            ids.Add(await RequestSeatIdAsync(token, eventId));
        }

        Assert.Equal(JsonValueKind.Null, (await AdminActionOkAsync(admin, ids[0], "refuse")).GetProperty("refusalReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await AdminActionOkAsync(admin, ids[1], "refuse", new { })).GetProperty("refusalReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await AdminActionOkAsync(admin, ids[2], "refuse", new { reason = "   " })).GetProperty("refusalReason").ValueKind);
        Assert.Equal(500, Str(await AdminActionOkAsync(admin, ids[3], "refuse", new { reason = new string('a', 500) }), "refusalReason")!.Length);

        var tooLong = await AdminActionAsync(admin, ids[4], "refuse", new { reason = new string('a', 501) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var problem = await ReadJsonAsync(tooLong);
        Assert.Equal("Le motif ne doit pas dépasser 500 caractères.", problem.GetProperty("errors").GetProperty("reason")[0].GetString());
        Assert.Equal(1, await ActiveCountInDatabaseAsync(eventId)); // la 5e est toujours en attente
    }

    // ---- Annuler (US-E5) ----

    [Fact(DisplayName = "Annulation admin : confirmée 2 h avant le début → Annulée par l'association, place libérée ; en attente aussi")]
    public async Task Association_cancels()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 10, startsIn: TimeSpan.FromDays(3));
        var member = (await RegisterMemberAsync()).Token;
        var late = (await RegisterMemberAsync()).Token; // jeton obtenu avant d'avancer l'horloge
        var confirmed = await RequestSeatIdAsync(member, eventId);
        await AdminActionOkAsync(admin, confirmed, "confirm");
        Assert.Equal(9, await RemainingSeatsAsync(eventId));

        Clock.Advance(TimeSpan.FromDays(3) - TimeSpan.FromHours(2));
        var cancelled = await AdminActionOkAsync(admin, confirmed, "cancel");
        Assert.Equal("CancelledByAssociation", Str(cancelled, "status"));
        Assert.Equal(Clock.GetUtcNow(), cancelled.GetProperty("cancelledAt").GetDateTimeOffset());
        Assert.Equal("CancelledByAssociation", Str(await GetMyReservationAsync(member, confirmed), "status"));

        // Demande en attente faite à J−2 h (échéance = début) : annulable aussi.
        var fresh = await RequestSeatIdAsync(late, eventId);
        Assert.Equal("CancelledByAssociation", Str(await AdminActionOkAsync(admin, fresh, "cancel"), "status"));
        Assert.Equal(10, await RemainingSeatsAsync(eventId));

        var twice = await AdminActionAsync(admin, confirmed, "cancel");
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("CancelledByAssociation", Str(await ReadJsonAsync(twice), "currentStatus"));
    }

    [Fact(DisplayName = "Annulation admin après la fin de l'événement → 409 event_ended ; « Payée » reste possible")]
    public async Task Association_cannot_cancel_after_end()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, startsIn: TimeSpan.FromDays(1));
        var id = await RequestSeatIdAsync((await RegisterMemberAsync()).Token, eventId);
        await AdminActionOkAsync(admin, id, "confirm");

        Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromHours(2));
        var response = await AdminActionAsync(admin, id, "cancel");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("event_ended", Str(problem, "code"));
        Assert.Equal("Cet événement est terminé : la réservation ne peut plus être annulée.", Str(problem, "title"));

        var paid = await AdminActionOkAsync(admin, id, "mark-paid"); // QF-4 : sans limite de date
        Assert.True(paid.GetProperty("isPaid").GetBoolean());
        Assert.False(paid.GetProperty("actions").GetProperty("canCancel").GetBoolean());
    }

    // ---- « Payée » (US-E7) ----

    [Fact(DisplayName = "Payée : date et nom de l'admin ; le membre voit Payée ; annulée ensuite par le membre → reste payée ; retrait possible")]
    public async Task Paid_then_cancelled_by_member()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, price: 15m);
        var member = (await RegisterMemberAsync()).Token;
        var id = await RequestSeatIdAsync(member, eventId);

        var notConfirmed = await AdminActionAsync(admin, id, "mark-paid");
        Assert.Equal(HttpStatusCode.Conflict, notConfirmed.StatusCode);
        var problem = await ReadJsonAsync(notConfirmed);
        Assert.Equal("reservation_not_confirmed", Str(problem, "code"));
        Assert.Equal("Confirmez d'abord la demande avant de la marquer payée", Str(problem, "title"));

        await AdminActionOkAsync(admin, id, "confirm");
        var paid = await AdminActionOkAsync(admin, id, "mark-paid");
        Assert.True(paid.GetProperty("isPaid").GetBoolean());
        Assert.Equal(Clock.GetUtcNow(), paid.GetProperty("paidAt").GetDateTimeOffset());
        Assert.Equal("Admin", Str(paid.GetProperty("paidBy"), "firstName"));
        Assert.Equal("Association", Str(paid.GetProperty("paidBy"), "lastName"));
        Assert.True(paid.GetProperty("actions").GetProperty("canUnmarkPaid").GetBoolean());
        var mine = await GetMyReservationAsync(member, id);
        Assert.True(mine.GetProperty("isPaid").GetBoolean());
        Assert.False(mine.TryGetProperty("paidBy", out _)); // le membre ne voit pas quel admin (ADR 0021 § 2)

        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(member, id)).StatusCode); // J−10 : dans le délai
        var afterCancel = await GetMyReservationAsync(member, id);
        Assert.Equal("CancelledByUser", Str(afterCancel, "status"));
        Assert.True(afterCancel.GetProperty("isPaid").GetBoolean());

        var unmarked = await AdminActionOkAsync(admin, id, "unmark-paid");
        Assert.False(unmarked.GetProperty("isPaid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, unmarked.GetProperty("paidAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, unmarked.GetProperty("paidBy").ValueKind);
        Assert.Equal("CancelledByUser", Str(unmarked, "status"));

        // Reposer la mention sur une annulée : refusé avec le statut actuel.
        var markCancelled = await AdminActionAsync(admin, id, "mark-paid");
        Assert.Equal(HttpStatusCode.Conflict, markCancelled.StatusCode);
        Assert.Equal("CancelledByUser", Str(await ReadJsonAsync(markCancelled), "currentStatus"));
    }

    [Fact(DisplayName = "Payée puis annulée par l'association → reste payée")]
    public async Task Paid_then_cancelled_by_association()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, price: 15m);
        var id = await RequestSeatIdAsync((await RegisterMemberAsync()).Token, eventId);
        await AdminActionOkAsync(admin, id, "confirm");
        await AdminActionOkAsync(admin, id, "mark-paid");

        var cancelled = await AdminActionOkAsync(admin, id, "cancel");

        Assert.Equal("CancelledByAssociation", Str(cancelled, "status"));
        Assert.True(cancelled.GetProperty("isPaid").GetBoolean());
        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Reservations WHERE Id = @id AND IsPaid = 1 AND Status = 6", ("@id", id)));
    }

    [Fact(DisplayName = "Événement gratuit → 409 payment_not_applicable, action non proposée")]
    public async Task Free_event_cannot_be_paid()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, price: null);
        var id = await RequestSeatIdAsync((await RegisterMemberAsync()).Token, eventId);
        var confirmed = await AdminActionOkAsync(admin, id, "confirm");
        Assert.False(confirmed.GetProperty("actions").GetProperty("canMarkPaid").GetBoolean());

        var response = await AdminActionAsync(admin, id, "mark-paid");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("payment_not_applicable", Str(problem, "code"));
        Assert.Equal("Cet événement est gratuit : aucun paiement à enregistrer", Str(problem, "title"));
    }

    // ---- Inscrits (US-E4) ----

    [Fact(DisplayName = "Inscrits : groupes, tri (en attente ancienne d'abord, confirmées nom puis prénom, autres récentes d'abord), total, compte supprimé")]
    public async Task Registrants_are_grouped_and_sorted()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 30);
        var salah = await RegisterNamedMemberAsync("Amine", "Ben Salah");
        var zied = await RegisterNamedMemberAsync("Zied", "Ben Ali");
        var amel = await RegisterNamedMemberAsync("Amel", "ben ali");
        var others = await MemberTokensAsync(5);

        foreach (var m in new[] { salah, zied, amel })
        {
            await AdminActionOkAsync(admin, await RequestSeatIdAsync(m.Token, eventId), "confirm");
        }

        var pendingOld = await RequestSeatIdAsync(others[0], eventId);
        Clock.Advance(TimeSpan.FromMinutes(5));
        var pendingNew = await RequestSeatIdAsync(others[1], eventId);
        Clock.Advance(TimeSpan.FromMinutes(5));
        var refused = await RequestSeatIdAsync(others[2], eventId);
        await AdminActionOkAsync(admin, refused, "refuse", new { reason = "Complet côté salle" });
        Clock.Advance(TimeSpan.FromMinutes(5));
        var cancelled = await RequestSeatIdAsync(others[3], eventId);
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(others[3], cancelled)).StatusCode);
        Clock.Advance(TimeSpan.FromMinutes(5));
        var deleted = await RequestSeatIdAsync(others[4], eventId);
        await AdminActionOkAsync(admin, deleted, "confirm");
        await Factory.ExecuteAsync("UPDATE Reservations SET UserId = NULL WHERE Id = @id", ("@id", deleted)); // ADR 0007

        var response = await SendAsync(HttpMethod.Get, $"/api/admin/events/{eventId}/reservations", admin);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await ReadJsonAsync(response);

        Assert.Equal(30, json.GetProperty("event").GetProperty("capacity").GetInt32());
        var occupancy = json.GetProperty("occupancy");
        Assert.Equal(2, occupancy.GetProperty("pending").GetInt32());
        Assert.Equal(4, occupancy.GetProperty("confirmed").GetInt32());
        Assert.Equal(6, occupancy.GetProperty("active").GetInt32());
        Assert.Equal(24, occupancy.GetProperty("remaining").GetInt32());

        Assert.Equal(new[] { pendingOld, pendingNew }, Ids(json, "pending"));
        var confirmed = json.GetProperty("confirmed").EnumerateArray().ToList();
        Assert.Equal(new[] { "Amel", "Zied", "Amine" }, confirmed.Take(3).Select(c => Str(c.GetProperty("member"), "firstName")));
        Assert.Equal(JsonValueKind.Null, confirmed[3].GetProperty("member").ValueKind); // « Compte supprimé », en dernier
        Assert.Equal(new[] { cancelled, refused }, Ids(json, "others"));
        Assert.Equal("Complet côté salle", Str(json.GetProperty("others")[1], "refusalReason"));

        // Une demande échue (non écrite) passe dans « autres » et ne compte plus.
        Clock.Advance(TimeSpan.FromHours(48));
        var later = await RegistrantsAsync(admin, eventId);
        Assert.Empty(later.GetProperty("pending").EnumerateArray());
        Assert.Equal(0, later.GetProperty("occupancy").GetProperty("pending").GetInt32());
        var expired = later.GetProperty("others").EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == pendingOld);
        Assert.Equal("Expired", Str(expired, "status"));
        Assert.False(expired.GetProperty("actions").GetProperty("canConfirm").GetBoolean());
    }

    [Fact(DisplayName = "Inscrits : événement sans réservation → listes vides ; événement inconnu → 404")]
    public async Task Registrants_empty_and_unknown()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 12);

        var json = await RegistrantsAsync(admin, eventId);
        Assert.Empty(json.GetProperty("pending").EnumerateArray());
        Assert.Empty(json.GetProperty("confirmed").EnumerateArray());
        Assert.Empty(json.GetProperty("others").EnumerateArray());
        Assert.Equal(12, json.GetProperty("occupancy").GetProperty("remaining").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/admin/events/{Guid.NewGuid()}/reservations", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AdminActionAsync(admin, Guid.NewGuid(), "confirm")).StatusCode);
    }

    // ---- R11, R12, R14 atteints par le vrai parcours (sans SQL direct) ----

    [Fact(DisplayName = "Confirmées par l'API : capacité trop basse (R11), avertissement (R12), dépublication refusée (R14) ; après annulation admin, dépublication OK")]
    public async Task Event_rules_reachable_through_admin_flow()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 5);
        var ids = new List<Guid>();
        foreach (var token in await MemberTokensAsync(2))
        {
            var id = await RequestSeatIdAsync(token, eventId);
            await AdminActionOkAsync(admin, id, "confirm");
            ids.Add(id);
        }

        var ev = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{eventId}", admin));
        var version = Str(ev, "version");
        var startsAt = ev.GetProperty("startsAt").GetDateTimeOffset();

        var tooSmall = await SendAsync(HttpMethod.Put, $"/api/admin/events/{eventId}", admin, PriceBody(startsAt, 1, version));
        Assert.Equal("capacity_below_reserved", await CodeAsync(tooSmall));

        var moved = PriceBody(startsAt.AddDays(1), 5, version);
        Assert.Equal("registrants_warning", await CodeAsync(await SendAsync(HttpMethod.Put, $"/api/admin/events/{eventId}", admin, moved)));

        var unpublish = await SendAsync(HttpMethod.Post, $"/api/admin/events/{eventId}/unpublish", admin);
        Assert.Equal("event_has_active_reservations", await CodeAsync(unpublish));

        foreach (var id in ids)
        {
            await AdminActionOkAsync(admin, id, "cancel");
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/admin/events/{eventId}/unpublish", admin)).StatusCode);
    }

    // ---- Accès et en-têtes ----

    public static TheoryData<string, string> AdminRoutes() => new()
    {
        { "GET", "/api/admin/reservations/pending" },
        { "GET", "/api/admin/events/{event}/reservations" },
        { "POST", "/api/admin/reservations/{id}/confirm" },
        { "POST", "/api/admin/reservations/{id}/refuse" },
        { "POST", "/api/admin/reservations/{id}/cancel" },
        { "POST", "/api/admin/reservations/{id}/mark-paid" },
        { "POST", "/api/admin/reservations/{id}/unmark-paid" },
    };

    [Theory(DisplayName = "Accès : visiteur 401, membre 403 (sans coordonnées), toujours no-store")]
    [MemberData(nameof(AdminRoutes))]
    public async Task Visitors_and_members_are_rejected(string method, string template)
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var member = (await RegisterMemberAsync()).Token;
        var id = await RequestSeatIdAsync(member, eventId);
        var url = template.Replace("{event}", eventId.ToString(), StringComparison.Ordinal).Replace("{id}", id.ToString(), StringComparison.Ordinal);

        var visitor = await SendAsync(new HttpMethod(method), url, null);
        var asMember = await SendAsync(new HttpMethod(method), url, member);

        Assert.Equal(HttpStatusCode.Unauthorized, visitor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asMember.StatusCode);
        Assert.True(visitor.Headers.CacheControl?.NoStore);
        Assert.True(asMember.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("Salma", await asMember.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("Pending", Str(await GetMyReservationAsync(member, id), "status")); // rien n'a changé
    }

    [Theory(DisplayName = "Identifiant mal formé : admin 404 ; membre 403 ; visiteur 401")]
    [InlineData("GET", "/api/admin/events/pas-un-guid/reservations")]
    [InlineData("POST", "/api/admin/reservations/pas-un-guid/confirm")]
    [InlineData("POST", "/api/admin/reservations/pas-un-guid/refuse")]
    [InlineData("POST", "/api/admin/reservations/pas-un-guid/cancel")]
    [InlineData("POST", "/api/admin/reservations/pas-un-guid/mark-paid")]
    [InlineData("POST", "/api/admin/reservations/pas-un-guid/unmark-paid")]
    public async Task Malformed_ids(string method, string url)
    {
        var admin = await AdminTokenAsync();
        var member = (await RegisterMemberAsync()).Token;

        var asAdmin = await SendAsync(new HttpMethod(method), url, admin);
        Assert.Equal(HttpStatusCode.NotFound, asAdmin.StatusCode);
        Assert.Equal("not_found", await CodeAsync(asAdmin));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(new HttpMethod(method), url, member)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(new HttpMethod(method), url, null)).StatusCode);
    }

    // ---- Journaux (R24, ADR 0021 § 4) ----

    [Fact(DisplayName = "R24 : parcours admin (listes, confirmer, refuser avec motif, payée, 409) sans e-mail, téléphone, nom ni motif dans les journaux")]
    public async Task Admin_flow_logs_contain_no_personal_data()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, price: 15m);
        var (token, _, email) = await RegisterNamedMemberAsync("Yasmine", "Trabelsi");
        var (otherToken, _, otherEmail) = await RegisterNamedMemberAsync("Hedi", "Gharbi");
        var first = await RequestSeatIdAsync(token, eventId);
        var second = await RequestSeatIdAsync(otherToken, eventId);
        const string reason = "Motif-confidentiel-Kairouan";

        await PendingListAsync(admin);
        await AdminActionOkAsync(admin, first, "confirm");
        await AdminActionOkAsync(admin, first, "mark-paid");
        await AdminActionOkAsync(admin, second, "refuse", new { reason });
        Assert.Equal(HttpStatusCode.Conflict, (await AdminActionAsync(admin, second, "confirm")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AdminActionAsync(admin, first, "refuse", new { reason = reason + new string('x', 500) })).StatusCode);
        await RegistrantsAsync(admin, eventId);
        await AdminActionOkAsync(admin, first, "cancel");

        var messages = Factory.Logs.Messages;
        Assert.NotEmpty(messages);
        foreach (var forbidden in new[] { email, otherEmail, "+21620123456", "20 123 456", "Yasmine", "Trabelsi", "Hedi", "Gharbi", reason })
        {
            Assert.DoesNotContain(messages, m => m.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static List<Guid> Ids(JsonElement json, string group) =>
        json.GetProperty(group).EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    private static Dictionary<string, object?> PriceBody(DateTimeOffset startsAt, int capacity, string? version)
    {
        var body = EventBodyAt(startsAt, startsAt.AddHours(2), capacity: capacity, version: version);
        body["price"] = 15m;
        return body;
    }
}
