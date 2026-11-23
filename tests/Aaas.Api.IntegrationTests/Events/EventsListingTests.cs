using System.Net;
using System.Text.Json;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Events;

/// <summary>Liste publique et admin (US-C1) : pagination, tri stable, événement annulé, prix.</summary>
public sealed class EventsListingTests(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    [Fact(DisplayName = "Pagination : 25 événements, pageSize=10 → pages de 10, 10, 5 ; page 4 vide ; aucun doublon ni oubli")]
    public async Task Pagination_over_25_events_is_complete_without_duplicates()
    {
        var admin = await AdminTokenAsync();
        var expected = await CreateManyPublishedAsync(admin, 25);

        var pages = new List<JsonElement>();
        for (var page = 1; page <= 4; page++)
        {
            var response = await SendAsync(HttpMethod.Get, $"/api/events?page={page}&pageSize=10", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            pages.Add(await ReadJsonAsync(response));
        }

        Assert.All(pages, p => Assert.Equal(25, p.GetProperty("totalCount").GetInt32()));
        Assert.Equal([10, 10, 5, 0], pages.Select(p => p.GetProperty("items").GetArrayLength()));
        Assert.Equal([1, 2, 3, 4], pages.Select(p => p.GetProperty("page").GetInt32()));
        var ids = pages.SelectMany(p => p.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(25, ids.Distinct().Count());
        Assert.Equivalent(expected, ids, strict: false);
    }

    [Fact(DisplayName = "Tri : par début croissant ; à début identique, l'ordre est stable entre deux appels et d'une page à l'autre")]
    public async Task Sort_is_by_start_and_stable_on_ties()
    {
        var admin = await AdminTokenAsync();
        await CreateManyPublishedAsync(admin, 25); // 5 groupes de 5 événements au même début

        async Task<List<(Guid Id, DateTimeOffset Start)>> ReadAllAsync(int pageSize)
        {
            var all = new List<(Guid, DateTimeOffset)>();
            for (var page = 1; all.Count < 25 && page <= 25; page++)
            {
                var json = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/events?page={page}&pageSize={pageSize}", null));
                all.AddRange(json.GetProperty("items").EnumerateArray()
                    .Select(i => (i.GetProperty("id").GetGuid(), i.GetProperty("startsAt").GetDateTimeOffset())));
            }

            return all;
        }

        var first = await ReadAllAsync(10);
        var second = await ReadAllAsync(10);
        var oneByOne = await ReadAllAsync(1); // pages de 1 : le moindre tri instable ferait des doublons ou des trous

        Assert.Equal(first.Select(x => x.Start).Order(), first.Select(x => x.Start));
        Assert.Equal(first.Select(x => x.Id), second.Select(x => x.Id));
        Assert.Equal(first.Select(x => x.Id), oneByOne.Select(x => x.Id));
    }

    [Fact(DisplayName = "Événement annulé (UPDATE Status = 3) : listé et détaillé avec status Cancelled, non réservable, non supprimable")]
    public async Task Cancelled_event_is_visible_publicly()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedEventAsync(admin);
        Assert.Equal(1, await Factory.ExecuteAsync("UPDATE Events SET Status = 3 WHERE Id = @id", ("@id", id)));

        var list = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/events", null));
        var item = list.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("Cancelled", item.GetProperty("status").GetString());
        Assert.False(item.GetProperty("isBookable").GetBoolean());

        var detail = await SendAsync(HttpMethod.Get, $"/api/events/{id}", null);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal("Cancelled", (await ReadJsonAsync(detail)).GetProperty("status").GetString());

        var adminView = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{id}", admin));
        Assert.False(adminView.GetProperty("canBeDeleted").GetBoolean());
    }

    [Fact(DisplayName = "Événement annulé : ni modifiable (event_cancelled), ni publiable, ni dépubliable, ni supprimable")]
    public async Task Cancelled_event_refuses_admin_actions()
    {
        var admin = await AdminTokenAsync();
        var id = await CreatePublishedEventAsync(admin);
        await Factory.ExecuteAsync("UPDATE Events SET Status = 3 WHERE Id = @id", ("@id", id));
        var version =(await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{id}", admin))).GetProperty("version").GetString();

        var update = await SendAsync(HttpMethod.Put, $"/api/admin/events/{id}", admin, EventBody(version: version));
        Assert.Equal("event_cancelled", (await ReadJsonAsync(update)).GetProperty("code").GetString());
        var publish = await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/publish", admin);
        Assert.Equal("event_cancelled", (await ReadJsonAsync(publish)).GetProperty("code").GetString());
        var unpublish = await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/unpublish", admin);
        Assert.Equal("event_cancelled", (await ReadJsonAsync(unpublish)).GetProperty("code").GetString());
        var delete = await SendAsync(HttpMethod.Delete, $"/api/admin/events/{id}", admin);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("event_already_published", (await ReadJsonAsync(delete)).GetProperty("code").GetString());
    }

    [Fact(DisplayName = "Liste admin : brouillons inclus, filtre status=Draft, canBeDeleted selon l'historique de publication")]
    public async Task Admin_list_includes_drafts_and_filters_by_status()
    {
        var admin = await AdminTokenAsync();
        var draft = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();
        var published = await CreatePublishedEventAsync(admin);

        var all = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/events", admin))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, all.Count);
        Assert.True(all.Single(i => i.GetProperty("id").GetGuid() == draft).GetProperty("canBeDeleted").GetBoolean());
        Assert.False(all.Single(i => i.GetProperty("id").GetGuid() == published).GetProperty("canBeDeleted").GetBoolean());

        var drafts = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/events?status=Draft", admin))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(draft, Assert.Single(drafts).GetProperty("id").GetGuid());
    }

    [Theory(DisplayName = "Prix : 0 → null (gratuit), 12.500 → 12.5, 12.5001 → 400 errors.price")]
    [InlineData("0", null, HttpStatusCode.Created)]
    [InlineData("12.500", 12.5, HttpStatusCode.Created)]
    [InlineData("12.5001", null, HttpStatusCode.BadRequest)]
    public async Task Price_normalisation(string rawPrice, double? expectedPrice, HttpStatusCode expectedStatus)
    {
        var admin = await AdminTokenAsync();
        var start = DateTimeOffset.UtcNow.AddDays(5);
        var json = $$"""
            {"title":"T","description":"D","location":"L","startsAt":"{{Iso(start)}}","endsAt":"{{Iso(start.AddHours(1))}}","capacity":5,"price":{{rawPrice}}}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/events")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin);

        var response = await Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        var body = await ReadJsonAsync(response);
        if (expectedStatus == HttpStatusCode.Created)
        {
            var price = body.GetProperty("price");
            if (expectedPrice is null)
            {
                Assert.Equal(JsonValueKind.Null, price.ValueKind);
            }
            else
            {
                Assert.Equal(expectedPrice.Value, price.GetDouble());
            }
        }
        else
        {
            Assert.True(body.GetProperty("errors").TryGetProperty("price", out _));
        }
    }

    /// <summary>Crée <paramref name="count"/> événements publiés ; ils partagent leur début par groupes de 5 (égalités à départager).</summary>
    private async Task<List<Guid>> CreateManyPublishedAsync(string admin, int count)
    {
        var ids = new List<Guid>();
        var baseStart = DateTimeOffset.UtcNow.AddDays(10);
        for (var i = 0; i < count; i++)
        {
            var start = baseStart.AddDays(i / 5);
            ids.Add((await CreateEventAsync(admin, EventBodyAt(start, start.AddHours(2), title: $"Événement {i}"))).GetProperty("id").GetGuid());
        }

        // Publication directe en base : seul l'état compte ici, et on évite 25 requêtes de plus.
        await Factory.ExecuteAsync("UPDATE Events SET Status = 2, PublishedAt = SYSDATETIMEOFFSET(), FirstPublishedAt = SYSDATETIMEOFFSET()");
        return ids;
    }
}
