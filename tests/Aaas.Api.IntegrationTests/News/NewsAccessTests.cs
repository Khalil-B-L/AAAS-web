using System.Net;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.News;

/// <summary>
/// Accès aux routes admin des actualités (les 7 + les routes de repli pour un identifiant mal formé) :
/// visiteur 401, membre 403, admin 404 sur un id mal formé ; Cache-Control: no-store sur toutes les réponses admin.
/// </summary>
public sealed class NewsAccessTests(SqlServerFixture sql) : NewsApiTestBase(sql)
{
    private const string AnyVersion = "AAAAAAAAB9E=";

    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/news" },
        { "POST", "/api/admin/news" },
        { "GET", $"/api/admin/news/{Guid.Empty}" },
        { "PUT", $"/api/admin/news/{Guid.Empty}" },
        { "DELETE", $"/api/admin/news/{Guid.Empty}" },
        { "POST", $"/api/admin/news/{Guid.Empty}/publish" },
        { "POST", $"/api/admin/news/{Guid.Empty}/unpublish" },
        { "GET", "/api/admin/news/abc" },
        { "PUT", "/api/admin/news/abc" },
        { "DELETE", "/api/admin/news/abc" },
        { "POST", "/api/admin/news/abc/publish" },
        { "POST", "/api/admin/news/abc/unpublish" },
    };

    public static TheoryData<string, string> MalformedAdminRoutes => new()
    {
        { "GET", "/api/admin/news/abc" },
        { "PUT", "/api/admin/news/abc" },
        { "DELETE", "/api/admin/news/abc" },
        { "POST", "/api/admin/news/abc/publish" },
        { "POST", "/api/admin/news/abc/unpublish" },
    };

    private static object? BodyFor(string method) => method is "PUT" or "POST" ? NewsBody(version: AnyVersion) : null;

    [Theory(DisplayName = "Visiteur : 401 sur toutes les routes admin, avec no-store")]
    [MemberData(nameof(AdminRoutes))]
    public async Task Visitor_gets_401(string method, string route)
    {
        var response = await SendAsync(new HttpMethod(method), route, null, BodyFor(method));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory(DisplayName = "Membre : 403 sur toutes les routes admin (même id mal formé), avec no-store, rien n'est écrit")]
    [MemberData(nameof(AdminRoutes))]
    public async Task Member_gets_403(string method, string route)
    {
        var member = (await RegisterMemberAsync()).Token;

        var response = await SendAsync(new HttpMethod(method), route, member, BodyFor(method));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(0, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM News"));
    }

    [Theory(DisplayName = "Admin : identifiant mal formé → 404 « Actualité introuvable. », avec no-store")]
    [MemberData(nameof(MalformedAdminRoutes))]
    public async Task Admin_gets_404_on_malformed_id(string method, string route)
    {
        var admin = await AdminTokenAsync();

        var response = await SendAsync(new HttpMethod(method), route, admin, BodyFor(method));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await ReadJsonAsync(response);
        Assert.Equal("not_found", json.GetProperty("code").GetString());
        Assert.Equal("Actualité introuvable.", json.GetProperty("title").GetString());
    }

    [Fact(DisplayName = "no-store sur une erreur 400 admin ; absent des réponses publiques réussies (200)")]
    public async Task No_store_only_on_admin()
    {
        var admin = await AdminTokenAsync();
        var bad = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, new { });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.True(bad.Headers.CacheControl?.NoStore);

        var id = await CreatePublishedNewsAsync(admin);
        Assert.NotEqual(true, (await SendAsync(HttpMethod.Get, "/api/news", null)).Headers.CacheControl?.NoStore);
        Assert.NotEqual(true, (await SendAsync(HttpMethod.Get, $"/api/news/{id}", null)).Headers.CacheControl?.NoStore);
        // Un 404 public, lui, porte « no-cache,no-store » : c'est l'ExceptionHandlerMiddleware d'ASP.NET Core qui vide
        // les en-têtes de cache de toute réponse d'erreur (comportement du framework, sans conséquence ici).
    }

    [Theory(DisplayName = "Visiteur : une écriture sur /api/news/abc reste refusée (401), rien n'est ouvert")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Visitor_cannot_write_on_public_route(string method)
    {
        var response = await SendAsync(new HttpMethod(method), "/api/news/abc", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
