using System.Net;
using Aaas.Api.IntegrationTests.Support;
using Aaas.Application.Reservations;
using Aaas.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aaas.Api.IntegrationTests.Reservations;

/// <summary>
/// Tests différés de l'étape 2 (plan-etape-2 § 9.4 : R7, R11, R12, R14, une requête de décompte), filets du schéma,
/// concordance entre la règle « active » en SQL et en mémoire, et absence de données personnelles dans les journaux (R24).
/// </summary>
public sealed class ReservationRulesTests(SqlServerFixture sql) : ReservationApiTestBase(sql)
{
    // Les commandes SQL exécutées sont journalisées (Information) pour compter les requêtes de décompte.
    protected override Dictionary<string, string?>? ConfigurationOverrides => new(base.ConfigurationOverrides!)
    {
        ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Information",
    };

    private async Task<(Guid EventId, List<string> Tokens, List<Guid> Ids)> EventWithRegistrantsAsync(string admin, int confirmed, int pending, int capacity = 20)
    {
        var eventId = await PublishedEventAsync(admin, capacity);
        var tokens = await MemberTokensAsync(confirmed + pending);
        var ids = new List<Guid>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var id = (await RequestSeatOkAsync(tokens[i], eventId)).GetProperty("id").GetGuid();
            if (i < confirmed)
            {
                await ConfirmInDatabaseAsync(id);
            }

            ids.Add(id);
        }

