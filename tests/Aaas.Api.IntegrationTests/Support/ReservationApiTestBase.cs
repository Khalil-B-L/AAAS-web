using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace Aaas.Api.IntegrationTests.Support;

/// <summary>
/// Base des tests de réservation : un admin (Bootstrap), une horloge simulée injectée dans l'API, et des raccourcis
/// pour demander une place, lire « ma demande » et forcer un statut en SQL (la confirmation par l'admin arrive en tâche B).
/// Les jetons sont obtenus AVANT d'avancer l'horloge : JwtBearer valide la durée de vie avec l'horloge réelle.
/// </summary>
public abstract class ReservationApiTestBase(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    private FakeTimeProvider? _clock;

    protected DateTimeOffset Start { get; } = DateTimeOffset.UtcNow;

    protected override TimeProvider? ClockOverride => _clock ??= new FakeTimeProvider(Start);

    protected FakeTimeProvider Clock => (FakeTimeProvider)ClockOverride!;

    protected Task<HttpResponseMessage> RequestSeatAsync(string token, Guid eventId) =>
        SendAsync(HttpMethod.Post, "/api/me/reservations", token, new { eventId });

    protected async Task<JsonElement> RequestSeatOkAsync(string token, Guid eventId)
    {
        var response = await RequestSeatAsync(token, eventId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    protected Task<HttpResponseMessage> CancelAsync(string token, Guid reservationId) =>
        SendAsync(HttpMethod.Post, $"/api/me/reservations/{reservationId}/cancel", token);

    protected async Task<JsonElement> GetMyReservationAsync(string token, Guid reservationId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/me/reservations/{reservationId}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    protected async Task<int> RemainingSeatsAsync(Guid eventId) =>
        (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/events/{eventId}", null))).GetProperty("remainingSeats").GetInt32();

    protected static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).GetProperty("code").GetString();

    /// <summary>Événement publié, début dans <paramref name="startsIn"/> (par défaut 10 jours), fin 2 h plus tard.</summary>
    protected async Task<Guid> PublishedEventAsync(string adminToken, int capacity = 30, TimeSpan? startsIn = null, decimal? price = 15m)
    {
        var begin = Clock.GetUtcNow() + (startsIn ?? TimeSpan.FromDays(10));
        var body = EventBodyAt(begin, begin.AddHours(2), capacity: capacity);
        body["price"] = price;
        return await CreatePublishedEventAsync(adminToken, body);
    }

    /// <summary>Simule la confirmation par l'admin (tâche B) : écrit directement le statut 2.</summary>
    protected Task ConfirmInDatabaseAsync(Guid reservationId) =>
        Factory.ExecuteAsync(
            "UPDATE Reservations SET Status = 2, DecidedAt = @at WHERE Id = @id",
            ("@id", reservationId), ("@at", Clock.GetUtcNow()));

    protected Task<int> ActiveCountInDatabaseAsync(Guid eventId) =>
        Factory.ScalarAsync<int>(
            "SELECT COUNT(*) FROM Reservations WHERE EventId = @id AND (Status = 2 OR (Status = 1 AND ExpiresAt > @now))",
            ("@id", eventId), ("@now", Clock.GetUtcNow()));

    /// <summary>N membres distincts inscrits (séquentiellement : la création de compte n'est pas l'objet du test).</summary>
    protected async Task<List<string>> MemberTokensAsync(int count)
    {
        var tokens = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            tokens.Add((await RegisterMemberAsync()).Token);
        }

        return tokens;
    }
}
