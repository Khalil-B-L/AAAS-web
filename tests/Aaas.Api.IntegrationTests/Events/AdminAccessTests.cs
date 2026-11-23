using System.Net;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Events;

/// <summary>Autorisation par rôle sur toutes les routes admin de l'étape 2 : visiteur 401, membre 403, rien n'est modifié.</summary>
public sealed class AdminAccessTests(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    public static TheoryData<string, string> AdminRoutes => new()
    {
        { "GET", "/api/admin/events" },
        { "GET", "/api/admin/events/{id}" },
        { "POST", "/api/admin/events" },
        { "PUT", "/api/admin/events/{id}" },
        { "POST", "/api/admin/events/{id}/publish" },
        { "POST", "/api/admin/events/{id}/unpublish" },
        { "DELETE", "/api/admin/events/{id}" },
        { "PUT", "/api/admin/settings/contact" },
        { "DELETE", "/api/admin/settings/contact" },
    };

    [Theory(DisplayName = "Membre (rôle User) : toute route admin répond 403 et aucune donnée n'est modifiée")]
    [MemberData(nameof(AdminRoutes))]
    public async Task Member_gets_403_and_nothing_changes(string method, string route)
    {
        var admin = await AdminTokenAsync();
        var member = (await RegisterMemberAsync()).Token;
        var created = await CreateEventAsync(admin);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();
        var settingsVersion = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/settings/contact", admin))).GetProperty("version").GetString();
        var eventsBefore = await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Events");

        var response = await SendAsync(new HttpMethod(method), route.Replace("{id}", id.ToString()), member, BodyFor(method, route, version, settingsVersion));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(eventsBefore, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Events"));
        Assert.Equal("Atelier couture", await Factory.ScalarAsync<string>("SELECT Title FROM Events WHERE Id = @id", ("@id", id)));
        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT CAST(Status AS int) FROM Events WHERE Id = @id", ("@id", id))); // toujours brouillon
        Assert.Null(await Factory.ScalarAsync<string>("SELECT ContactPhone FROM AssociationSettings"));
    }

    [Theory(DisplayName = "Visiteur sans jeton : toute route admin répond 401")]
    [MemberData(nameof(AdminRoutes))]
    public async Task Visitor_gets_401(string method, string route)
    {
        var response = await SendAsync(new HttpMethod(method), route.Replace("{id}", Guid.NewGuid().ToString()), null, BodyFor(method, route, "AAAAAAAAB9E=", "AAAAAAAAB9E="));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Membre : un DELETE de numéro de contact 403 laisse le numéro enregistré")]
    public async Task Member_cannot_remove_contact_number()
    {
        var admin = await AdminTokenAsync();
        var member = (await RegisterMemberAsync()).Token;
        var v0 = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/settings/contact", admin))).GetProperty("version").GetString();
        var saved = await ReadJsonAsync(await SendAsync(HttpMethod.Put, "/api/admin/settings/contact", admin,
            new { phoneNumber = "+216 20 123 456", whatsApp = true, version = v0 }));

        var response = await SendAsync(HttpMethod.Delete, "/api/admin/settings/contact", member, new { version = saved.GetProperty("version").GetString() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("+21620123456", await Factory.ScalarAsync<string>("SELECT ContactPhone FROM AssociationSettings"));
    }

    private static Dictionary<string, object?>? BodyFor(string method, string route, string? eventVersion, string? settingsVersion)
    {
        if (route.StartsWith("/api/admin/settings/contact", StringComparison.Ordinal))
        {
            return method == "PUT"
                ? new() { ["phoneNumber"] = "+216 20 123 456", ["whatsApp"] = true, ["version"] = settingsVersion }
                : new() { ["version"] = settingsVersion };
        }

        return method switch
        {
            "POST" when route == "/api/admin/events" => EventBody(title: "Créé par un intrus"),
            "PUT" => EventBody(title: "Modifié par un intrus", version: eventVersion),
            _ => null,
        };
    }
}