        return (eventId, tokens, ids);
    }

    private async Task<(System.Text.Json.JsonElement Admin, string Version)> AdminEventAsync(string admin, Guid eventId)
    {
        var json = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{eventId}", admin));
        return (json, json.GetProperty("version").GetString()!);
    }

    [Fact(DisplayName = "R11 : capacité sous les places prises → 409 capacity_below_reserved avec le message chiffré")]
    public async Task Capacity_below_reserved()
    {
        var admin = await AdminTokenAsync();
        var (eventId, _, _) = await EventWithRegistrantsAsync(admin, confirmed: 1, pending: 2);
        var (json, version) = await AdminEventAsync(admin, eventId);
        var startsAt = json.GetProperty("startsAt").GetDateTimeOffset();

        var response = await SendAsync(HttpMethod.Put, $"/api/admin/events/{eventId}", admin,
            EventBodyAt(startsAt, startsAt.AddHours(2), capacity: 2, version: version));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("capacity_below_reserved", problem.GetProperty("code").GetString());
        Assert.StartsWith("3 places sont déjà occupées (1 confirmées, 2 en attente).", problem.GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "R12 : date changée avec des inscrits → registrants_warning, puis 200 avec la confirmation")]
    public async Task Registrants_warning_then_confirmation()
    {
        var admin = await AdminTokenAsync();
        var (eventId, _, _) = await EventWithRegistrantsAsync(admin, confirmed: 0, pending: 1);
        var (json, version) = await AdminEventAsync(admin, eventId);
        var newStart = json.GetProperty("startsAt").GetDateTimeOffset().AddDays(1);
        var body = EventBodyAt(newStart, newStart.AddHours(2), version: version);
        body["price"] = 15m;

        var warning = await SendAsync(HttpMethod.Put, $"/api/admin/events/{eventId}", admin, body);
        Assert.Equal(HttpStatusCode.Conflict, warning.StatusCode);
        Assert.Equal("registrants_warning", await CodeAsync(warning));

        body["confirmRegistrantsWarning"] = true;
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/admin/events/{eventId}", admin, body)).StatusCode);
    }

    [Fact(DisplayName = "R14 : dépublication refusée avec une demande en attente ; acceptée une fois la demande annulée")]
    public async Task Unpublish_with_active_reservation()
    {
        var admin = await AdminTokenAsync();
        var (eventId, tokens, ids) = await EventWithRegistrantsAsync(admin, confirmed: 0, pending: 1);

        var refused = await SendAsync(HttpMethod.Post, $"/api/admin/events/{eventId}/unpublish", admin);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("event_has_active_reservations", await CodeAsync(refused));

        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(tokens[0], ids[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/admin/events/{eventId}/unpublish", admin)).StatusCode);

        // L'historique reste, sans lien (isVisible faux).
        var mine = await GetMyReservationAsync(tokens[0], ids[0]);
        Assert.False(mine.GetProperty("event").GetProperty("isVisible").GetBoolean());
    }

    [Fact(DisplayName = "R7 : demandes échues non comptées (détail public, fiche et liste admin)")]
    public async Task Due_requests_are_not_counted()
    {
        var admin = await AdminTokenAsync();
        var (eventId, _, _) = await EventWithRegistrantsAsync(admin, confirmed: 1, pending: 2, capacity: 10);
        Assert.Equal(7, await RemainingSeatsAsync(eventId));

        Clock.Advance(TimeSpan.FromHours(48));

        Assert.Equal(9, await RemainingSeatsAsync(eventId));
        var (json, _) = await AdminEventAsync(admin, eventId);
        Assert.Equal(0, json.GetProperty("pendingCount").GetInt32());
        Assert.Equal(1, json.GetProperty("confirmedCount").GetInt32());
        var row = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/events", admin))).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == eventId);
        Assert.Equal(0, row.GetProperty("pendingCount").GetInt32());
        Assert.Equal(9, row.GetProperty("remainingSeats").GetInt32());
    }

    [Fact(DisplayName = "GetManyAsync : une page de 20 événements = UNE seule requête de décompte (pas de N+1)")]
    public async Task Listing_counts_occupancy_in_one_query()
    {
        var admin = await AdminTokenAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            ids.Add(await PublishedEventAsync(admin, startsIn: TimeSpan.FromDays(5 + i)));
        }

        var member = (await RegisterMemberAsync()).Token;
        await RequestSeatOkAsync(member, ids[0]);
        var before = Factory.Logs.Messages.Count;

        var list = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/events?pageSize=20", null));

        Assert.Equal(20, list.GetProperty("items").GetArrayLength());
        var reservationQueries = Factory.Logs.Messages.Skip(before)
            .Count(m => m.Contains("Executed DbCommand", StringComparison.Ordinal) && m.Contains("[Reservations]", StringComparison.Ordinal));
        Assert.Equal(1, reservationQueries);
    }

    [Fact(DisplayName = "Schéma : deux lignes actives pour le même (événement, membre) → 2601 ; IsPaid sur une En attente → CHECK")]
    public async Task Schema_safety_nets()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var (_, _, memberId) = await RegisterMemberAsync();
        const string insert =
            "INSERT INTO Reservations (Id, EventId, UserId, Status, RequestedAt, ExpiresAt, IsPaid, PaidAt) " +
            "VALUES (NEWID(), @event, @user, @status, SYSUTCDATETIME(), DATEADD(hour, 48, SYSUTCDATETIME()), @paid, @paidAt)";

        await Factory.ExecuteAsync(insert, ("@event", eventId), ("@user", memberId), ("@status", 1), ("@paid", false), ("@paidAt", DBNull.Value));
        var duplicate = await Assert.ThrowsAsync<SqlException>(() =>
            Factory.ExecuteAsync(insert, ("@event", eventId), ("@user", memberId), ("@status", 2), ("@paid", false), ("@paidAt", DBNull.Value)));
        Assert.Equal(2601, duplicate.Number);

        // Une demande refusée (3) en plus de l'active : hors du filtre de l'index, acceptée.
        await Factory.ExecuteAsync(insert, ("@event", eventId), ("@user", memberId), ("@status", 3), ("@paid", false), ("@paidAt", DBNull.Value));

        var paidPending = await Assert.ThrowsAsync<SqlException>(() =>
            Factory.ExecuteAsync(insert, ("@event", eventId), ("@user", DBNull.Value), ("@status", 1), ("@paid", true), ("@paidAt", DateTimeOffset.UtcNow)));
        Assert.Equal(547, paidPending.Number); // violation de contrainte CHECK
    }

    [Fact(DisplayName = "Concordance : ReservationQueries.IsActive (SQL) = Reservation.IsActive (mémoire) sur 6 statuts × échéance")]
    public async Task Active_rule_matches_in_sql_and_in_memory()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var now = Clock.GetUtcNow();
        foreach (var status in Enumerable.Range(1, 6))
        {
            foreach (var expiresAt in new[] { now.AddMinutes(-1), now, now.AddMinutes(1) })
            {
                await Factory.ExecuteAsync(
                    "INSERT INTO Reservations (Id, EventId, UserId, Status, RequestedAt, ExpiresAt) VALUES (NEWID(), @event, NULL, @status, @requested, @expires)",
                    ("@event", eventId), ("@status", status), ("@requested", now.AddHours(-48)), ("@expires", expiresAt));
            }
        }

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inSql = await db.Reservations.Where(r => r.EventId == eventId).Where(ReservationQueries.IsActive(now)).Select(r => r.Id).ToListAsync();
        var inMemory = (await db.Reservations.AsNoTracking().Where(r => r.EventId == eventId).ToListAsync())
            .Where(r => r.IsActive(now)).Select(r => r.Id).ToList();

        Assert.Equal(18, await db.Reservations.CountAsync(r => r.EventId == eventId));
        Assert.Equal(4, inMemory.Count); // 3 confirmées + 1 en attente à échéance future
        Assert.Equal(inMemory.Order(), inSql.Order());
    }

    [Fact(DisplayName = "R24 : sur tout le parcours (demande, 409, annulation, liste), aucun e-mail, téléphone ni nom dans les journaux")]
    public async Task Logs_contain_no_personal_data()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 1);
        var (email, token, _) = await RegisterMemberAsync();
        var other = (await RegisterMemberAsync()).Token;

        var id = (await RequestSeatOkAsync(token, eventId)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await RequestSeatAsync(token, eventId)).StatusCode);   // already_reserved
        Assert.Equal(HttpStatusCode.Conflict, (await RequestSeatAsync(other, eventId)).StatusCode);   // event_full
        await SendAsync(HttpMethod.Get, "/api/me/reservations", token);
        await SendAsync(HttpMethod.Get, $"/api/me/events/{eventId}/reservation", token);
        Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(other, id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(token, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(token, id)).StatusCode);

        var messages = Factory.Logs.Messages;
        Assert.NotEmpty(messages);
        foreach (var forbidden in new[] { email, "+21620123456", "20 123 456", "Ben Ali", "Salma" })
        {
            Assert.DoesNotContain(messages, m => m.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }
}
