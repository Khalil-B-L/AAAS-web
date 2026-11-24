using System.Net;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Events;

/// <summary>
/// Identifiant qui n'est pas un GUID (écart D1) : la route publique répond comme pour un identifiant inconnu (404),
/// les routes admin gardent leur contrôle de rôle (visiteur 401, membre 403) et seul un admin reçoit 404.
/// </summary>
public sealed class MalformedIdTests(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    public static TheoryData<string> MalformedIds => new() { "abc", "123", "not-a-guid-at-all" };

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/events/abc" },
        { "PUT", "/api/admin/events/abc" },
        { "DELETE", "/api/admin/events/abc" },
        { "POST", "/api/admin/events/abc/publish" },
        { "POST", "/api/admin/events/abc/unpublish" },
    };

    [Theory(DisplayName = "Visiteur : GET /api/events/<pas un GUID> répond 404 not_found, comme un identifiant inconnu")]
    [MemberData(nameof(MalformedIds))]
    public async Task Visitor_gets_same_404_as_unknown_id(string id)
    {
        var unknown = await SendAsync(HttpMethod.Get, $"/api/events/{Guid.NewGuid()}", null);
        var malformed = await SendAsync(HttpMethod.Get, $"/api/events/{id}", null);

        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        var expected = await ReadJsonAsync(unknown);
        var actual = await ReadJsonAsync(malformed);
        Assert.Equal("not_found", actual.GetProperty("code").GetString());
        Assert.Equal(expected.GetProperty("code").GetString(), actual.GetProperty("code").GetString());
        Assert.Equal(expected.GetProperty("title").GetString(), actual.GetProperty("title").GetString());
    }

    [Fact(DisplayName = "Membre connecté : GET /api/events/abc répond aussi 404 not_found")]
    public async Task Member_gets_404_with_code()
    {
        var member = (await RegisterMemberAsync()).Token;

        var response = await SendAsync(HttpMethod.Get, "/api/events/abc", member);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact(DisplayName = "Un GUID valide est toujours servi par la route {id:guid} (événement publié → 200)")]
    public async Task Valid_guid_still_reaches_detail()
    {
        var id = await CreatePublishedEventAsync(await AdminTokenAsync());

        var response = await SendAsync(HttpMethod.Get, $"/api/events/{id}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory(DisplayName = "Visiteur : une écriture publique sur /api/events/abc reste refusée (401), rien n'est ouvert")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Visitor_cannot_write_on_public_malformed_route(string method)
    {
        var response = await SendAsync(new HttpMethod(method), "/api/events/abc", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory(DisplayName = "Visiteur : route admin avec identifiant mal formé → 401")]
    [MemberData(nameof(AdminRoutes))]
    public async Task Visitor_gets_401_on_admin_route(string method, string route)
    {
        var response = await SendAsync(new HttpMethod(method), route, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory(DisplayName = "Membre : route admin avec identifiant mal formé → 403 (pas de 404 qui renseignerait avant le refus)")]
    [MemberData(nameof(AdminRoutes))]
    public async Task Member_gets_403_on_admin_route(string method, string route)
    {
        var member = (await RegisterMemberAsync()).Token;

        var response = await SendAsync(new HttpMethod(method), route, member, method == "PUT" ? EventBody() : null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory(DisplayName = "Admin : route admin avec identifiant mal formé → 404 not_found « Événement introuvable. »")]
    [MemberData(nameof(AdminRoutes))]
    public async Task Admin_gets_404_on_admin_route(string method, string route)
    {
        var admin = await AdminTokenAsync();
        var eventsBefore = await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Events");

        var response = await SendAsync(new HttpMethod(method), route, admin, method == "PUT" ? EventBody(version: "AAAAAAAAB9E=") : null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("not_found", json.GetProperty("code").GetString());
        Assert.Equal("Événement introuvable.", json.GetProperty("title").GetString());
        Assert.Equal(eventsBefore, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Events"));
    }
}
