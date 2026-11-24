using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Aaas.Api.IntegrationTests.Support;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aaas.Api.IntegrationTests.News;

/// <summary>Consultation publique des actualités (US-B1, US-B2), anonyme.</summary>
public sealed class PublicNewsTests(SqlServerFixture sql) : NewsApiTestBase(sql)
{
    // Commandes SQL journalisées (sans valeurs de paramètres) : pour vérifier la projection SUBSTRING de la liste.
    protected override Dictionary<string, string?>? ConfigurationOverrides => new(base.ConfigurationOverrides!)
    {
        ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Information",
    };

    [Fact(DisplayName = "US-B1 : 3 publiées + 1 brouillon → 3, la plus récente d'abord, avec extrait et sans no-store")]
    public async Task Lists_published_newest_first()
    {
        var admin = await AdminTokenAsync();
        var first = await CreatePublishedNewsAsync(admin, "Première");
        Clock.Advance(TimeSpan.FromHours(1));
        var second = await CreatePublishedNewsAsync(admin, "Deuxième");
        Clock.Advance(TimeSpan.FromHours(1));
        var third = await CreatePublishedNewsAsync(admin, "Troisième");
        await CreateNewsAsync(admin, "Brouillon");

        var response = await SendAsync(HttpMethod.Get, "/api/news", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(true, response.Headers.CacheControl?.NoStore);
        var page = await ReadJsonAsync(response);

        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(10, page.GetProperty("pageSize").GetInt32());
        Assert.Equal([third, second, first], Ids(page));
        var item = page.GetProperty("items")[0];
        Assert.Equal("Troisième", item.GetProperty("title").GetString());
        Assert.Equal("Chers membres, L'assemblée générale aura lieu samedi.", item.GetProperty("excerpt").GetString());
        Assert.False(item.TryGetProperty("content", out _));
    }

    [Fact(DisplayName = "US-B1 : 25 publiées → page 1 = 10 (totalCount 25), page 3 = 5, page 4 = 200 vide")]
    public async Task Pagination()
    {
        var admin = await AdminTokenAsync();
        for (var i = 0; i < 25; i++)
        {
            Clock.Advance(TimeSpan.FromMinutes(1));
            await CreatePublishedNewsAsync(admin, $"Actualité {i}");
        }

        var page1 = await PublicListAsync("?page=1");
        Assert.Equal(25, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(10, page1.GetProperty("items").GetArrayLength());
        Assert.Equal("Actualité 24", page1.GetProperty("items")[0].GetProperty("title").GetString());

        Assert.Equal(5, (await PublicListAsync("?page=3")).GetProperty("items").GetArrayLength());

        var page4 = await PublicListAsync("?page=4");
        Assert.Equal(0, page4.GetProperty("items").GetArrayLength());
        Assert.Equal(25, page4.GetProperty("totalCount").GetInt32());
    }

    [Fact(DisplayName = "US-B1 : même PublishedAt → ordre stable par Id, identique sur deux appels et sans doublon entre pages")]
    public async Task Stable_order_on_same_date()
    {
        var admin = await AdminTokenAsync();
        for (var i = 0; i < 6; i++)
        {
            await CreatePublishedNewsAsync(admin, $"Même instant {i}"); // horloge simulée immobile
        }

        var a1 = Ids(await PublicListAsync("?pageSize=3&page=1"));
        var a2 = Ids(await PublicListAsync("?pageSize=3&page=2"));
        var b1 = Ids(await PublicListAsync("?pageSize=3&page=1"));
        var b2 = Ids(await PublicListAsync("?pageSize=3&page=2"));

        Assert.Equal(a1, b1);
        Assert.Equal(a2, b2);
        Assert.Equal(6, a1.Concat(a2).Distinct().Count());
    }

    [Fact(DisplayName = "US-B1 : aucune actualité → totalCount 0, items vide")]
    public async Task Empty_list()
    {
        var page = await PublicListAsync();

        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
    }

    [Theory(DisplayName = "US-B1 : pagination invalide → 400 sur le champ (camelCase)")]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=51", "pageSize")]
    public async Task Invalid_paging(string query, string field)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/news" + query, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact(DisplayName = "Extrait : texte long coupé sur un mot + « … », lu par SUBSTRING (le texte entier ne transite pas)")]
    public async Task Long_text_excerpt()
    {
        var admin = await AdminTokenAsync();
        var content = string.Join(' ', Enumerable.Repeat("mot", 2000)); // 7 999 caractères
        await CreatePublishedNewsAsync(admin, "Long", content);
        var before = Factory.Logs.Messages.Count;

        var excerpt = (await PublicListAsync()).GetProperty("items")[0].GetProperty("excerpt").GetString()!;

        Assert.EndsWith("…", excerpt, StringComparison.Ordinal);
        Assert.True(excerpt.Length <= 201);
        Assert.EndsWith("mot…", excerpt, StringComparison.Ordinal);
        var logs = Factory.Logs.Messages.Skip(before).ToList();
        var sql = string.Join("\n", logs.Where(m => m.Contains("Executed DbCommand", StringComparison.Ordinal) && m.Contains("[News]", StringComparison.Ordinal)));
        Assert.True(sql.Length > 0, "Journaux : " + string.Join("\n---\n", logs));
        Assert.Contains("SUBSTRING(", sql, StringComparison.Ordinal);
        Assert.Contains("401)", sql, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "US-B2 : publiée → 200, texte complet avec ses lignes vides, date en UTC")]
    public async Task Detail_of_published()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin);

        var response = await SendAsync(HttpMethod.Get, $"/api/news/{id}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("Chers membres,\n\nL'assemblée générale aura lieu samedi.", json.GetProperty("content").GetString());
        Assert.EndsWith("+00:00", json.GetProperty("publishedAt").GetString(), StringComparison.Ordinal);
        Assert.False(json.TryGetProperty("status", out _));
        Assert.False(json.TryGetProperty("version", out _));
    }

    [Fact(DisplayName = "US-B2 : brouillon, dépubliée, inconnue et /api/news/abc → même 404 anonyme (code et title identiques)")]
    public async Task Same_404_for_draft_unpublished_unknown_and_malformed()
    {
        var admin = await AdminTokenAsync();
        var draft = (await CreateNewsAsync(admin)).GetProperty("id").GetGuid();
        var unpublished = await CreatePublishedNewsAsync(admin);
        await SendAsync(HttpMethod.Post, $"/api/admin/news/{unpublished}/unpublish", admin);

        string[] urls = [$"/api/news/{draft}", $"/api/news/{unpublished}", $"/api/news/{Guid.NewGuid()}", "/api/news/abc", "/api/news/123"];
        var bodies = new List<(string? Code, string? Title)>();
        foreach (var url in urls)
        {
            var response = await SendAsync(HttpMethod.Get, url, null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var json = await ReadJsonAsync(response);
            bodies.Add((json.GetProperty("code").GetString(), json.GetProperty("title").GetString()));
        }

        Assert.All(bodies, b => Assert.Equal(("not_found", "Contenu introuvable."), b));
        Assert.Equal(0, (await PublicListAsync()).GetProperty("totalCount").GetInt32());
    }

    [Fact(DisplayName = "Une dépubliée garde sa date mais disparaît de la liste (filtre sur le statut, pas sur PublishedAt)")]
    public async Task Unpublished_is_hidden_despite_its_date()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin);
        await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/unpublish", admin);

        Assert.NotNull(await Factory.ScalarAsync<DateTimeOffset?>("SELECT PublishedAt FROM News WHERE Id = @id", ("@id", id)));
        Assert.Equal(0, (await PublicListAsync()).GetProperty("totalCount").GetInt32());
    }

