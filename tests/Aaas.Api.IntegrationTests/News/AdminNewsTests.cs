using System.Net;
using System.Text.Json;
using Aaas.Api.IntegrationTests.Support;
using Microsoft.Data.SqlClient;

namespace Aaas.Api.IntegrationTests.News;

/// <summary>Gestion des actualités par un admin (US-E1, ADR 0022, 0023).</summary>
public sealed class AdminNewsTests(SqlServerFixture sql) : NewsApiTestBase(sql)
{
    [Fact(DisplayName = "US-E1 : création → 201, Location, Brouillon, version, no-store ; absente du public")]
    public async Task Create_draft()
    {
        var admin = await AdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, NewsBody("  Assemblée générale  ", "Ligne 1\r\nLigne 2"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await ReadJsonAsync(response);
        var id = json.GetProperty("id").GetGuid();
        Assert.Equal($"/api/admin/news/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Draft", json.GetProperty("status").GetString());
        Assert.Equal("Assemblée générale", json.GetProperty("title").GetString());
        Assert.Equal("Ligne 1\nLigne 2", json.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("publishedAt").ValueKind);
        Assert.False(string.IsNullOrEmpty(json.GetProperty("version").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/news/{id}", null)).StatusCode);
        Assert.Equal(0, (await PublicListAsync()).GetProperty("totalCount").GetInt32());
    }

    [Fact(DisplayName = "US-E1 : publier (date simulée), modifier une publiée (visible tout de suite, date conservée), dépublier, republier (date d'origine)")]
    public async Task Full_lifecycle()
    {
        var admin = await AdminTokenAsync();
        var id = (await CreateNewsAsync(admin)).GetProperty("id").GetGuid();
        Clock.Advance(TimeSpan.FromHours(1));
        var publishedAt = Clock.GetUtcNow();

        var publish = await ReadJsonAsync(await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin));
        Assert.Equal("Published", publish.GetProperty("status").GetString());
        Assert.Equal(publishedAt, publish.GetProperty("publishedAt").GetDateTimeOffset());

        Clock.Advance(TimeSpan.FromHours(1));
        var update = await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin,
            NewsBody("Titre corrigé", "Texte corrigé", publish.GetProperty("version").GetString()));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadJsonAsync(update);
        Assert.Equal(publishedAt, updated.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.Equal(Clock.GetUtcNow(), updated.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.NotEqual(publish.GetProperty("version").GetString(), updated.GetProperty("version").GetString());
        var detail = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/news/{id}", null));
        Assert.Equal("Texte corrigé", detail.GetProperty("content").GetString());

        var unpublish = await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/unpublish", admin);
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        Assert.Equal("Draft", (await ReadJsonAsync(unpublish)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/news/{id}", null)).StatusCode);

        Clock.Advance(TimeSpan.FromDays(2));
        var republish = await ReadJsonAsync(await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin));
        Assert.Equal(publishedAt, republish.GetProperty("publishedAt").GetDateTimeOffset());
    }

    [Fact(DisplayName = "US-E1 : suppression d'une publiée → 204, puis admin 404, public 404, second DELETE 404")]
    public async Task Delete()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin);

        var delete = await SendAsync(HttpMethod.Delete, $"/api/admin/news/{id}", admin);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.True(delete.Headers.CacheControl?.NoStore);
        var get = await SendAsync(HttpMethod.Get, $"/api/admin/news/{id}", admin);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal("Actualité introuvable.", (await ReadJsonAsync(get)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/news/{id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Delete, $"/api/admin/news/{id}", admin)).StatusCode);
        Assert.Equal(0, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM News"));
    }

    [Fact(DisplayName = "US-E1 : titre et texte manquants → 400 errors.title et errors.content (messages français)")]
    public async Task Missing_fields()
    {
        var admin = await AdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, new { title = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.Equal("Le titre est obligatoire.", errors.GetProperty("title")[0].GetString());
        Assert.Equal("Le texte est obligatoire.", errors.GetProperty("content")[0].GetString());
    }

    [Fact(DisplayName = "Limites : texte de 8 001 → 400 ; 8 000 écrit avec des \\r\\n (9 999 bruts) → 201 ; titre de 201 → 400")]
    public async Task Length_limits()
    {
        var admin = await AdminTokenAsync();

        var tooLong = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, NewsBody("T", new string('a', 8001)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("Le texte ne doit pas dépasser 8 000 caractères.",
            (await ReadJsonAsync(tooLong)).GetProperty("errors").GetProperty("content")[0].GetString());

        var crlf = string.Concat(Enumerable.Repeat("abc\r\n", 1999)) + "abcd";
        var accepted = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, NewsBody("T", crlf));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(8000, (await ReadJsonAsync(accepted)).GetProperty("content").GetString()!.Length);

        var longTitle = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, NewsBody(new string('t', 201), "x"));
        Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);
        Assert.True((await ReadJsonAsync(longTitle)).GetProperty("errors").TryGetProperty("title", out _));
    }

    [Fact(DisplayName = "XSS : <script> et <img onerror> stockés et renvoyés À L'IDENTIQUE (aucun encodage serveur), en application/json")]
    public async Task Malicious_text_is_returned_verbatim()
    {
        const string title = "<b>Titre</b> & \"guillemets\"";
        const string content = "<script>alert(1)</script><img src=x onerror=alert(1)>\n\n<a href=\"javascript:alert(1)\">lien</a> &amp;";
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin, title, content);

        Assert.Equal(content, await Factory.ScalarAsync<string>("SELECT Content FROM News WHERE Id = @id", ("@id", id)));
        var response = await SendAsync(HttpMethod.Get, $"/api/news/{id}", null);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var detail = await ReadJsonAsync(response);
        Assert.Equal(title, detail.GetProperty("title").GetString());
        Assert.Equal(content, detail.GetProperty("content").GetString());
        var admins = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/news/{id}", admin));
        Assert.Equal(content, admins.GetProperty("content").GetString());
    }

    [Fact(DisplayName = "Concurrence : PUT avec une ancienne version → 409 concurrent_update, rien n'est écrit")]
    public async Task Stale_version()
    {
        var admin = await AdminTokenAsync();
        var created = await CreateNewsAsync(admin);
        var id = created.GetProperty("id").GetGuid();
        var oldVersion = created.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("Première modification", "x", oldVersion))).StatusCode);

        var stale = await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("Seconde", "y", oldVersion));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrent_update", (await ReadJsonAsync(stale)).GetProperty("code").GetString());
        Assert.Equal("Première modification", await Factory.ScalarAsync<string>("SELECT Title FROM News WHERE Id = @id", ("@id", id)));
    }

    [Fact(DisplayName = "Concurrence : version périmée ET saisie invalide → 409 d'abord (pas de 400 sur une fiche périmée)")]
    public async Task Stale_version_wins_over_validation()
    {
        var admin = await AdminTokenAsync();
        var created = await CreateNewsAsync(admin);
        var id = created.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin);

        var response = await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin,
            NewsBody("T", new string('a', 8001), created.GetProperty("version").GetString()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact(DisplayName = "Concurrence : deux PUT simultanés avec la même version → un 200 et un 409 concurrent_update")]
    [Trait("Category", "Concurrency")]
    public async Task Concurrent_updates()
    {
        var admin = await AdminTokenAsync();
        var created = await CreateNewsAsync(admin);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("Admin A", "a", version)),
            SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("Admin B", "b", version)));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("concurrent_update", (await ReadJsonAsync(conflict)).GetProperty("code").GetString());
    }

    [Fact(DisplayName = "Transitions : publier deux fois → 409 news_state_changed ; dépublier un brouillon → 409 ; inconnue → 404")]
    public async Task Invalid_transitions()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin);

        var twice = await ReadJsonAsync(await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin));
        Assert.Equal("news_state_changed", twice.GetProperty("code").GetString());
        Assert.Equal("Cette actualité est déjà publiée.", twice.GetProperty("title").GetString());

        await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/unpublish", admin);
        var draft = await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/unpublish", admin);
        Assert.Equal(HttpStatusCode.Conflict, draft.StatusCode);
        Assert.Equal("Cette actualité n'est pas publiée.", (await ReadJsonAsync(draft)).GetProperty("title").GetString());

        var unknown = await SendAsync(HttpMethod.Post, $"/api/admin/news/{Guid.NewGuid()}/publish", admin);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact(DisplayName = "Liste admin : toutes, dernière modifiée d'abord, filtre status, 20 par défaut, sans le texte")]
    public async Task Admin_list()
    {
        var admin = await AdminTokenAsync();
        var draft = (await CreateNewsAsync(admin, "Brouillon")).GetProperty("id").GetGuid();
        Clock.Advance(TimeSpan.FromMinutes(1));
        var published = await CreatePublishedNewsAsync(admin, "Publiée");

        var all = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/news", admin));
        Assert.Equal(20, all.GetProperty("pageSize").GetInt32());
        Assert.Equal([published, draft], Ids(all));
        Assert.False(all.GetProperty("items")[0].TryGetProperty("content", out _));

        var drafts = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/news?status=Draft", admin));
        Assert.Equal([draft], Ids(drafts));
        Assert.Equal(JsonValueKind.Null, drafts.GetProperty("items")[0].GetProperty("publishedAt").ValueKind);
        Assert.Equal([published], Ids(await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/news?status=Published", admin))));
    }

    [Theory(DisplayName = "Schéma : écriture SQL directe hors règles refusée (CHECK, erreur 547)")]
    [InlineData("Status = 3", "CK_News_Status")]
    [InlineData("Status = 0", "CK_News_Status")]
    [InlineData("Status = 2, PublishedAt = NULL", "CK_News_PublishedAt")]
    public async Task Check_constraints(string assignment, string constraint)
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin);

        var error = await Assert.ThrowsAsync<SqlException>(() =>
            Factory.ExecuteAsync($"UPDATE News SET {assignment} WHERE Id = @id", ("@id", id)));

        Assert.Equal(547, error.Number);
        Assert.Contains(constraint, error.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Schéma : un brouillon peut garder une date (déjà publié puis dépublié) : accepté par CK_News_PublishedAt")]
    public async Task Draft_with_date_is_allowed()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin);

        Assert.Equal(1, await Factory.ExecuteAsync("UPDATE News SET Status = 1 WHERE Id = @id", ("@id", id)));
    }

    [Fact(DisplayName = "Journaux : ni le titre ni le texte d'une actualité n'apparaissent (seulement des identifiants)")]
    public async Task Logs_do_not_contain_text()
    {
        const string title = "Titre-Secret-7c1f";
        const string content = "Texte-Secret-9b2e avec des détails.";
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin, title, content);
        var version = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/news/{id}", admin))).GetProperty("version").GetString();
        await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody(title + "-2", content + " suite", version));
        await PublicListAsync();
        await SendAsync(HttpMethod.Get, $"/api/news/{id}", null);
        await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody(title, content, version)); // 409
        await SendAsync(HttpMethod.Delete, $"/api/admin/news/{id}", admin);

        var logs = string.Join("\n", Factory.Logs.Messages);
        Assert.NotEmpty(Factory.Logs.Messages);
        Assert.DoesNotContain("Secret-7c1f", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret-9b2e", logs, StringComparison.Ordinal);
    }
}
