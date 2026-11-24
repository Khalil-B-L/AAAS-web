using System.Net;
using Aaas.Api.IntegrationTests.Support;
using Microsoft.Data.SqlClient;

namespace Aaas.Api.IntegrationTests.Reservations;

/// <summary>
/// RG-02 sur un vrai SQL Server (RCSI activé, comme Azure SQL) : demandes simultanées sur la dernière place,
/// double clic, verrou réellement posé, modification et dépublication en parallèle (ADR 0004, 0019).
/// Les assertions portent sur l'INVARIANT (jamais plus d'actives que la capacité), pas sur un ordre d'arrivée.
/// Relancer seuls : dotnet test tests/Aaas.Api.IntegrationTests --filter "Category=Concurrency"
/// </summary>
[Trait("Category", "Concurrency")]
public sealed class ReservationConcurrencyTests(SqlServerFixture sql) : ReservationApiTestBase(sql)
{
    [Theory(DisplayName = "N membres en même temps sur C places : exactement C demandes acceptées, les autres event_full")]
    [InlineData(1, 20)]
    [InlineData(3, 30)]
    public async Task Simultaneous_requests_never_exceed_capacity(int capacity, int members)
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity);
        var tokens = await MemberTokensAsync(members);

        var responses = await Task.WhenAll(tokens.Select(token => RequestSeatAsync(token, eventId)));

        Assert.Equal(capacity, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var refused = responses.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        Assert.All(refused, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        foreach (var response in refused)
        {
            Assert.Equal("event_full", await CodeAsync(response));
        }

        Assert.Equal(capacity, await ActiveCountInDatabaseAsync(eventId));
        Assert.Equal(0, await RemainingSeatsAsync(eventId));
    }

    [Fact(DisplayName = "Double clic du même membre : un 201, un 409 already_reserved avec le reservationId du premier")]
    public async Task Double_click_creates_a_single_request()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 10);
        var member = (await RegisterMemberAsync()).Token;

        var responses = await Task.WhenAll(RequestSeatAsync(member, eventId), RequestSeatAsync(member, eventId));

        var created = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var problem = await ReadJsonAsync(conflict);
        Assert.Equal("already_reserved", problem.GetProperty("code").GetString());
        Assert.Equal((await ReadJsonAsync(created)).GetProperty("id").GetGuid(), problem.GetProperty("reservationId").GetGuid());
        Assert.Equal(1, await ActiveCountInDatabaseAsync(eventId));
    }

    [Fact(DisplayName = "Verrou déterministe : une transaction SQL qui tient UPDLOCK sur l'événement bloque la demande jusqu'au COMMIT")]
    public async Task Request_waits_for_the_event_row_lock()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 5);
        var member = (await RegisterMemberAsync()).Token;

        await using var connection = new SqlConnection(Factory.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Id FROM Events WITH (UPDLOCK, ROWLOCK) WHERE Id = @id";
            command.Parameters.AddWithValue("@id", eventId);
            Assert.Equal(eventId, (Guid)(await command.ExecuteScalarAsync())!);
        }

        var pending = RequestSeatAsync(member, eventId);
        var first = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(pending, first); // toujours bloquée : le verrou U de la demande attend le nôtre

        await transaction.CommitAsync();
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact(DisplayName = "Capacité réduite de 5 à 3 pendant que 5 membres demandent : jamais plus d'actives que la capacité")]
    public async Task Capacity_reduction_in_parallel_with_requests()
    {
        var admin = await AdminTokenAsync();
        var begin = Clock.GetUtcNow().AddDays(10);
        var eventId = await CreatePublishedEventAsync(admin, EventBodyAt(begin, begin.AddHours(2), capacity: 5));
        var version = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{eventId}", admin)))
            .GetProperty("version").GetString();
        var tokens = await MemberTokensAsync(5);
        var update = EventBodyAt(begin, begin.AddHours(2), capacity: 3, version: version);
        update["confirmRegistrantsWarning"] = true;

        var requests = tokens.Select(token => RequestSeatAsync(token, eventId)).ToList();
        var put = SendAsync(HttpMethod.Put, $"/api/admin/events/{eventId}", admin, update);
        await Task.WhenAll([.. requests, put]);

        var putResponse = await put;
        Assert.True(
            putResponse.StatusCode == HttpStatusCode.OK
            || (putResponse.StatusCode == HttpStatusCode.Conflict && await CodeAsync(putResponse) == "capacity_below_reserved"),
            $"PUT inattendu : {(int)putResponse.StatusCode}");
        var capacity = await Factory.ScalarAsync<int>("SELECT Capacity FROM Events WHERE Id = @id", ("@id", eventId));
        Assert.True(await ActiveCountInDatabaseAsync(eventId) <= capacity);
        foreach (var response in (await Task.WhenAll(requests)).Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            Assert.Equal("event_full", await CodeAsync(response));
        }
    }

    [Fact(DisplayName = "Dépublication en parallèle d'une demande : jamais un brouillon avec une réservation active")]
    public async Task Unpublish_in_parallel_with_request()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 5);
        var member = (await RegisterMemberAsync()).Token;

        var requestTask = RequestSeatAsync(member, eventId);
        var unpublishTask = SendAsync(HttpMethod.Post, $"/api/admin/events/{eventId}/unpublish", admin);
        await Task.WhenAll(requestTask, unpublishTask);
        var request = await requestTask;
        var unpublish = await unpublishTask;

        if (request.StatusCode == HttpStatusCode.Created)
        {
            Assert.Equal(HttpStatusCode.Conflict, unpublish.StatusCode);
            Assert.Equal("event_has_active_reservations", await CodeAsync(unpublish));
        }
        else
        {
            Assert.Equal(HttpStatusCode.NotFound, request.StatusCode);
            Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        }

        var draftsWithActive = await Factory.ScalarAsync<int>(
            "SELECT COUNT(*) FROM Reservations r JOIN Events e ON e.Id = r.EventId WHERE e.Status = 1 AND r.Status IN (1, 2)");
        Assert.Equal(0, draftsWithActive);
    }
}
