using System.Net;
using System.Text.Json;

namespace Aaas.Api.IntegrationTests.Support;

/// <summary>Raccourcis des actions admin sur les réservations (tâche B) : /api/admin/reservations/{id}/{action}.</summary>
public abstract class AdminReservationApiTestBase(SqlServerFixture sql) : ReservationApiTestBase(sql)
{
    protected Task<HttpResponseMessage> AdminActionAsync(string adminToken, Guid reservationId, string action, object? body = null) =>
        SendAsync(HttpMethod.Post, $"/api/admin/reservations/{reservationId}/{action}", adminToken, body);

    protected async Task<JsonElement> AdminActionOkAsync(string adminToken, Guid reservationId, string action, object? body = null)
    {
        var response = await AdminActionAsync(adminToken, reservationId, action, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    protected async Task<JsonElement> RegistrantsAsync(string adminToken, Guid eventId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/admin/events/{eventId}/reservations", adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    protected async Task<JsonElement> PendingListAsync(string adminToken, string query = "")
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/admin/reservations/pending{query}", adminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    /// <summary>Membre au nom choisi (tri des confirmées), renvoie son jeton et son identifiant.</summary>
    protected async Task<(string Token, Guid Id, string Email)> RegisterNamedMemberAsync(string firstName, string lastName)
    {
        var email = NewEmail();
        var response = await RegisterAsync(RegistrationBody(email, firstName: firstName, lastName: lastName));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await ReadJsonAsync(response);
        return (json.GetProperty("accessToken").GetString()!, json.GetProperty("user").GetProperty("id").GetGuid(), email);
    }

    protected async Task<Guid> RequestSeatIdAsync(string token, Guid eventId) =>
        (await RequestSeatOkAsync(token, eventId)).GetProperty("id").GetGuid();

    protected static string? Str(JsonElement json, string property) => json.GetProperty(property).GetString();
}