    [Fact(DisplayName = "Visiteur avec un jeton expiré : 200 sur /api/news et sur le détail (route anonyme, pas de 401)")]
    public async Task Expired_token_is_ignored()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin);
        var expired = ExpiredToken();

        foreach (var url in new[] { "/api/news", $"/api/news/{id}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expired);
            Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(request)).StatusCode);
        }
    }

    [Fact(DisplayName = "Membre connecté : la liste et le détail répondent comme pour un visiteur")]
    public async Task Member_reads_like_visitor()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedNewsAsync(admin);
        var member = (await RegisterMemberAsync()).Token;

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/news/{id}", member)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, "/api/news/abc", member)).StatusCode);
    }

    [Fact(DisplayName = "Fuseau : publication à 23:30Z → publishedAt renvoyé en UTC (…T23:30:00+00:00), conversion par le front")]
    public async Task Published_at_is_utc()
    {
        var admin = await AdminTokenAsync();
        var lateEvening = new DateTimeOffset(Start.UtcDateTime.Date.AddDays(1).AddHours(23).AddMinutes(30), TimeSpan.Zero);
        var draft = (await CreateNewsAsync(admin)).GetProperty("id").GetGuid();
        Clock.SetUtcNow(lateEvening);
        await SendAsync(HttpMethod.Post, $"/api/admin/news/{draft}/publish", admin);

        var item = (await PublicListAsync()).GetProperty("items")[0];

        Assert.Equal(lateEvening, item.GetProperty("publishedAt").GetDateTimeOffset());
        Assert.EndsWith("T23:30:00+00:00", item.GetProperty("publishedAt").GetString(), StringComparison.Ordinal);
    }

    private static string ExpiredToken()
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "aaas-api",
            Audience = "aaas-front",
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", "User")]),
            IssuedAt = now.AddHours(-3),
            NotBefore = now.AddHours(-3),
            Expires = now.AddMinutes(-10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(ApiFactory.SigningKey)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
