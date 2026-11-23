using System.Net;
using System.Text.Json;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Events;

/// <summary>
/// Tests de fumée de l'étape 2 écrits avec le code (expert-technique) : parcours nominal, version, messages
/// français de liaison (T8). La couverture complète du plan § 9.3 revient à l'agent testeur.
/// </summary>
public sealed class EventsSmokeTests(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    [Fact(DisplayName = "Parcours : créer (brouillon invisible), publier (avertissement contact), lister, détail, dépublier")]
    public async Task Create_publish_list_unpublish()
    {
        var admin = await AdminTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/admin/events", admin, EventBody(price: 0m));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJsonAsync(create);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/api/admin/events/{id}", create.Headers.Location?.OriginalString);
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("price").ValueKind); // 0 → gratuit
        Assert.EndsWith("+00:00", created.GetProperty("startsAt").GetString(), StringComparison.Ordinal);
        Assert.True(created.GetProperty("canBeDeleted").GetBoolean());

        // Brouillon : invisible du public (404, même réponse qu'un identifiant inconnu).
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/events/{id}", null)).StatusCode);

        // Publication sans numéro de contact : effectuée, avec l'avertissement non bloquant.
        var publish = await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/publish", admin);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        var published = await ReadJsonAsync(publish);
        Assert.Equal("Published", published.GetProperty("event").GetProperty("status").GetString());
        Assert.Equal("contact_not_configured", published.GetProperty("warnings")[0].GetProperty("code").GetString());

        var list = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/events?page=1&pageSize=20", null));
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        var item = list.GetProperty("items")[0];
        Assert.Equal(30, item.GetProperty("remainingSeats").GetInt32());
        Assert.True(item.GetProperty("isBookable").GetBoolean());
        Assert.False(item.GetProperty("hasEnded").GetBoolean());

        var detail = await SendAsync(HttpMethod.Get, $"/api/events/{id}", null);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal("Initiation, matériel fourni.", (await ReadJsonAsync(detail)).GetProperty("description").GetString());

        var republish = await ReadJsonAsync(await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/publish", admin));
        Assert.Equal("event_state_changed", republish.GetProperty("code").GetString());

        var unpublish = await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/unpublish", admin);
        Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/events", null))).GetProperty("totalCount").GetInt32());
    }

    [Fact(DisplayName = "Suppression : brouillon jamais publié 204 ; publié puis dépublié 409 event_already_published")]
    public async Task Delete_only_never_published_drafts()
    {
        var admin = await AdminTokenAsync();
        var draftId = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();
        var onceId = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, $"/api/admin/events/{onceId}/publish", admin);
        await SendAsync(HttpMethod.Post, $"/api/admin/events/{onceId}/unpublish", admin);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/admin/events/{draftId}", admin)).StatusCode);
        Assert.Equal(0, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Events WHERE Id = @id", ("@id", draftId)));

        var refused = await SendAsync(HttpMethod.Delete, $"/api/admin/events/{onceId}", admin);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("event_already_published", (await ReadJsonAsync(refused)).GetProperty("code").GetString());
    }

    // Ne prouve pas le verrou : la version seule suffit à refuser le second PUT, avec ou sans UPDLOCK.
    [Fact(DisplayName = "Version : deux PUT simultanés avec la même version → un 200 et un 409 concurrent_update")]
    public async Task Concurrent_updates_with_same_version()
    {
        var admin = await AdminTokenAsync();
        var created = await CreateEventAsync(admin);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Put, $"/api/admin/events/{id}", admin, EventBody(title: "Version A", capacity: 40, version: version)),
            SendAsync(HttpMethod.Put, $"/api/admin/events/{id}", admin, EventBody(title: "Version B", capacity: 50, version: version)));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order());
        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("concurrent_update", (await ReadJsonAsync(conflict)).GetProperty("code").GetString());
        var ok = await ReadJsonAsync(responses.Single(r => r.StatusCode == HttpStatusCode.OK));
        Assert.NotEqual(version, ok.GetProperty("version").GetString());
    }

    [Fact(DisplayName = "Numéro de contact : visiteur 401, admin enregistre un fixe, retire (WhatsApp à false), version périmée 409")]
    public async Task Contact_settings_lifecycle()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/settings/contact", null)).StatusCode);

        var admin = await AdminTokenAsync();
        var initial = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/settings/contact", admin));
        Assert.False(initial.GetProperty("isConfigured").GetBoolean());
        var v0 = initial.GetProperty("version").GetString();

        var put = await SendAsync(HttpMethod.Put, "/api/admin/settings/contact", admin,
            new { phoneNumber = "+216 71 123 456", whatsApp = true, version = v0 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = await ReadJsonAsync(put);
        Assert.Equal("+21671123456", saved.GetProperty("phoneNumber").GetString());

        // Sans corps ni Content-Type : 415 (ASP.NET Core refuse avant de lire le corps), complété en français.
        var unsupported = await SendAsync(HttpMethod.Delete, "/api/admin/settings/contact", admin);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupported.StatusCode);
        var unsupportedBody = await ReadJsonAsync(unsupported);
        Assert.Equal("unsupported_media_type", unsupportedBody.GetProperty("code").GetString());
        Assert.StartsWith("Le corps de la requête doit être envoyé en JSON", unsupportedBody.GetProperty("title").GetString(), StringComparison.Ordinal);

        // Content-Type JSON mais corps vide : 400 avec le message français de liaison (T8).
        using (var emptyBody = new HttpRequestMessage(HttpMethod.Delete, "/api/admin/settings/contact"))
        {
            emptyBody.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin);
            emptyBody.Content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");
            var noBody = await Client.SendAsync(emptyBody);
            Assert.Equal(HttpStatusCode.BadRequest, noBody.StatusCode);
            Assert.Contains("Le corps de la requête est obligatoire.", (await ReadJsonAsync(noBody)).GetRawText(), StringComparison.Ordinal);
        }

        // Ancienne version : le retrait est refusé, le numéro reste.
        var stale = await SendAsync(HttpMethod.Delete, "/api/admin/settings/contact", admin, new { version = v0 });
        Assert.Equal("concurrent_update", (await ReadJsonAsync(stale)).GetProperty("code").GetString());

        var removed = await SendAsync(HttpMethod.Delete, "/api/admin/settings/contact", admin,
            new { version = saved.GetProperty("version").GetString() });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var after = await ReadJsonAsync(removed);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("phoneNumber").ValueKind);
        Assert.False(after.GetProperty("whatsApp").GetBoolean());
        Assert.False(after.GetProperty("isConfigured").GetBoolean());
    }

    [Theory(DisplayName = "T8 : paramètres de requête invalides → 400 avec un message français sur le paramètre")]
    [InlineData("/api/events?page=abc", "page", "La valeur « abc » n'est pas valide.")]
    [InlineData("/api/events?pageSize=51", "pageSize", "Le nombre d'éléments par page doit être compris entre 1 et 50.")]
    [InlineData("/api/events?page=0", "page", "Le numéro de page doit être compris entre 1 et 10 000.")]
    public async Task Invalid_query_parameters_have_french_messages(string url, string field, string message)
    {
        var response = await SendAsync(HttpMethod.Get, url, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        var actual = errors.EnumerateObject().Single();
        Assert.Equal(field, actual.Name);
        Assert.Equal(message, actual.Value[0].GetString());
    }

    [Theory(DisplayName = "Date sans décalage ou impossible → 400 sur le champ, message général (format + fuseau)")]
    [InlineData("2026-12-05T10:00")]
    [InlineData("2026-02-30T10:00+01:00")]
    public async Task Date_without_offset_is_400_on_the_field(string startsAt)
    {
        var admin = await AdminTokenAsync();
        var body = EventBody();
        body["startsAt"] = startsAt;

        var response = await SendAsync(HttpMethod.Post, "/api/admin/events", admin, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        var errors = problem.GetProperty("errors");
        Assert.Equal(
            "La date est invalide : format attendu AAAA-MM-JJTHH:mm avec le fuseau horaire, par exemple 2026-03-10T10:00+01:00.",
            errors.GetProperty("startsAt")[0].GetString());
        Assert.False(errors.TryGetProperty("endsAt", out _));
    }

    [Fact(DisplayName = "GET /api/events (anonyme) avec un jeton invalide → 200, le jeton est ignoré")]
    public async Task Public_list_ignores_invalid_token()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/events", "jeton.invalide.signature");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await ReadJsonAsync(response)).GetProperty("items").ValueKind);
    }

    [Fact(DisplayName = "Méthode non prise en charge (GET sur /publish) → 405 method_not_allowed en français")]
    public async Task Wrong_method_is_405_with_code()
    {
        // Avec jeton : l'endpoint 405 du routage n'a pas de [AllowAnonymous], la politique par défaut (jeton
        // obligatoire) passe d'abord ; sans jeton, la réponse serait 401.
        var admin = await AdminTokenAsync();
        var response = await SendAsync(HttpMethod.Get, $"/api/admin/events/{Guid.NewGuid()}/publish", admin);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("method_not_allowed", problem.GetProperty("code").GetString());
        Assert.Equal("Cette action n'est pas possible sur cette adresse.", problem.GetProperty("title").GetString());
    }

    [Fact(DisplayName = "T8 : filtre admin inconnu (period=demain) → 400 en français ; filtres valides acceptés")]
    public async Task Admin_filters()
    {
        var admin = await AdminTokenAsync();

        var invalid = await SendAsync(HttpMethod.Get, "/api/admin/events?period=demain", admin);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("n'est pas valide", (await ReadJsonAsync(invalid)).GetRawText(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/admin/events?status=Draft&period=past", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/admin/events?status=draft&period=all", admin)).StatusCode);
    }
}
