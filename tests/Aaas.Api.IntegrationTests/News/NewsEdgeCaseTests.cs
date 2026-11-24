using System.Net;
using System.Text.Json;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.News;

/// <summary>
/// Cas limites des actualités (US-B1, US-B2, US-E1) qui complètent AdminNewsTests, PublicNewsTests et NewsAccessTests :
/// course entre une suppression, une modification et une publication ; limites en unités UTF-16 ; casse du filtre ;
/// pages au-delà de la dernière ; tri stable ; confidentialité des journaux.
/// </summary>
public sealed class NewsEdgeCaseTests(SqlServerFixture sql) : NewsApiTestBase(sql)
{
    // Niveau Information : sans lui, les journaux de requêtes ne sont pas émis et le test de confidentialité ne prouverait rien.
    protected override Dictionary<string, string?>? ConfigurationOverrides => new(base.ConfigurationOverrides!)
    {
        ["Logging:LogLevel:Default"] = "Information",
        ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Information",
    };

    private const string Rocket = "\U0001F680"; // 2 unités UTF-16

    private async Task<string> VersionOfAsync(string admin, Guid id) =>
        (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/news/{id}", admin))).GetProperty("version").GetString()!;

    // ---- Suppression / modification / publication ----

    [Fact(DisplayName = "PUT sur une actualité supprimée entre-temps → 404 « Actualité introuvable. » (pas 409, pas 500), rien n'est recréé")]
    public async Task Put_after_delete_returns_404()
    {
        var admin = await AdminTokenAsync();
        var created = await CreateNewsAsync(admin);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/admin/news/{id}", admin)).StatusCode);

        var response = await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("Tardif", "Texte tardif", version));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("not_found", json.GetProperty("code").GetString());
        Assert.Equal("Actualité introuvable.", json.GetProperty("title").GetString());
        Assert.Equal(0, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM News"));
    }

    [Fact(DisplayName = "PUT avec un texte de 8 001 caractères sur une actualité supprimée → 404 d'abord (not_found passe avant le 400 de valeurs)")]
    public async Task Put_invalid_after_delete_returns_404_not_400()
    {
        var admin = await AdminTokenAsync();
        var created = await CreateNewsAsync(admin);
        var id = created.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Delete, $"/api/admin/news/{id}", admin);

        var response = await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("T", new string('a', 8001), created.GetProperty("version").GetString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "Publier ou dépublier une actualité supprimée entre-temps → 404")]
    public async Task Transitions_after_delete_return_404()
    {
        var admin = await AdminTokenAsync();
        var id = (await CreateNewsAsync(admin)).GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Delete, $"/api/admin/news/{id}", admin);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/unpublish", admin)).StatusCode);
    }

    [Fact(DisplayName = "Concurrence : PUT et DELETE simultanés → aucune erreur 5xx, issue cohérente (fiche supprimée ou modifiée, jamais les deux)")]
    [Trait("Category", "Concurrency")]
    public async Task Concurrent_put_and_delete_are_consistent()
    {
        var admin = await AdminTokenAsync();
        for (var round = 0; round < 4; round++)
        {
            var created = await CreateNewsAsync(admin, $"Fiche {round}");
            var id = created.GetProperty("id").GetGuid();
            var version = created.GetProperty("version").GetString();

            var put = SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("Modifiée", "x", version));
            var delete = SendAsync(HttpMethod.Delete, $"/api/admin/news/{id}", admin);
            var responses = await Task.WhenAll(put, delete);

            Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"Tour {round} : {r.StatusCode}"));
            Assert.Contains(responses[1].StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict, HttpStatusCode.NotFound });
            Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.NotFound });
            var remaining = await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM News WHERE Id = @id", ("@id", id));
            Assert.Equal(responses[1].StatusCode == HttpStatusCode.NoContent ? 0 : 1, remaining);
        }
    }

    [Fact(DisplayName = "Concurrence : publier pendant un PUT avec la même version → aucune écriture perdue ni 5xx ; au moins une réussit")]
    [Trait("Category", "Concurrency")]
    public async Task Publish_during_concurrent_put_loses_no_write()
    {
        var admin = await AdminTokenAsync();
        for (var round = 0; round < 5; round++)
        {
            var created = await CreateNewsAsync(admin, $"Fiche {round}");
            var id = created.GetProperty("id").GetGuid();
            var version = created.GetProperty("version").GetString();

            var responses = await Task.WhenAll(
                SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody($"Modifiée {round}", "Nouveau texte", version)),
                SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin));
            var (put, publish) = (responses[0], responses[1]);

            Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            Assert.Contains(HttpStatusCode.OK, responses.Select(r => r.StatusCode));
            foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            {
                Assert.Equal("concurrent_update", (await ReadJsonAsync(conflict)).GetProperty("code").GetString());
            }

            // Chaque 200 doit se retrouver en base : ni mise à jour perdue, ni publication perdue.
            var title = await Factory.ScalarAsync<string>("SELECT Title FROM News WHERE Id = @id", ("@id", id));
            var status = await Factory.ScalarAsync<byte>("SELECT Status FROM News WHERE Id = @id", ("@id", id));
            Assert.Equal(put.StatusCode == HttpStatusCode.OK ? $"Modifiée {round}" : $"Fiche {round}", title);
            Assert.Equal(publish.StatusCode == HttpStatusCode.OK ? (byte)2 : (byte)1, status);
            Assert.Equal(status == 2 ? HttpStatusCode.OK : HttpStatusCode.NotFound,
                (await SendAsync(HttpMethod.Get, $"/api/news/{id}", null)).StatusCode);
        }
    }

    [Fact(DisplayName = "Concurrence : publier puis PUT avec la version d'avant la publication → 409 ; la publication reste")]
    public async Task Put_with_version_from_before_publish_conflicts()
    {
        var admin = await AdminTokenAsync();
        var created = await CreateNewsAsync(admin);
        var id = created.GetProperty("id").GetGuid();
        await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin);

        var response = await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("T", "x", created.GetProperty("version").GetString()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("concurrent_update", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/news/{id}", null)).StatusCode);
    }

    // ---- Limites en unités UTF-16, espaces et \r\n ----

    [Fact(DisplayName = "Limites : 4 000 émojis (8 000 unités UTF-16) → 201 relu à l'identique ; 4 001 → 400 « 8 000 caractères »")]
    public async Task Emoji_content_is_counted_in_utf16_units()
    {
        var admin = await AdminTokenAsync();
        var exact = string.Concat(Enumerable.Repeat(Rocket, 4000));

        var created = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, NewsBody("Émojis", exact));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        Assert.Equal(exact, await Factory.ScalarAsync<string>("SELECT Content FROM News WHERE Id = @id", ("@id", id)));
        Assert.Equal(exact, (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/news/{id}", admin))).GetProperty("content").GetString());

        var tooLong = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, NewsBody("Émojis", exact + Rocket));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("Le texte ne doit pas dépasser 8 000 caractères.",
            (await ReadJsonAsync(tooLong)).GetProperty("errors").GetProperty("content")[0].GetString());
    }

    [Fact(DisplayName = "Limites : PUT avec 4 000 émojis accepté (200), avec 4 001 refusé (400)")]
    public async Task Emoji_content_limit_on_update()
    {
        var admin = await AdminTokenAsync();
        var created = await CreateNewsAsync(admin);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();
        var exact = string.Concat(Enumerable.Repeat(Rocket, 4000));

        var tooLong = await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("T", exact + Rocket, version));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var ok = await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody("T", exact, version));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact(DisplayName = "Limites : émojis + \\r\\n + espaces de début et de fin = 8 000 après normalisation → 201 ; un caractère de plus → 400")]
    public async Task Emoji_crlf_and_padding_boundary()
    {
        var admin = await AdminTokenAsync();
        var body = string.Concat(Enumerable.Repeat(Rocket + "\r\n", 2000)) + new string('a', 1999) + "b"; // 6 000 + 2 000 normalisés
        var padded = "  \r\n\t" + body + "\r\n   ";

        var ok = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, NewsBody("T", padded));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var stored = (await ReadJsonAsync(ok)).GetProperty("content").GetString()!;
        Assert.Equal(8000, stored.Length);
        Assert.DoesNotContain('\r', stored);
        Assert.False(char.IsWhiteSpace(stored[0]));
        Assert.False(char.IsWhiteSpace(stored[^1]));

        var tooLong = await SendAsync(HttpMethod.Post, "/api/admin/news", admin, NewsBody("T", padded + "c"));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact(DisplayName = "Contenu avec espaces de début/fin et \\r\\n : stocké normalisé (\\n, sans espaces autour), titre rogné, paragraphes conservés")]
    public async Task Padded_crlf_content_is_normalized()
    {
        var admin = await AdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/admin/news", admin,
            NewsBody("\t  Titre  \r\n", "  \r\n\r\nPremier\r\nsuite\r\n\r\nSecond\r  \r\n"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await ReadJsonAsync(response);
        var id = json.GetProperty("id").GetGuid();
        Assert.Equal("Titre", json.GetProperty("title").GetString());
        Assert.Equal("Premier\nsuite\n\nSecond", json.GetProperty("content").GetString());
        Assert.Equal("Premier\nsuite\n\nSecond", await Factory.ScalarAsync<string>("SELECT Content FROM News WHERE Id = @id", ("@id", id)));

        var version = json.GetProperty("version").GetString();
        var updated = await ReadJsonAsync(await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody(" Titre 2 ", "\r\n A\r\n\r\nB \r\n", version)));
        Assert.Equal("Titre 2", updated.GetProperty("title").GetString());
        Assert.Equal("A\n\nB", updated.GetProperty("content").GetString());
    }

    [Fact(DisplayName = "Extrait : un émoji à cheval sur la position 200 n'est jamais coupé en deux (pas de paire de substitution orpheline)")]
    public async Task Excerpt_never_splits_a_surrogate_pair()
    {
        var admin = await AdminTokenAsync();
        await CreatePublishedNewsAsync(admin, "Émoji", new string('a', 199) + Rocket + " " + string.Concat(Enumerable.Repeat("fin ", 200)));

        var excerpt = (await PublicListAsync()).GetProperty("items")[0].GetProperty("excerpt").GetString()!;

        Assert.EndsWith("…", excerpt, StringComparison.Ordinal);
        for (var i = 0; i < excerpt.Length; i++)
        {
            if (char.IsHighSurrogate(excerpt[i]))
            {
                Assert.True(i + 1 < excerpt.Length && char.IsLowSurrogate(excerpt[i + 1]), "Paire de substitution coupée.");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(excerpt[i]), "Moitié basse orpheline.");
            }
        }
    }

    // ---- Filtre et pagination ----

    [Theory(DisplayName = "Liste admin : statut insensible à la casse (draft, DRAFT, published) → 200 et bon filtre")]
    [InlineData("draft", "Draft")]
    [InlineData("DRAFT", "Draft")]
    [InlineData("Draft", "Draft")]
    [InlineData("published", "Published")]
    [InlineData("PUBLISHED", "Published")]
    public async Task Status_filter_is_case_insensitive(string value, string expected)
    {
        var admin = await AdminTokenAsync();
        await CreateNewsAsync(admin, "Brouillon");
        await CreatePublishedNewsAsync(admin, "Publiée");

        var response = await SendAsync(HttpMethod.Get, $"/api/admin/news?status={value}", admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadJsonAsync(response);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(expected, page.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact(DisplayName = "Liste admin : statut inconnu → 400 sur « status », jamais 500")]
    public async Task Unknown_status_returns_400()
    {
        var admin = await AdminTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/admin/news?status=archived", admin);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("status", out _));
    }

    [Fact(DisplayName = "Liste admin : page au-delà de la dernière → 200, items vide, totalCount exact (filtré ou non)")]
    public async Task Admin_page_beyond_last_is_empty_with_exact_total()
    {
        var admin = await AdminTokenAsync();
        for (var i = 0; i < 12; i++)
        {
            Clock.Advance(TimeSpan.FromMinutes(1));
            if (i % 3 == 0)
            {
                await CreatePublishedNewsAsync(admin, $"P{i}");
            }
            else
            {
                await CreateNewsAsync(admin, $"B{i}");
            }
        }

        var all = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/news?pageSize=10&page=3", admin));
        Assert.Equal(0, all.GetProperty("items").GetArrayLength());
        Assert.Equal(12, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, all.GetProperty("page").GetInt32());

        var drafts = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/news?status=draft&pageSize=5&page=9", admin));
        Assert.Equal(0, drafts.GetProperty("items").GetArrayLength());
        Assert.Equal(8, drafts.GetProperty("totalCount").GetInt32());

        var far = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/news?page=10000", admin));
        Assert.Equal(0, far.GetProperty("items").GetArrayLength());
        Assert.Equal(12, far.GetProperty("totalCount").GetInt32());
    }

    [Fact(DisplayName = "Liste publique : page au-delà de la dernière → 200, items vide, totalCount exact ; page 10 000 aussi")]
    public async Task Public_page_beyond_last_is_empty_with_exact_total()
    {
        var admin = await AdminTokenAsync();
        for (var i = 0; i < 11; i++)
        {
            await CreatePublishedNewsAsync(admin, $"N{i}");
        }

        var page3 = await PublicListAsync("?page=3");
        Assert.Equal(0, page3.GetProperty("items").GetArrayLength());
        Assert.Equal(11, page3.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await PublicListAsync("?page=2")).GetProperty("items").GetArrayLength());

        var far = await PublicListAsync("?page=10000");
        Assert.Equal(0, far.GetProperty("items").GetArrayLength());
        Assert.Equal(11, far.GetProperty("totalCount").GetInt32());
    }

    [Fact(DisplayName = "Liste publique : 25 publiées avec des dates à égalité (par groupes de 5), 10 par page → ni doublon ni trou, dates décroissantes, ordre identique à la relecture")]
    public async Task Public_pagination_has_no_duplicate_nor_gap_with_ties()
    {
        var admin = await AdminTokenAsync();
        var expected = new HashSet<Guid>();
        for (var i = 0; i < 25; i++)
        {
            if (i % 5 == 0)
            {
                Clock.Advance(TimeSpan.FromHours(1));
            }

            expected.Add(await CreatePublishedNewsAsync(admin, $"Actualité {i}"));
        }

        async Task<List<(Guid Id, DateTimeOffset At)>> ReadAllAsync()
        {
            var all = new List<(Guid, DateTimeOffset)>();
            for (var p = 1; p <= 3; p++)
            {
                var page = await PublicListAsync($"?page={p}");
                Assert.Equal(25, page.GetProperty("totalCount").GetInt32());
                Assert.Equal(p < 3 ? 10 : 5, page.GetProperty("items").GetArrayLength());
                all.AddRange(page.GetProperty("items").EnumerateArray()
                    .Select(i => (i.GetProperty("id").GetGuid(), i.GetProperty("publishedAt").GetDateTimeOffset())));
            }

            return all;
        }

        var first = await ReadAllAsync();
        var second = await ReadAllAsync();

        Assert.Equal(25, first.Select(x => x.Id).Distinct().Count());
        Assert.True(expected.SetEquals(first.Select(x => x.Id)), "Une actualité manque ou est en trop.");
        Assert.Equal(first.OrderByDescending(x => x.At).Select(x => x.At), first.Select(x => x.At));
        Assert.Equal(first.Select(x => x.Id), second.Select(x => x.Id));
    }

    [Fact(DisplayName = "Liste admin : 25 actualités modifiées au même instant, 10 par page → ni doublon ni trou, ordre stable")]
    public async Task Admin_pagination_has_no_duplicate_nor_gap_with_ties()
    {
        var admin = await AdminTokenAsync();
        var expected = new HashSet<Guid>();
        for (var i = 0; i < 25; i++)
        {
            expected.Add((await CreateNewsAsync(admin, $"Brouillon {i}")).GetProperty("id").GetGuid()); // horloge immobile
        }

        async Task<List<Guid>> ReadAllAsync()
        {
            var ids = new List<Guid>();
            for (var p = 1; p <= 3; p++)
            {
                var page = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/news?pageSize=10&page={p}", admin));
                Assert.Equal(25, page.GetProperty("totalCount").GetInt32());
                ids.AddRange(Ids(page));
            }

            return ids;
        }

        var first = await ReadAllAsync();
        var second = await ReadAllAsync();

        Assert.Equal(25, first.Distinct().Count());
        Assert.True(expected.SetEquals(first));
        Assert.Equal(first, second);
    }

    [Fact(DisplayName = "Republication : la fiche garde sa date d'origine et donc sa place dans la liste publique (pas en tête)")]
    public async Task Republished_keeps_its_place_in_public_list()
    {
        var admin = await AdminTokenAsync();
        Clock.Advance(TimeSpan.FromHours(1));
        var older = await CreatePublishedNewsAsync(admin, "Ancienne");
        var originalDate = Clock.GetUtcNow();
        Clock.Advance(TimeSpan.FromDays(1));
        var newer = await CreatePublishedNewsAsync(admin, "Récente");
        await SendAsync(HttpMethod.Post, $"/api/admin/news/{older}/unpublish", admin);
        Assert.Equal([newer], Ids(await PublicListAsync()));

        Clock.Advance(TimeSpan.FromDays(5));
        var republished = await ReadJsonAsync(await SendAsync(HttpMethod.Post, $"/api/admin/news/{older}/publish", admin));

        Assert.Equal(originalDate, republished.GetProperty("publishedAt").GetDateTimeOffset());
        var list = await PublicListAsync();
        Assert.Equal([newer, older], Ids(list));
        Assert.Equal(originalDate, list.GetProperty("items")[1].GetProperty("publishedAt").GetDateTimeOffset());
    }

    [Fact(DisplayName = "Republication après deux cycles de dépublication : PublishedAt toujours celle de la première publication")]
    public async Task Published_at_survives_several_cycles()
    {
        var admin = await AdminTokenAsync();
        var id = (await CreateNewsAsync(admin)).GetProperty("id").GetGuid();
        Clock.Advance(TimeSpan.FromHours(2));
        var first = Clock.GetUtcNow();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var published = await ReadJsonAsync(await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin));
            Assert.Equal(first, published.GetProperty("publishedAt").GetDateTimeOffset());
            await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/unpublish", admin);
            Clock.Advance(TimeSpan.FromDays(3));
        }

        Assert.Equal(first, (await Factory.ScalarAsync<DateTimeOffset?>("SELECT PublishedAt FROM News WHERE Id = @id", ("@id", id)))!.Value);
    }

    // ---- Identifiants mal formés, 404 public ----

    [Theory(DisplayName = "Identifiant mal formé sur la route publique : 404 « Contenu introuvable. » (jamais 400 ni 500)")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("00000000-0000-0000-0000-00000000000")] // un chiffre de moins
    [InlineData("%20")]
    [InlineData("1;DROP%20TABLE%20News")]
    [InlineData("%27%20OR%201=1--")]
    public async Task Public_malformed_id_returns_404(string id)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/news/{id}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("not_found", json.GetProperty("code").GetString());
        Assert.Equal("Contenu introuvable.", json.GetProperty("title").GetString());
    }

    [Fact(DisplayName = "404 public (brouillon, inconnue, mal formé) : réponse non mise en cache (no-store posé par le framework sur les erreurs)")]
    public async Task Public_404_is_not_cacheable()
    {
        var admin = await AdminTokenAsync();
        var draft = (await CreateNewsAsync(admin)).GetProperty("id").GetGuid();

        foreach (var url in new[] { $"/api/news/{draft}", $"/api/news/{Guid.NewGuid()}", "/api/news/abc" })
        {
            var response = await SendAsync(HttpMethod.Get, url, null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore, $"{url} : Cache-Control = {response.Headers.CacheControl}");
        }
    }

    [Theory(DisplayName = "Admin : identifiant mal formé → visiteur 401, membre 403, admin 404 (y compris avec un id « presque » GUID)")]
    [InlineData("abc")]
    [InlineData("00000000-0000-0000-0000-00000000000")]
    [InlineData("1;DROP%20TABLE%20News")]
    public async Task Admin_malformed_id_by_role(string id)
    {
        var admin = await AdminTokenAsync();
        var member = (await RegisterMemberAsync()).Token;

        foreach (var (token, expected) in new (string?, HttpStatusCode)[]
                 {
                     (null, HttpStatusCode.Unauthorized),
                     (member, HttpStatusCode.Forbidden),
                     (admin, HttpStatusCode.NotFound),
                 })
        {
            var get = await SendAsync(HttpMethod.Get, $"/api/admin/news/{id}", token);
            var publish = await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", token);
            Assert.Equal(expected, get.StatusCode);
            Assert.Equal(expected, publish.StatusCode);
            Assert.True(get.Headers.CacheControl?.NoStore);
        }
    }

    // ---- Journaux ----

    [Fact(DisplayName = "Journaux : ni titre, ni texte, ni e-mail, ni nom, ni téléphone de l'admin ou du membre pendant les opérations sur les actualités")]
    public async Task Logs_contain_no_news_text_nor_personal_data()
    {
        const string title = "Titre-Confidentiel-4d8a";
        const string content = "Texte-Confidentiel-5e9b <script>alert(1)</script>";
        var admin = await AdminTokenAsync();
        var (memberEmail, memberToken, _) = await RegisterMemberAsync();
        var before = Factory.Logs.Messages.Count; // l'inscription et la connexion ne sont pas l'objet de ce test

        var created = await CreateNewsAsync(admin, title, content);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();
        await SendAsync(HttpMethod.Post, "/api/admin/news", admin, new { title = "  ", content = content + "-invalide" }); // 400
        await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin);
        await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/publish", admin); // 409
        await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody(title + "-2", content + "-2", version)); // 409
        await SendAsync(HttpMethod.Put, $"/api/admin/news/abc", memberToken, NewsBody(title, content)); // 403
        await SendAsync(HttpMethod.Get, "/api/admin/news?status=draft", admin);
        await SendAsync(HttpMethod.Get, "/api/news", memberToken);
        await SendAsync(HttpMethod.Get, $"/api/news/{id}", memberToken);
        await SendAsync(HttpMethod.Get, "/api/news/abc", null);
        await SendAsync(HttpMethod.Post, $"/api/admin/news/{id}/unpublish", admin);
        await SendAsync(HttpMethod.Get, $"/api/news/{id}", null); // 404
        await SendAsync(HttpMethod.Delete, $"/api/admin/news/{id}", admin);
        await SendAsync(HttpMethod.Put, $"/api/admin/news/{id}", admin, NewsBody(title, content, version)); // 404

        var logged = Factory.Logs.Messages.Skip(before).ToList();
        var logs = string.Join("\n", logged);
        Assert.NotEmpty(logged);
        foreach (var secret in new[] { "Confidentiel-4d8a", "Confidentiel-5e9b", "alert(1)", "-invalide", AdminEmail, memberEmail, ValidPhone, "+216 71 000 000", "Salma", "Ben Ali" })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
        }
    }
}
