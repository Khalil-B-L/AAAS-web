using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aaas.Api.IntegrationTests.Support;

/// <summary>Base des tests qui ont besoin d'un admin : le premier admin est créé au démarrage par la configuration Bootstrap.</summary>
public abstract class AdminApiTestBase(SqlServerFixture sql) : ApiTestBase(sql)
{
    protected const string AdminPassword = "AdminTest2026";

    protected string AdminEmail { get; } = NewEmail("admin");

    protected override Dictionary<string, string?>? ConfigurationOverrides => new()
    {
        ["Bootstrap:AdminEmail"] = AdminEmail,
        ["Bootstrap:AdminPassword"] = AdminPassword,
        ["Bootstrap:AdminFirstName"] = "Admin",
        ["Bootstrap:AdminLastName"] = "Association",
        ["Bootstrap:AdminPhone"] = "+216 71 000 000",
    };

    protected async Task<string> AdminTokenAsync()
    {
        var response = await LoginAsync(AdminEmail, AdminPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    // async + await : le « using » ne doit libérer la requête (et son contenu) qu'une fois la réponse reçue.
    protected async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    /// <summary>Corps de création ou de modification ; dates en heure de Tunis (+01:00), relatives à maintenant.</summary>
    protected static Dictionary<string, object?> EventBody(
        int daysFromNow = 10,
        int capacity = 30,
        decimal? price = 12.5m,
        string title = "Atelier couture",
        string? version = null)
    {
        var start = DateTimeOffset.UtcNow.AddDays(daysFromNow).ToOffset(TimeSpan.FromHours(1));
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["description"] = "Initiation, matériel fourni.",
            ["location"] = "Local de l'association, Tunis",
            ["startsAt"] = start.ToString("yyyy-MM-dd'T'HH:mmzzz", System.Globalization.CultureInfo.InvariantCulture),
            ["endsAt"] = start.AddHours(2).ToString("yyyy-MM-dd'T'HH:mmzzz", System.Globalization.CultureInfo.InvariantCulture),
            ["capacity"] = capacity,
            ["price"] = price,
        };
        if (version is not null)
        {
            body["version"] = version;
        }

        return body;
    }

    /// <summary>Corps avec des instants explicites (heure de Tunis, +01:00), pour les tests à horloge simulée.</summary>
    protected static Dictionary<string, object?> EventBodyAt(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        string title = "Atelier couture",
        string? version = null,
        int capacity = 30)
    {
        var body = EventBody(capacity: capacity, title: title, version: version);
        body["startsAt"] = Iso(startsAt);
        body["endsAt"] = Iso(endsAt);
        return body;
    }

    protected static string Iso(DateTimeOffset value) =>
        value.ToOffset(TimeSpan.FromHours(1)).ToString("yyyy-MM-dd'T'HH:mmzzz", System.Globalization.CultureInfo.InvariantCulture);

    protected async Task<Guid> CreatePublishedEventAsync(string token, Dictionary<string, object?>? body = null)
    {
        var id = (await CreateEventAsync(token, body)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/publish", token)).StatusCode);
        return id;
    }

    protected async Task<JsonElement> CreateEventAsync(string token, Dictionary<string, object?>? body = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/admin/events", token, body ?? EventBody());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }
}
