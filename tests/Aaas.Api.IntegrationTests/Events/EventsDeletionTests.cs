using System.Net;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Events;

/// <summary>Suppression d'un brouillon jamais publié (R15 étape 2, RG-07, FirstPublishedAt).</summary>
public sealed class EventsDeletionTests(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    [Fact(DisplayName = "Brouillon jamais publié : DELETE 204, la ligne disparaît, GET admin puis 404")]
    public async Task Never_published_draft_is_deleted()
    {
        var admin = await AdminTokenAsync();
        var id = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Delete, $"/api/admin/events/{id}", admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Events WHERE Id = @id", ("@id", id)));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/admin/events/{id}", admin)).StatusCode);
    }

    [Fact(DisplayName = "Publié puis dépublié : FirstPublishedAt conservé, canBeDeleted faux, DELETE 409 event_already_published, ligne intacte")]
    public async Task Published_then_unpublished_draft_cannot_be_deleted()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedEventAsync(admin);
        var unpublished = await ReadJsonAsync(await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/unpublish", admin));
        Assert.Equal("Draft", unpublished.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, unpublished.GetProperty("publishedAt").ValueKind);
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, unpublished.GetProperty("firstPublishedAt").ValueKind);
        Assert.False(unpublished.GetProperty("canBeDeleted").GetBoolean());

        var response = await SendAsync(HttpMethod.Delete, $"/api/admin/events/{id}", admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("event_already_published", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Events WHERE Id = @id", ("@id", id)));
    }

    [Fact(DisplayName = "Republier après dépublication : FirstPublishedAt reste la première date")]
    public async Task First_published_at_is_never_overwritten()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedEventAsync(admin);
        var first = await Factory.ScalarAsync<DateTimeOffset>("SELECT FirstPublishedAt FROM Events WHERE Id = @id", ("@id", id));
        await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/unpublish", admin);
        await Task.Delay(50);
        await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/publish", admin);

        var after = await Factory.ScalarAsync<DateTimeOffset>("SELECT FirstPublishedAt FROM Events WHERE Id = @id", ("@id", id));

        Assert.Equal(first, after);
    }

    [Fact(DisplayName = "Événement publié : DELETE 409 event_already_published")]
    public async Task Published_event_cannot_be_deleted()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedEventAsync(admin);

        var response = await SendAsync(HttpMethod.Delete, $"/api/admin/events/{id}", admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("event_already_published", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Theory(DisplayName = "Identifiant inconnu ou mal formé : DELETE 404")]
    [InlineData("00000000-0000-0000-0000-000000000042")]
    [InlineData("pas-un-guid")]
    public async Task Unknown_id_returns_404(string id)
    {
        var admin = await AdminTokenAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Delete, $"/api/admin/events/{id}", admin)).StatusCode);
    }

    [Fact(DisplayName = "Deux suppressions du même brouillon : la seconde reçoit 404")]
    public async Task Second_delete_returns_404()
    {
        var admin = await AdminTokenAsync();
        var id = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();

        await SendAsync(HttpMethod.Delete, $"/api/admin/events/{id}", admin);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Delete, $"/api/admin/events/{id}", admin)).StatusCode);
    }
}
