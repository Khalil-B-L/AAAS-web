using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;

namespace Aaas.Api.IntegrationTests.Support;

/// <summary>
/// Base des tests d'actualités : un admin, et une horloge simulée qui n'avance que si le test le demande
/// (deux publications sans Advance ont exactement le même PublishedAt : utile pour tester le tri stable par Id).
/// </summary>
public abstract class NewsApiTestBase(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    private FakeTimeProvider? _clock;

    protected DateTimeOffset Start { get; } = DateTimeOffset.UtcNow;

    protected override TimeProvider? ClockOverride => _clock ??= new FakeTimeProvider(Start);

    protected FakeTimeProvider Clock => (FakeTimeProvider)ClockOverride!;

    protected static object NewsBody(string title = "Assemblée générale", string content = "Chers membres,\n\nL'assemblée générale aura lieu samedi.", string? version = null) =>
        version is null ? new { title, content } : new { title, content, version };

    protected async Task<JsonElement> CreateNewsAsync(string token, string title = "Assemblée générale", string? content = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/admin/news", token,
            content is null ? NewsBody(title) : NewsBody(title, content));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    protected async Task<Guid> CreatePublishedNewsAsync(string token, string title = "Assemblée générale", string? content = null)
    {
        var id = (await CreateNewsAsync(token, title, content)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", token)).StatusCode);
        return id;
    }

    protected async Task<JsonElement> PublicListAsync(string query = "")
    {
        var response = await SendAsync(HttpMethod.Get, "/api/news" + query, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    protected static List<Guid> Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];
}
